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
	/// <see cref="SpeechOptions.Pitch"/> and <see cref="SpeechOptions.Volume"/> cannot be honoured
	/// and are rejected rather than silently ignored.
	/// </remarks>
	public sealed class TizenTextToSpeech : ITextToSpeech, IDisposable
	{
		const float RateMax = 2.0f;

		readonly SemaphoreSlim _speakLock = new(1, 1);

		TtsClient? _client;
		TaskCompletionSource<bool>? _initialize;
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
		/// Use <see cref="GetSupportedVoiceLanguagesAsync"/> to enumerate the languages Tizen reports,
		/// and pass one of them to <see cref="SpeakAsync(string, string, float?, CancellationToken)"/>.
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
			var client = await InitializeAsync().ConfigureAwait(false);

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
		public Task SpeakAsync(string text, string language, float? rate = null, CancellationToken cancelToken = default) =>
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

			var client = await InitializeAsync().ConfigureAwait(false);

			await _speakLock.WaitAsync(cancelToken).ConfigureAwait(false);
			try
			{
				var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				_utterance = utterance;

				using var registration = cancelToken.Register(() =>
				{
					try
					{
						client.Stop();
					}
					catch
					{
						// The client may already be idle.
					}

					utterance.TrySetResult(false);
				});

				var (resolvedLanguage, voiceType) = ResolveVoice(client, language);

				client.AddText(text, resolvedLanguage, (int)voiceType, ResolveRate(client, rate));
				client.Play();

				await utterance.Task.ConfigureAwait(false);
			}
			finally
			{
				_utterance = null;
				_speakLock.Release();
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

		Task<TtsClient> InitializeAsync()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_client is { } existing && _initialize is { } pending)
				return WrapAsync(pending, existing);

			var client = new TtsClient();
			var initialize = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			client.StateChanged += (_, e) =>
			{
				if (e.Current == State.Ready)
					initialize.TrySetResult(true);
			};

			client.UtteranceCompleted += (_, _) =>
			{
				try
				{
					client.Stop();
				}
				catch
				{
					// The client may already be idle.
				}

				_utterance?.TrySetResult(true);
			};

			_client = client;
			_initialize = initialize;

			client.Prepare();

			return WrapAsync(initialize, client);

			static async Task<TtsClient> WrapAsync(TaskCompletionSource<bool> tcs, TtsClient client)
			{
				await tcs.Task.ConfigureAwait(false);
				return client;
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_client?.Dispose();
			_client = null;
			_speakLock.Dispose();
		}
	}
}
