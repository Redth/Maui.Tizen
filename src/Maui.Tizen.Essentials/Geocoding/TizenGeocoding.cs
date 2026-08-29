using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPlatformGeocoding"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Unsupported on the targeted Tizen surface.</b> The in-box dotnet/maui Tizen backend
	/// implemented geocoding with <c>Tizen.Maps</c> (the HERE-backed <c>MapService</c>). That API
	/// was deprecated in TizenFX API11 and <b>removed entirely by API15</b>, which is the
	/// reference pack this repository targets: <c>Samsung.Tizen.Ref.API15</c> ships no
	/// <c>Tizen.Maps.dll</c>. Tizen offers no replacement geocoding service, so there is nothing
	/// left to call.
	/// </para>
	/// <para>
	/// Both operations therefore throw <see cref="FeatureNotSupportedException"/> rather than
	/// returning an empty result set, which a caller could not distinguish from "this address
	/// matched nothing".
	/// </para>
	/// <para>
	/// <see cref="MapServiceToken"/> deliberately does <b>not</b> throw. The .NET 11 Essentials DI
	/// bridge reads and writes it during <c>MauiApp</c> initialization when an app calls
	/// <c>ConfigureEssentials(e =&gt; e.UseMapServiceToken(...))</c>; throwing there would turn a
	/// configuration line into a startup crash. The token is accepted and simply never used.
	/// </para>
	/// </remarks>
	public sealed class TizenGeocoding : IPlatformGeocoding
	{
		const string Reason =
			"Tizen.Maps (MapService) was deprecated in TizenFX API11 and removed by API15, and " +
			"Tizen ships no replacement geocoding service. Register a cross-platform geocoding " +
			"client as IGeocoding instead.";

		/// <inheritdoc/>
		/// <remarks>
		/// Accepted and retained so that a configured map service token does not fail application
		/// startup, but never used: there is no Tizen map service left to authenticate against.
		/// </remarks>
		public string? MapServiceToken { get; set; }

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IGeocoding)}.{nameof(GetPlacemarksAsync)}", Reason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<IEnumerable<Location>> GetLocationsAsync(string address) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IGeocoding)}.{nameof(GetLocationsAsync)}", Reason);
	}
}
