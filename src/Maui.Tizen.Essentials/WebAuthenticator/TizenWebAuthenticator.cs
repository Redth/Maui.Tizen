using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IWebAuthenticator"/>.
	/// </summary>
	/// <remarks>
	/// Tizen has no app-link / custom-URI-scheme callback contract that can return control to the
	/// calling application after an external browser flow, so a web authentication round trip cannot
	/// be completed. Both overloads throw rather than returning a partially populated
	/// <see cref="WebAuthenticatorResult"/>.
	/// </remarks>
	public sealed class TizenWebAuthenticator : IWebAuthenticator
	{
		const string Reason =
			"Tizen provides no callback URI registration that can return an external browser " +
			"authentication response to the launching application.";

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IWebAuthenticator)}.{nameof(AuthenticateAsync)}", Reason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions, CancellationToken cancellationToken) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IWebAuthenticator)}.{nameof(AuthenticateAsync)}", Reason);
	}

	/// <summary>
	/// Tizen implementation of <see cref="IAppleSignInAuthenticator"/>.
	/// </summary>
	/// <remarks>Sign in with Apple's native flow is only available on Apple platforms.</remarks>
	public sealed class TizenAppleSignInAuthenticator : IAppleSignInAuthenticator
	{
		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<WebAuthenticatorResult> AuthenticateAsync(AppleSignInAuthenticator.Options? options = null) =>
			throw TizenEssentialsSupport.NotSupported(
				$"{nameof(IAppleSignInAuthenticator)}.{nameof(AuthenticateAsync)}",
				"Native Sign in with Apple is only available on Apple platforms.");
	}
}
