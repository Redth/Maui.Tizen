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

		TtsClient? _client;
		Task<TtsClient>? _initialization;
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
			var client = await InitializeAsync(CancellationToken.None).ConfigureAwait(false);

			var languages = new List<string>();

			foreach (var voice in client.GetSupportedVoices())
			{
				if (!languages.Any(l => string.Equals(l, voice.Language, StringComparison.OrdinalIgnoreCase)))
					languages.Add(voice.Language);
			}

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

			var client = await InitializeAsync(cancelToken).ConfigureAwait(false);

			await _speakLock.WaitAsync(cancelToken).ConfigureAwait(false);
			try
			{
				// Re-check after acquiring the lock. Initialization and queuing behind another
				// utterance can both take arbitrarily long, and starting speech for a request that
				// was cancelled while waiting is exactly the surprise a cancellation token exists to
				// prevent.
				cancelToken.ThrowIfCancellationRequested();

				var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				_utterance = utterance;

				using var registration = cancelToken.Register(() =>
					CancelUtterance(utterance, cancelToken, () => InvalidateClient(client)));

				try
				{
					cancelToken.ThrowIfCancellationRequested();

					var (resolvedLanguage, voiceType) = ResolveVoice(client, language);
					client.AddText(text, resolvedLanguage, (int)voiceType, ResolveRate(client, rate));

					// Clear any text queued by a cancellation racing AddText before playback starts.
					cancelToken.ThrowIfCancellationRequested();

					client.Play();
				}
				catch (Exception) when (cancelToken.IsCancellationRequested)
				{
					cancelToken.ThrowIfCancellationRequested();
					throw;
				}

				await utterance.Task.ConfigureAwait(false);
			}
			finally
			{
				_utterance = null;
				_speakLock.Release();
			}
		}

		internal static void CancelUtterance(
			TaskCompletionSource<bool> utterance,
			CancellationToken cancelToken,
			Action stop)
		{
			utterance.TrySetCanceled(cancelToken);
			try
			{
				stop();
			}
			catch (Exception)
			{
				// Cancellation has already won the task contract.
			}
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
		async Task<TtsClient> InitializeAsync(CancellationToken cancelToken)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_initialization is { } cached)
				return await cached.WaitAsync(cancelToken).ConfigureAwait(false);

			await _initializeLock.WaitAsync(cancelToken).ConfigureAwait(false);
			try
			{
				ObjectDisposedException.ThrowIf(_disposed, this);

				_initialization ??= PrepareAsync();
			}
			finally
			{
				_initializeLock.Release();
			}

			try
			{
				return await _initialization.WaitAsync(cancelToken).ConfigureAwait(false);
			}
			catch (Exception) when (_initialization?.IsFaulted == true)
			{
				// Let the next caller retry rather than caching the failure forever.
				await _initializeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
				try
				{
					if (_initialization?.IsFaulted == true)
						_initialization = null;
				}
				finally
				{
					_initializeLock.Release();
				}

				throw;
			}
		}

		Task<TtsClient> PrepareAsync()
		{
			var client = new TtsClient();
			var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var cleanedUp = 0;
			_readiness = ready;

			client.StateChanged += OnStateChanged;
			client.UtteranceCompleted += OnUtteranceCompleted;
			client.ErrorOccurred += OnErrorOccurred;

			try
			{
				client.Prepare();
			}
			catch (Exception)
			{
				CleanupClient();
				throw;
			}

			_client = client;

			return AwaitReadyAsync();

			void OnStateChanged(object? sender, StateChangedEventArgs e)
			{
				if (e.Current == State.Ready)
					ready.TrySetResult(true);
			}

			void OnUtteranceCompleted(object? sender, UtteranceEventArgs e)
			{
				StopQuietly(client);
				_utterance?.TrySetResult(true);
			}

			void OnErrorOccurred(object? sender, ErrorOccurredEventArgs e)
			{
				var exception = new InvalidOperationException(
					$"Tizen text-to-speech failed ({e.ErrorValue}): {e.ErrorMessage}");

				FailPendingTasks(ready, _utterance, exception);
				CleanupClient();
			}

			async Task<TtsClient> AwaitReadyAsync()
			{
				try
				{
					await ready.Task.ConfigureAwait(false);
					return client;
				}
				catch (Exception)
				{
					CleanupClient();
					throw;
				}
			}

			void CleanupClient()
			{
				if (Interlocked.Exchange(ref cleanedUp, 1) != 0)
					return;

				client.StateChanged -= OnStateChanged;
				client.UtteranceCompleted -= OnUtteranceCompleted;
				client.ErrorOccurred -= OnErrorOccurred;

				if (ReferenceEquals(_client, client))
				{
					_client = null;
					_initialization = null;
				}
				if (ReferenceEquals(_readiness, ready))
					_readiness = null;

				client.Dispose();
			}
		}

		void InvalidateClient(TtsClient client)
		{
			if (ReferenceEquals(_client, client))
			{
				_client = null;
				_initialization = null;
			}

			client.Dispose();
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
			if (_disposed)
				return;

			_disposed = true;
			FailPendingTasks(
				_readiness,
				_utterance,
				new ObjectDisposedException(nameof(TizenTextToSpeech)));

			_client?.Dispose();
			_client = null;
			_initialization = null;
			_readiness = null;
		}
	}
}
