using System;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Declares how an Essentials capability is supported by this Tizen backend.
	/// </summary>
	public enum TizenSupportLevel
	{
		/// <summary>The capability is fully implemented against native Tizen APIs.</summary>
		Implemented,

		/// <summary>
		/// The capability is implemented, but only part of the contract can be honoured on Tizen.
		/// The unsupported members throw instead of returning a success-shaped result.
		/// </summary>
		Partial,

		/// <summary>
		/// Tizen has no API that can satisfy the contract. Every member throws
		/// <see cref="FeatureNotSupportedException"/>; nothing is silently faked.
		/// </summary>
		Unsupported,
	}

	/// <summary>
	/// Central, explicit classification of Essentials capability support on Tizen.
	/// </summary>
	/// <remarks>
	/// Unsupported capabilities deliberately throw rather than returning empty collections,
	/// <see langword="null"/>, or completed tasks. A silent no-op is indistinguishable from success
	/// at the call site and hides real portability problems from app authors.
	/// </remarks>
	public static class TizenEssentialsSupport
	{
		/// <summary>
		/// Creates the exception thrown by capabilities that Tizen cannot provide at all.
		/// </summary>
		/// <param name="capability">The Essentials capability name, for example <c>IClipboard.GetTextAsync</c>.</param>
		/// <param name="reason">Why Tizen cannot provide the capability.</param>
		/// <returns>A <see cref="FeatureNotSupportedException"/> describing the gap.</returns>
		public static FeatureNotSupportedException NotSupported(string capability, string reason) =>
			new FeatureNotSupportedException(
				$"'{capability}' is not supported by the Tizen platform backend. {reason}");

		/// <summary>
		/// Creates the exception thrown by capabilities that exist on Tizen, but not on the
		/// device profile the application is currently running on.
		/// </summary>
		/// <param name="capability">The Essentials capability name.</param>
		/// <param name="supportedProfiles">The Tizen device profiles that do provide the capability.</param>
		/// <returns>A <see cref="FeatureNotSupportedException"/> describing the gap.</returns>
		public static FeatureNotSupportedException NotSupportedOnProfile(
			string capability,
			params TizenDeviceProfile[] supportedProfiles) =>
			new FeatureNotSupportedException(
				$"'{capability}' is not available on the '{TizenSystemInformation.CurrentProfile}' Tizen device profile. " +
				$"Supported profiles: {string.Join(", ", supportedProfiles)}.");
	}
}
