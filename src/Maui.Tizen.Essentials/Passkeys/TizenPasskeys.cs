using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPasskeys"/>.
	/// </summary>
	/// <remarks>
	/// API15 exposes a public WebAuthn authenticator, but the pinned MAUI package exposes no public
	/// constructor or factory for <see cref="PasskeyCreationResponse"/> or
	/// <see cref="PasskeyAssertionResponse"/>. The backend can translate the input JSON and receive
	/// native WebAuthn bytes, but cannot return the sealed MAUI response types without reflection or
	/// an internal API. Until MAUI publishes that response factory, the complete contract is
	/// unavailable and <see cref="IsSupported"/> must remain <see langword="false"/>.
	/// </remarks>
	public sealed class TizenPasskeys : IPasskeys
	{
		const string Reason =
			"Tizen API15 provides Tizen.Security.WebAuthn.Authenticator, but the pinned MAUI " +
			"PasskeyCreationResponse and PasskeyAssertionResponse types are sealed and expose no " +
			"public constructor or factory. Returning the native response would require forbidden " +
			"reflection or an unavailable MAUI API.";

		/// <inheritdoc/>
		public bool IsSupported
		{
			get
			{
				// Probe the actual API15 feature/authenticator state so a future MAUI response
				// factory can activate without replacing the native capability logic.
				_ = IsNativeAuthenticatorAvailable();
				return false;
			}
		}

		internal static bool IsNativeAuthenticatorAvailable(
			Func<bool> getFeature,
			Func<global::Tizen.Security.WebAuthn.AuthenticatorTransport> getAuthenticators)
		{
			try
			{
				return getFeature() &&
					getAuthenticators() != global::Tizen.Security.WebAuthn.AuthenticatorTransport.None;
			}
			catch (Exception)
			{
				return false;
			}
		}

		internal static bool IsNativeAuthenticatorAvailable() =>
			IsNativeAuthenticatorAvailable(
				static () => TizenSystemInformation.GetFeatureInfo<bool>("security.webauthn"),
				static () => global::Tizen.Security.WebAuthn.Authenticator.SupportedAuthenticators());

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<PasskeyCreationResponse> CreateAsync(PasskeyCreationOptions options, CancellationToken cancellationToken = default) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IPasskeys)}.{nameof(CreateAsync)}", Reason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<PasskeyAssertionResponse> AssertAsync(PasskeyRequestOptions options, CancellationToken cancellationToken = default) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IPasskeys)}.{nameof(AssertAsync)}", Reason);
	}
}
