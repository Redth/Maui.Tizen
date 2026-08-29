using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Tizen.Uix.Tts;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ITextToSpeech"/>, backed by <c>Tizen.Uix.Tts</c>.
	/// </summary>
	/// <remarks>
	/// Tizen's TTS client is bound to the Ecore main loop. Construction, queries, playback and
	/// teardown are therefore all marshalled through the MAUI main-thread dispatcher.
	/// </remarks>
	public sealed class TizenTextToSpeech : ITextToSpeech, IDisposable
	{
		const float RateMax = 2.0f;

		readonly SemaphoreSlim _speakLock = new(1, 1);
		readonly object _stateLock = new();
		readonly ITizenTextToSpeechDispatcher _dispatcher;
		readonly ITizenTextToSpeechClientFactory _clientFactory;

		ClientState? _clientState;
		ClientState? _initializingState;
		Task<ClientState>? _initialization;
		ActiveUtterance? _activeUtterance;
		TaskCompletionSource<bool>? _readiness;
		TaskCompletionSource<bool>? _utterance;
		bool _disposed;

		/// <summary>Creates a Tizen text-to-speech service.</summary>
		public TizenTextToSpeech()
			: this(TizenTextToSpeechDispatcher.Instance, TizenTextToSpeechClientFactory.Instance)
		{
		}

		internal TizenTextToSpeech(
			ITizenTextToSpeechDispatcher dispatcher,
			ITizenTextToSpeechClientFactory clientFactory)
		{
			_dispatcher = dispatcher;
			_clientFactory = clientFactory;
		}

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown; see remarks.</exception>
		/// <remarks>
		/// <c>Microsoft.Maui.Media.Locale</c> has no public constructor in the currently published
		/// Essentials package. Use <see cref="GetSupportedVoiceLanguagesAsync"/> and
		/// <see cref="SpeakWithVoiceAsync"/> until the public Locale API is packaged.
		/// </remarks>
		public Task<IEnumerable<Locale>> GetLocalesAsync() =>
			throw TizenEssentialsSupport.NotSupported(
				$"{nameof(ITextToSpeech)}.{nameof(GetLocalesAsync)}",
				"Microsoft.Maui.Media.Locale exposes no public constructor, so a standalone platform " +
				"backend cannot create the Locale values this contract returns. " +
				$"Use {nameof(TizenTextToSpeech)}.{nameof(GetSupportedVoiceLanguagesAsync)} instead.");

		/// <summary>Enumerates the voice languages Tizen reports for the current device.</summary>
		public async Task<IReadOnlyList<string>> GetSupportedVoiceLanguagesAsync()
		{
			var languages = new List<string>();

			await RunWithCurrentClientAsync(
				_speakLock,
				CancellationToken.None,
				InitializeAsync,
				async state =>
				{
					var voices = await _dispatcher.InvokeAsync(() =>
					{
						ThrowIfNativeCallInvalid(state, CancellationToken.None);
						return state.Client!.GetSupportedVoices();
					}).ConfigureAwait(false);
					foreach (var voice in voices)
					{
						if (!languages.Any(value =>
							string.Equals(value, voice.Language, StringComparison.OrdinalIgnoreCase)))
						{
							languages.Add(voice.Language);
						}
					}
				}).ConfigureAwait(false);

			return languages;
		}

		/// <summary>Speaks text using an explicit Tizen voice language.</summary>
		public Task SpeakWithVoiceAsync(
			string text,
			string language,
			float? rate = null,
			CancellationToken cancelToken = default) =>
			SpeakCoreAsync(text, language, rate, cancelToken);

		/// <inheritdoc/>
		public async Task SpeakAsync(
			string text,
			SpeechOptions? options = default,
			CancellationToken cancelToken = default)
		{
			ArgumentNullException.ThrowIfNull(text);

			if (options?.Pitch is not null)
			{
				throw TizenEssentialsSupport.NotSupported(
					$"{nameof(ITextToSpeech)}.{nameof(SpeakAsync)} with SpeechOptions.Pitch",
					"Tizen's TTS client exposes no pitch control.");
			}

			if (options?.Volume is not null)
			{
				throw TizenEssentialsSupport.NotSupported(
					$"{nameof(ITextToSpeech)}.{nameof(SpeakAsync)} with SpeechOptions.Volume",
					"Tizen's TTS client exposes no per-utterance volume control.");
			}

			await SpeakCoreAsync(text, options?.Locale?.Language, options?.Rate, cancelToken)
				.ConfigureAwait(false);
		}

		async Task SpeakCoreAsync(
			string text,
			string? language,
			float? rate,
			CancellationToken cancelToken)
		{
			ArgumentNullException.ThrowIfNull(text);
			cancelToken.ThrowIfCancellationRequested();

			await RunWithCurrentClientAsync(
				_speakLock,
				cancelToken,
				InitializeAsync,
				async state =>
				{
					var completion = new TaskCompletionSource<bool>(
						TaskCreationOptions.RunContinuationsAsynchronously);
					SetPendingUtterance(state, completion);

					try
					{
						using var registration = cancelToken.Register(
							() => CancelClientState(state, completion, cancelToken));
						cancelToken.ThrowIfCancellationRequested();

						var utteranceId = await _dispatcher.InvokeAsync(() =>
						{
							ThrowIfNativeCallInvalid(state, cancelToken);
							var (resolvedLanguage, voiceType) = ResolveVoice(state.Client!, language);
							return state.Client!.AddText(
								text,
								resolvedLanguage,
								voiceType,
								ResolveRate(state, rate));
						}).ConfigureAwait(false);

						SetActiveUtterance(state, utteranceId, completion);
						cancelToken.ThrowIfCancellationRequested();

						try
						{
							await _dispatcher.InvokeAsync(() =>
							{
								ThrowIfNativeCallInvalid(state, cancelToken);
								state.Client!.Play();
							}).ConfigureAwait(false);
						}
						catch
						{
							if (RetireClientState(state))
								DispatchTeardown(state, stop: true);
							throw;
						}

						await completion.Task.ConfigureAwait(false);
					}
					catch (Exception) when (cancelToken.IsCancellationRequested)
					{
						cancelToken.ThrowIfCancellationRequested();
						throw;
					}
					finally
					{
						ClearActiveUtterance(state, completion);
					}
				}).ConfigureAwait(false);
		}

		internal static async Task RunWithCurrentClientAsync<TClient>(
			SemaphoreSlim speakLock,
			CancellationToken cancelToken,
			Func<CancellationToken, Task<TClient>> initialize,
			Func<TClient, Task> operation)
		{
			await speakLock.WaitAsync(cancelToken).ConfigureAwait(false);
			try
			{
				cancelToken.ThrowIfCancellationRequested();
				var client = await initialize(cancelToken).ConfigureAwait(false);
				cancelToken.ThrowIfCancellationRequested();
				await operation(client).ConfigureAwait(false);
			}
			finally
			{
				speakLock.Release();
			}
		}

		async Task<ClientState> InitializeAsync(CancellationToken cancelToken)
		{
			ClientState state;
			var initialize = false;

			lock (_stateLock)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);

				if (_clientState is { Retired: false } current)
					return current;

				if (_initialization is null)
				{
					state = new ClientState();
					_initializingState = state;
					_initialization = state.Initialization.Task;
					_readiness = state.Readiness;
					initialize = true;
				}
				else
				{
					state = _initializingState!;
				}
			}

			if (initialize)
			{
				try
				{
					await _dispatcher.InvokeAsync(() => PrepareOnMainThread(state)).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					RetireClientState(state);
					state.Readiness.TrySetException(exception);
					state.Initialization.TrySetException(exception);
					DispatchTeardown(state, stop: false);
				}
			}

			return await state.Initialization.Task.WaitAsync(cancelToken).ConfigureAwait(false);
		}

		void PrepareOnMainThread(ClientState state)
		{
			lock (_stateLock)
			{
				if (state.Retired || _disposed || !ReferenceEquals(_initializingState, state))
					return;
			}

			var client = _clientFactory.Create();
			var retireLateClient = false;
			lock (_stateLock)
			{
				state.Client = client;
				retireLateClient =
					state.Retired ||
					_disposed ||
					!ReferenceEquals(_initializingState, state);
			}

			if (retireLateClient)
			{
				DispatchTeardown(state, stop: true);
				return;
			}

			client.StateChanged += OnStateChanged;
			client.UtteranceCompleted += OnUtteranceCompleted;
			client.ErrorOccurred += OnErrorOccurred;

			lock (_stateLock)
			{
				retireLateClient =
					state.Retired ||
					_disposed ||
					!ReferenceEquals(_initializingState, state);
			}

			if (retireLateClient)
			{
				DispatchTeardown(state, stop: true);
				return;
			}

			// Tizen only permits GetSpeedRange while the new client is in Created. Cache it before
			// Prepare transitions the client, then apply the caller's rate after Ready.
			state.MaximumSpeed = client.GetMaximumSpeed();
			client.Prepare();

			void OnStateChanged(TizenTextToSpeechState current)
			{
				if (current != TizenTextToSpeechState.Ready)
					return;

				lock (_stateLock)
				{
					if (state.Retired || _disposed || !ReferenceEquals(_initializingState, state))
						return;

					_clientState = state;
					_initializingState = null;
					_readiness = null;
				}

				state.Readiness.TrySetResult(true);
				state.Initialization.TrySetResult(state);
			}

			void OnUtteranceCompleted(int utteranceId)
			{
				_dispatcher.Post(() =>
				{
					ActiveUtterance? utterance;
					lock (_stateLock)
					{
						utterance = _activeUtterance is { } active &&
							ReferenceEquals(active.Client, state) &&
							active.UtteranceId == utteranceId
								? active
								: null;
					}

					if (utterance is null)
						return;

					StopQuietly(state.Client!);
					utterance.Completion.TrySetResult(true);
				});
			}

			void OnErrorOccurred(TizenTextToSpeechError error)
			{
				ActiveUtterance? utterance;
				lock (_stateLock)
				{
					utterance = _activeUtterance;
					if (utterance is not null &&
						(!ReferenceEquals(utterance.Client, state) ||
							utterance.UtteranceId != error.UtteranceId))
					{
						return;
					}

					if (!RetireClientStateLocked(state))
						return;
				}

				var exception = new InvalidOperationException(
					$"Tizen text-to-speech failed ({error.ErrorValue}): {error.ErrorMessage}");
				state.Readiness.TrySetException(exception);
				state.Initialization.TrySetException(exception);
				utterance?.Completion.TrySetException(exception);

				// Never destroy the native handle from inside the ErrorOccurred callback.
				DispatchTeardown(state, stop: false);
			}
		}

		void SetPendingUtterance(ClientState state, TaskCompletionSource<bool> completion)
		{
			lock (_stateLock)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);
				if (state.Retired || !ReferenceEquals(_clientState, state))
					throw new InvalidOperationException("The Tizen text-to-speech client was retired.");

				_utterance = completion;
			}
		}

		void SetActiveUtterance(
			ClientState state,
			int utteranceId,
			TaskCompletionSource<bool> completion)
		{
			lock (_stateLock)
			{
				if (state.Retired || !ReferenceEquals(_clientState, state))
					throw new InvalidOperationException("The Tizen text-to-speech client was retired.");

				_activeUtterance = new ActiveUtterance(state, utteranceId, completion);
			}
		}

		void ClearActiveUtterance(ClientState state, TaskCompletionSource<bool> completion)
		{
			lock (_stateLock)
			{
				if (_activeUtterance is { } active &&
					ReferenceEquals(active.Client, state) &&
					ReferenceEquals(active.Completion, completion))
				{
					_activeUtterance = null;
				}

				if (ReferenceEquals(_utterance, completion))
					_utterance = null;
			}
		}

		void CancelClientState(
			ClientState state,
			TaskCompletionSource<bool> completion,
			CancellationToken cancelToken)
		{
			if (RetireClientState(state))
				DispatchTeardown(state, stop: true);

			completion.TrySetCanceled(cancelToken);
		}

		bool RetireClientState(ClientState state)
		{
			lock (_stateLock)
				return RetireClientStateLocked(state);
		}

		bool RetireClientStateLocked(ClientState state)
		{
			if (state.Retired)
				return false;

			state.Retired = true;
			if (ReferenceEquals(_clientState, state))
				_clientState = null;
			if (ReferenceEquals(_initializingState, state))
				_initializingState = null;
			if (ReferenceEquals(_initialization, state.Initialization.Task))
				_initialization = null;
			if (ReferenceEquals(_readiness, state.Readiness))
				_readiness = null;
			return true;
		}

		void DispatchTeardown(ClientState state, bool stop)
		{
			lock (_stateLock)
			{
				state.TeardownRequested = true;
				state.StopBeforeTeardown |= stop;

				if (state.Client is null || state.TeardownPosted)
					return;

				state.TeardownPosted = true;
			}

			_dispatcher.Post(() =>
			{
				var client = state.Client;
				if (client is null)
					return;

				if (state.StopBeforeTeardown)
					StopQuietly(client);

				try
				{
					client.Dispose();
				}
				catch (Exception)
				{
					// Teardown is best effort and must not terminate the Ecore loop.
				}
			});
		}

		void ThrowIfNativeCallInvalid(ClientState state, CancellationToken cancelToken)
		{
			cancelToken.ThrowIfCancellationRequested();

			lock (_stateLock)
			{
				cancelToken.ThrowIfCancellationRequested();
				ObjectDisposedException.ThrowIf(_disposed, this);
				if (state.Retired || !ReferenceEquals(_clientState, state))
					throw new InvalidOperationException("The Tizen text-to-speech client was retired.");
			}
		}

		static void StopQuietly(ITizenTextToSpeechClient client)
		{
			try
			{
				client.Stop();
			}
			catch (Exception)
			{
				// Stop is best effort after cancellation, failure, or completion.
			}
		}

		static (string Language, int VoiceType) ResolveVoice(
			ITizenTextToSpeechClient client,
			string? requestedLanguage)
		{
			var language = "en_US";
			var voiceType = (int)Voice.Auto;

			if (requestedLanguage is { } requested)
			{
				foreach (var voice in client.GetSupportedVoices())
				{
					if (string.Equals(voice.Language, requested, StringComparison.OrdinalIgnoreCase))
					{
						language = voice.Language;
						voiceType = voice.VoiceType;
						break;
					}
				}
			}

			return (language, voiceType);
		}

		static int ResolveRate(ClientState state, float? rate)
		{
			if (rate is not { } value)
				return 0;

			return (int)Math.Round(
				value / RateMax * state.MaximumSpeed,
				MidpointRounding.AwayFromZero);
		}

		internal static void FailPendingTasks(
			TaskCompletionSource<bool>? readiness,
			TaskCompletionSource<bool>? utterance,
			Exception exception)
		{
			readiness?.TrySetException(exception);
			utterance?.TrySetException(exception);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			ClientState? current;
			ClientState? initializing;
			TaskCompletionSource<bool>? readiness;
			TaskCompletionSource<bool>? utterance;

			lock (_stateLock)
			{
				if (_disposed)
					return;

				_disposed = true;
				current = _clientState;
				initializing = _initializingState;
				readiness = _readiness;
				utterance = _utterance;

				if (current is not null)
					RetireClientStateLocked(current);
				if (initializing is not null && !ReferenceEquals(initializing, current))
					RetireClientStateLocked(initializing);

				_activeUtterance = null;
				_utterance = null;
			}

			var exception = new ObjectDisposedException(nameof(TizenTextToSpeech));
			FailPendingTasks(readiness, utterance, exception);
			current?.Initialization.TrySetException(exception);
			initializing?.Initialization.TrySetException(exception);

			if (current is not null)
				DispatchTeardown(current, stop: true);
			if (initializing is not null && !ReferenceEquals(initializing, current))
				DispatchTeardown(initializing, stop: true);
		}

		sealed class ClientState
		{
			public ITizenTextToSpeechClient? Client { get; set; }

			public TaskCompletionSource<bool> Readiness { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource<ClientState> Initialization { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public bool Retired { get; set; }

			public bool TeardownRequested { get; set; }

			public bool StopBeforeTeardown { get; set; }

			public bool TeardownPosted { get; set; }

			public int MaximumSpeed { get; set; }
		}

		sealed record ActiveUtterance(
			ClientState Client,
			int UtteranceId,
			TaskCompletionSource<bool> Completion);
	}

	internal enum TizenTextToSpeechState
	{
		Ready,
		Other,
	}

	internal sealed record TizenTextToSpeechVoice(string Language, int VoiceType);

	internal sealed record TizenTextToSpeechError(
		int UtteranceId,
		int ErrorValue,
		string ErrorMessage);

	internal interface ITizenTextToSpeechDispatcher
	{
		Task InvokeAsync(Action action);

		Task<T> InvokeAsync<T>(Func<T> action);

		void Post(Action action);
	}

	internal interface ITizenTextToSpeechClientFactory
	{
		ITizenTextToSpeechClient Create();
	}

	internal interface ITizenTextToSpeechClient : IDisposable
	{
		event Action<TizenTextToSpeechState>? StateChanged;

		event Action<int>? UtteranceCompleted;

		event Action<TizenTextToSpeechError>? ErrorOccurred;

		void Prepare();

		IReadOnlyList<TizenTextToSpeechVoice> GetSupportedVoices();

		int GetMaximumSpeed();

		int AddText(string text, string language, int voiceType, int speed);

		void Play();

		void Stop();
	}

	sealed class TizenTextToSpeechDispatcher : ITizenTextToSpeechDispatcher
	{
		public static TizenTextToSpeechDispatcher Instance { get; } = new();

		public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);

		public Task<T> InvokeAsync<T>(Func<T> action) => MainThread.InvokeOnMainThreadAsync(action);

		public void Post(Action action) =>
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var context = SynchronizationContext.Current ??
					throw new InvalidOperationException("The Tizen main loop has no synchronization context.");
				context.Post(static state => ((Action)state!).Invoke(), action);
			});
	}

	sealed class TizenTextToSpeechClientFactory : ITizenTextToSpeechClientFactory
	{
		public static TizenTextToSpeechClientFactory Instance { get; } = new();

		public ITizenTextToSpeechClient Create() => new TizenTextToSpeechClient(new TtsClient());
	}

	sealed class TizenTextToSpeechClient : ITizenTextToSpeechClient
	{
		readonly TtsClient _client;

		public TizenTextToSpeechClient(TtsClient client)
		{
			_client = client;
			_client.StateChanged += (_, args) =>
				StateChanged?.Invoke(
					args.Current == State.Ready
						? TizenTextToSpeechState.Ready
						: TizenTextToSpeechState.Other);
			_client.UtteranceCompleted += (_, args) => UtteranceCompleted?.Invoke(args.UtteranceId);
			_client.ErrorOccurred += (_, args) =>
				ErrorOccurred?.Invoke(new(args.UtteranceId, (int)args.ErrorValue, args.ErrorMessage));
		}

		public event Action<TizenTextToSpeechState>? StateChanged;

		public event Action<int>? UtteranceCompleted;

		public event Action<TizenTextToSpeechError>? ErrorOccurred;

		public void Prepare() => _client.Prepare();

		public IReadOnlyList<TizenTextToSpeechVoice> GetSupportedVoices() =>
			_client.GetSupportedVoices()
				.Select(voice => new TizenTextToSpeechVoice(voice.Language, (int)voice.VoiceType))
				.ToArray();

		public int GetMaximumSpeed() => _client.GetSpeedRange().Max;

		public int AddText(string text, string language, int voiceType, int speed) =>
			_client.AddText(text, language, voiceType, speed);

		public void Play() => _client.Play();

		public void Stop() => _client.Stop();

		public void Dispose() => _client.Dispose();
	}
}
