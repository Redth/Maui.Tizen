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
	/// Tizen exposes FIDO UAF through <c>Tizen.Account.FidoClient</c>, which is a different protocol
	/// from the WebAuthn / CTAP2 contract that <see cref="IPasskeys"/> models. There is no Tizen
	/// credential manager that can produce a WebAuthn attestation or assertion, so
	/// <see cref="IsSupported"/> reports <see langword="false"/> and both operations throw.
	/// </remarks>
	public sealed class TizenPasskeys : IPasskeys
	{
		const string Reason =
			"Tizen ships FIDO UAF (Tizen.Account.FidoClient) but no WebAuthn/CTAP2 credential " +
			"manager that can satisfy the passkey creation and assertion contract.";

		/// <inheritdoc/>
		public bool IsSupported => false;

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
