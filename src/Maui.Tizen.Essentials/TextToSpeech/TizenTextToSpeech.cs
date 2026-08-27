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
	/// Tizen's TTS client exposes a speed range but no pitch or volume control, so
	/// <see cref="SpeechOptions.Pitch"/> and <see cref="SpeechOptions.Volume"/> are rejected rather
	/// than silently ignored.
	/// </remarks>
	public sealed class TizenTextToSpeech : ITextToSpeech, IDisposable
	{
		const float RateMax = 2.0f;

		readonly SemaphoreSlim _speakLock = new(1, 1);
		readonly SemaphoreSlim _initializeLock = new(1, 1);

		ClientState? _clientState;
		ClientState? _initializingState;
		Task<ClientState>? _initialization;
		ActiveUtterance? _activeUtterance;
		TaskCompletionSource<bool>? _readiness;
		TaskCompletionSource<bool>? _utterance;
		bool _disposed;

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown; see remarks.</exception>
		/// <remarks>
		/// <para>
		/// Blocked by a public API gap in <c>Microsoft.Maui.Essentials</c>:
		/// <c>Microsoft.Maui.Media.Locale</c> has only an <c>internal</c> constructor, so a platform
		/// backend outside the dotnet/maui assembly cannot construct the values this contract must
		/// return. Returning an empty sequence instead would be indistinguishable from "this device
		/// supports no voices", so this throws rather than reporting a success-shaped result.
		/// </para>
		/// <para>
		/// Use <see cref="GetSupportedVoiceLanguagesAsync"/> to enumerate the languages Tizen
		/// reports, and pass one of them to <see cref="SpeakWithVoiceAsync"/>.
		/// </para>
		/// </remarks>
		public Task<IEnumerable<Locale>> GetLocalesAsync() =>
			throw TizenEssentialsSupport.NotSupported(
				$"{nameof(ITextToSpeech)}.{nameof(GetLocalesAsync)}",
				"Microsoft.Maui.Media.Locale exposes no public constructor, so a standalone platform " +
				"backend cannot create the Locale values this contract returns. " +
				$"Use {nameof(TizenTextToSpeech)}.{nameof(GetSupportedVoiceLanguagesAsync)} instead.");

		/// <summary>
		/// Enumerates the voice languages Tizen reports for the current device, for example <c>en_US</c>.
		/// </summary>
		/// <returns>The distinct supported voice languages.</returns>
		public async Task<IReadOnlyList<string>> GetSupportedVoiceLanguagesAsync()
		{
			var languages = new List<string>();

			await RunWithCurrentClientAsync(
				_speakLock,
				CancellationToken.None,
				InitializeAsync,
				state =>
				{
					foreach (var voice in state.Client.GetSupportedVoices())
					{
						if (!languages.Any(l => string.Equals(l, voice.Language, StringComparison.OrdinalIgnoreCase)))
							languages.Add(voice.Language);
					}

					return Task.CompletedTask;
				}).ConfigureAwait(false);

			return languages;
		}

		/// <summary>
		/// Speaks the supplied text using an explicit Tizen voice language.
		/// </summary>
		/// <param name="text">The text to speak.</param>
		/// <param name="language">A language returned by <see cref="GetSupportedVoiceLanguagesAsync"/>.</param>
		/// <param name="rate">The optional speech rate, in the same 0..2 range as <see cref="SpeechOptions.Rate"/>.</param>
		/// <param name="cancelToken">A token used to stop playback.</param>
		/// <returns>A task that completes when the utterance finishes or is cancelled.</returns>
		/// <remarks>
		/// Deliberately not named <c>SpeakAsync</c>. An overload taking a language string alongside
		/// <see cref="ITextToSpeech.SpeakAsync(string, SpeechOptions?, CancellationToken)"/> would make
		/// <c>SpeakAsync(text, null)</c> ambiguous between a null <see cref="SpeechOptions"/> and a null
		/// language, and adds a second optional-parameter overload to the same name (RS0026).
		/// </remarks>
		public Task SpeakWithVoiceAsync(string text, string language, float? rate = null, CancellationToken cancelToken = default) =>
			SpeakCoreAsync(text, language, rate, cancelToken);

		/// <inheritdoc/>
		public async Task SpeakAsync(string text, SpeechOptions? options = default, CancellationToken cancelToken = default)
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

			await SpeakCoreAsync(text, options?.Locale?.Language, options?.Rate, cancelToken).ConfigureAwait(false);
		}

		async Task SpeakCoreAsync(string text, string? language, float? rate, CancellationToken cancelToken)
		{
			ArgumentNullException.ThrowIfNull(text);

			cancelToken.ThrowIfCancellationRequested();

			await RunWithCurrentClientAsync(
				_speakLock,
				cancelToken,
				InitializeAsync,
				async state =>
				{
					var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					SetPendingUtterance(state, utterance);

					try
					{
						using var registration = cancelToken.Register(() =>
							CancelClientState(state, utterance, cancelToken));
						cancelToken.ThrowIfCancellationRequested();

						var (resolvedLanguage, voiceType) = ResolveVoice(state.Client, language);
						var utteranceId = state.Client.AddText(
							text,
							resolvedLanguage,
							(int)voiceType,
							ResolveRate(state.Client, rate));

						SetActiveUtterance(state, utteranceId, utterance);

						// A cancellation racing AddText retires this generation before playback.
						cancelToken.ThrowIfCancellationRequested();

						PlayOrRetire(
							state.Client.Play,
							() =>
							{
								if (RetireClientState(state))
									TeardownClient(state, stop: true);
							});

						await utterance.Task.ConfigureAwait(false);
					}
					catch (Exception) when (cancelToken.IsCancellationRequested)
					{
						cancelToken.ThrowIfCancellationRequested();
						throw;
					}
					finally
					{
						ClearActiveUtterance(state, utterance);
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

		internal static void CancelUtterance(
			TaskCompletionSource<bool> utterance,
			CancellationToken cancelToken,
			Action stop)
		{
			try
			{
				stop();
			}
			catch (Exception)
			{
				// Cancellation has already won the task contract.
			}

			utterance.TrySetCanceled(cancelToken);
		}

		internal static void PlayOrRetire(Action play, Action retire)
		{
			try
			{
				play();
			}
			catch
			{
				retire();
				throw;
			}
		}

		internal static bool MatchesUtterance(int expectedUtteranceId, int callbackUtteranceId) =>
			expectedUtteranceId == callbackUtteranceId;

		internal static bool RetireBeforeSettle(Func<bool> retire, Action settle)
		{
			if (!retire())
				return false;

			settle();
			return true;
		}

		static void StopQuietly(TtsClient client)
		{
			try
			{
				client.Stop();
			}
			catch (Exception)
			{
				// The client may already be idle; nothing actionable.
			}
		}

		static (string Language, Voice VoiceType) ResolveVoice(TtsClient client, string? requestedLanguage)
		{
			var language = "en_US";
			var voiceType = Voice.Auto;

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

		static int ResolveRate(TtsClient client, float? rate)
		{
			if (rate is not { } value)
				return 0;

			return (int)Math.Round(value / RateMax * client.GetSpeedRange().Max, MidpointRounding.AwayFromZero);
		}

		/// <summary>
		/// Prepares the native TTS client, at most once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Serialized behind a semaphore and cached as a single task. The previous implementation
		/// checked two fields without a lock, so concurrent callers could each construct a
		/// <see cref="TtsClient"/>; the loser's client was overwritten and leaked, still holding a
		/// native handle and an unremovable <c>StateChanged</c> subscription.
		/// </para>
		/// <para>
		/// A failed <c>Prepare</c> disposes the client and clears the cached task, so a later call
		/// retries cleanly instead of awaiting a task that will never complete.
		/// </para>
		/// </remarks>
		async Task<ClientState> InitializeAsync(CancellationToken cancelToken)
		{
			ClientState? stateToPrepare = null;
			Task<ClientState> initialization;
			await _initializeLock.WaitAsync(cancelToken).ConfigureAwait(false);
			try
			{
				ObjectDisposedException.ThrowIf(_disposed, this);

				if (_clientState is { Retired: false } current)
					return current;

				if (_initialization is null)
				{
					stateToPrepare = new ClientState(new TtsClient());
					_initializingState = stateToPrepare;
					_initialization = stateToPrepare.Initialization.Task;
					_readiness = stateToPrepare.Readiness;
				}

				initialization = _initialization;
			}
			finally
			{
				_initializeLock.Release();
			}

			if (stateToPrepare is not null)
				Prepare(stateToPrepare);

			return await initialization.WaitAsync(cancelToken).ConfigureAwait(false);
		}

		void Prepare(ClientState state)
		{
			state.Client.StateChanged += OnStateChanged;
			state.Client.UtteranceCompleted += OnUtteranceCompleted;
			state.Client.ErrorOccurred += OnErrorOccurred;
			state.Teardown = () =>
			{
				state.Client.StateChanged -= OnStateChanged;
				state.Client.UtteranceCompleted -= OnUtteranceCompleted;
				state.Client.ErrorOccurred -= OnErrorOccurred;
				state.Client.Dispose();
			};

			try
			{
				state.Client.Prepare();
			}
			catch (Exception exception)
			{
				RetireClientState(state);
				state.Readiness.TrySetException(exception);
				state.Initialization.TrySetException(exception);
				TeardownClient(state, stop: false);
				throw;
			}

			void OnStateChanged(object? sender, StateChangedEventArgs e)
			{
				if (e.Current == State.Ready)
					PublishReadyClient(state);
			}

			void OnUtteranceCompleted(object? sender, UtteranceEventArgs e)
			{
				var utterance = GetMatchingActiveUtterance(state, e.UtteranceId);
				if (utterance is null)
					return;

				StopQuietly(state.Client);
				utterance.Completion.TrySetResult(true);
			}

			void OnErrorOccurred(object? sender, ErrorOccurredEventArgs e)
			{
				var exception = new InvalidOperationException(
					$"Tizen text-to-speech failed ({e.ErrorValue}): {e.ErrorMessage}");
				ActiveUtterance? utterance = null;

				if (!RetireBeforeSettle(
					() => TryRetireForNativeError(state, e.UtteranceId, out utterance),
					() =>
					{
						state.Readiness.TrySetException(exception);
						state.Initialization.TrySetException(exception);
						utterance?.Completion.TrySetException(exception);
					}))
					return;

				DeferNativeTeardown(
					action => MainThread.BeginInvokeOnMainThread(action),
					() => TeardownClient(state, stop: false));
			}
		}

		void PublishReadyClient(ClientState state)
		{
			_initializeLock.Wait();
			try
			{
				if (state.Retired || !ReferenceEquals(_initializingState, state) || _disposed)
					return;

				_clientState = state;
				_initializingState = null;
				_readiness = null;
			}
			finally
			{
				_initializeLock.Release();
			}

			state.Readiness.TrySetResult(true);
			state.Initialization.TrySetResult(state);
		}

		void SetActiveUtterance(
			ClientState state,
			int utteranceId,
			TaskCompletionSource<bool> completion)
		{
			_initializeLock.Wait();
			try
			{
				if (state.Retired || !ReferenceEquals(_clientState, state))
					throw new InvalidOperationException("The Tizen text-to-speech client was retired before playback.");

				_activeUtterance = new ActiveUtterance(state, utteranceId, completion);
			}
			finally
			{
				_initializeLock.Release();
			}
		}

		void SetPendingUtterance(ClientState state, TaskCompletionSource<bool> completion)
		{
			_initializeLock.Wait();
			try
			{
				ObjectDisposedException.ThrowIf(_disposed, this);
				if (state.Retired || !ReferenceEquals(_clientState, state))
					throw new InvalidOperationException("The Tizen text-to-speech client was retired before playback.");

				_utterance = completion;
			}
			finally
			{
				_initializeLock.Release();
			}
		}

		ActiveUtterance? GetMatchingActiveUtterance(ClientState state, int utteranceId)
		{
			_initializeLock.Wait();
			try
			{
				return _activeUtterance is { } active &&
					ReferenceEquals(active.Client, state) &&
					MatchesUtterance(active.UtteranceId, utteranceId)
						? active
						: null;
			}
			finally
			{
				_initializeLock.Release();
			}
		}

		bool TryRetireForNativeError(
			ClientState state,
			int utteranceId,
			out ActiveUtterance? utterance)
		{
			_initializeLock.Wait();
			try
			{
				utterance = _activeUtterance;
				if (utterance is not null &&
					(!ReferenceEquals(utterance.Client, state) ||
						!MatchesUtterance(utterance.UtteranceId, utteranceId)))
				{
					utterance = null;
					return false;
				}

				return RetireClientStateLocked(state);
			}
			finally
			{
				_initializeLock.Release();
			}
		}

		void ClearActiveUtterance(ClientState state, TaskCompletionSource<bool> completion)
		{
			_initializeLock.Wait();
			try
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
			finally
			{
				_initializeLock.Release();
			}
		}

		void CancelClientState(
			ClientState state,
			TaskCompletionSource<bool> utterance,
			CancellationToken cancelToken)
		{
			CancelUtterance(
				utterance,
				cancelToken,
				() =>
				{
					if (RetireClientState(state))
						TeardownClient(state, stop: true);
				});
		}

		bool RetireClientState(ClientState state)
		{
			_initializeLock.Wait();
			try
			{
				return RetireClientStateLocked(state);
			}
			finally
			{
				_initializeLock.Release();
			}
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

		static void TeardownClient(ClientState state, bool stop)
		{
			if (Interlocked.Exchange(ref state.TeardownStarted, 1) != 0)
				return;

			if (stop)
				StopQuietly(state.Client);

			state.Teardown?.Invoke();
		}

		internal static void DeferNativeTeardown(Action<Action> dispatch, Action teardown)
		{
			ThreadPool.QueueUserWorkItem(_ => dispatch(teardown));
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

			_initializeLock.Wait();
			try
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
			finally
			{
				_initializeLock.Release();
			}

			FailPendingTasks(
				readiness,
				utterance,
				new ObjectDisposedException(nameof(TizenTextToSpeech)));

			var disposedException = new ObjectDisposedException(nameof(TizenTextToSpeech));
			current?.Initialization.TrySetException(disposedException);
			initializing?.Initialization.TrySetException(disposedException);

			_speakLock.Wait();
			try
			{
				if (current is not null)
					TeardownClient(current, stop: true);
				if (initializing is not null && !ReferenceEquals(initializing, current))
					TeardownClient(initializing, stop: true);
			}
			finally
			{
				_speakLock.Release();
			}
		}

		sealed class ClientState
		{
			public ClientState(TtsClient client)
			{
				Client = client;
			}

			public TtsClient Client { get; }

			public TaskCompletionSource<bool> Readiness { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource<ClientState> Initialization { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public Action? Teardown { get; set; }

			public bool Retired { get; set; }

			public int TeardownStarted;
		}

		sealed record ActiveUtterance(
			ClientState Client,
			int UtteranceId,
			TaskCompletionSource<bool> Completion);
	}
}
