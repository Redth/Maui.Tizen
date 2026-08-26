// MapService is marked deprecated from Tizen API level 11 onwards, but Tizen ships no replacement
// geocoding service, so this backend keeps using it (as dotnet/maui did).
#pragma warning disable CS0618 // Type or member is obsolete

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Tizen.Maps;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPlatformGeocoding"/>, backed by <c>Tizen.Maps</c> (HERE provider).
	/// </summary>
	/// <remarks>
	/// A map service token is required. Supply it with
	/// <c>builder.ConfigureEssentials(e =&gt; e.UseMapServiceToken("..."))</c>: the .NET 11 Essentials
	/// DI bridge forwards configured map tokens to the registered <see cref="IPlatformGeocoding"/>.
	/// </remarks>
	public sealed class TizenGeocoding : IPlatformGeocoding, IDisposable
	{
		readonly SemaphoreSlim _mapServiceLock = new(1, 1);

		MapService? _mapService;
		bool _disposed;

		/// <inheritdoc/>
		public string? MapServiceToken { get; set; }

		/// <inheritdoc/>
		public async Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude)
		{
			var map = await GetMapServiceAsync().ConfigureAwait(false);

			var request = map.CreateReverseGeocodeRequest(latitude, longitude);

			var placemarks = new List<Placemark>();
			foreach (var address in await request.GetResponseAsync().ConfigureAwait(false))
			{
				placemarks.Add(new Placemark
				{
					CountryCode = address.CountryCode,
					CountryName = address.Country,
					AdminArea = address.State,
					SubAdminArea = address.County,
					Locality = address.City,
					SubLocality = address.District,
					Thoroughfare = address.Street,
					SubThoroughfare = address.Building,
					FeatureName = address.Street,
					Location = new Location(latitude, longitude),
					PostalCode = address.PostalCode,
				});
			}

			return placemarks;
		}

		/// <inheritdoc/>
		public async Task<IEnumerable<Location>> GetLocationsAsync(string address)
		{
			ArgumentNullException.ThrowIfNull(address);

			var map = await GetMapServiceAsync().ConfigureAwait(false);

			var request = map.CreateGeocodeRequest(address);

			var locations = new List<Location>();
			foreach (var position in await request.GetResponseAsync().ConfigureAwait(false))
				locations.Add(new Location(position.Latitude, position.Longitude));

			return locations;
		}

		async Task<MapService> GetMapServiceAsync()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			var token = MapServiceToken;

			if (string.IsNullOrWhiteSpace(token))
			{
				throw new ArgumentNullException(
					nameof(MapServiceToken),
					"Set the map service token to be able to use geocoding on Tizen, for example with " +
					"builder.ConfigureEssentials(essentials => essentials.UseMapServiceToken(\"...\")).");
			}

			TizenPermissions.EnsureDeclared<Permissions.Maps>();

			await _mapServiceLock.WaitAsync().ConfigureAwait(false);
			try
			{
				if (_mapService is null)
				{
					var service = new MapService("HERE", token);
					await service.RequestUserConsent().ConfigureAwait(false);
					_mapService = service;
				}

				return _mapService;
			}
			finally
			{
				_mapServiceLock.Release();
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_mapService?.Dispose();
			_mapService = null;
			_mapServiceLock.Dispose();
		}
	}
}
