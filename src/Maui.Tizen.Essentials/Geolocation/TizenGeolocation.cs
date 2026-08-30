using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using TizenLocationChangedEventArgs = Tizen.Location.LocationChangedEventArgs;
using TizenLocationType = Tizen.Location.LocationType;
using TizenLocatorHelper = Tizen.Location.LocatorHelper;
using TizenLocator = Tizen.Location.Locator;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IGeolocation"/>, backed by <c>Tizen.Location.Locator</c>.
	/// </summary>
	/// <remarks>
	/// Continuous foreground listening is not implemented by this backend:
	/// <see cref="StartListeningForegroundAsync"/> and <see cref="StopListeningForeground"/> throw
	/// rather than silently succeeding while never raising <see cref="LocationChanged"/>.
	/// One-shot location requests are fully supported.
	/// </remarks>
	public sealed class TizenGeolocation : IGeolocation
	{
		const string ListeningReason =
			"Continuous foreground location updates are not implemented by the Tizen Essentials backend. " +
			"Use GetLocationAsync for one-shot requests.";

		readonly object _locker = new();

		Location? _lastKnownLocation;

		/// <inheritdoc/>
		public bool IsListeningForeground => false;

		/// <inheritdoc/>
		public bool IsEnabled => IsLocationEnabled(
			() => TizenSystemInformation.GetFeatureInfo<bool>("location.gps"),
			() => TizenSystemInformation.GetFeatureInfo<bool>("location.wps"),
			TizenLocatorHelper.IsEnabledType);

		/// <inheritdoc/>
		/// <remarks>Never raised: continuous listening is unsupported on this backend.</remarks>
		public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		/// <remarks>Never raised: continuous listening is unsupported on this backend.</remarks>
		public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		public Task<Location?> GetLastKnownLocationAsync()
		{
			lock (_locker)
				return Task.FromResult(_lastKnownLocation);
		}

		/// <inheritdoc/>
		public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
		{
			ArgumentNullException.ThrowIfNull(request);

			await TizenPermissions.EnsureGrantedAsync<Permissions.LocationWhenInUse>().ConfigureAwait(false);

			using var locator = new TizenLocator(ResolveLocationType());

			var tcs = new TaskCompletionSource<Location?>(TaskCreationOptions.RunContinuationsAsynchronously);

			using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			if (request.Timeout > TimeSpan.Zero)
				timeoutSource.CancelAfter(request.Timeout);

			void OnLocationChanged(object? sender, TizenLocationChangedEventArgs e)
			{
				if (e.Location is null)
					return;

				var location = new Location
				{
					Accuracy = e.Location.Accuracy,
					Altitude = e.Location.Altitude,
					Course = e.Location.Direction,
					Latitude = e.Location.Latitude,
					Longitude = e.Location.Longitude,
					Speed = KilometersPerHourToMetersPerSecond(e.Location.Speed),
					Timestamp = e.Location.Timestamp,
				};

				lock (_locker)
					_lastKnownLocation = location;

				tcs.TrySetResult(location);
			}

			locator.LocationChanged += OnLocationChanged;

			using var registration = timeoutSource.Token.Register(() => tcs.TrySetResult(null));

			try
			{
				locator.Start();
				return await tcs.Task.ConfigureAwait(false);
			}
			finally
			{
				locator.LocationChanged -= OnLocationChanged;

				try
				{
					locator.Stop();
				}
				catch
				{
					// The locator may already have stopped; nothing actionable.
				}
			}
		}

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IGeolocation)}.{nameof(StartListeningForegroundAsync)}", ListeningReason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public void StopListeningForeground() =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IGeolocation)}.{nameof(StopListeningForeground)}", ListeningReason);

		internal static TizenLocationType ResolveLocationType()
		{
			var gps = TizenSystemInformation.GetFeatureInfo<bool>("location.gps");
			var wps = TizenSystemInformation.GetFeatureInfo<bool>("location.wps");

			return (gps, wps) switch
			{
				(true, true) => TizenLocationType.Hybrid,
				(true, false) => TizenLocationType.Gps,
				(false, true) => TizenLocationType.Wps,
				_ => TizenLocationType.Passive,
			};
		}

		internal static bool IsLocationEnabled(
			Func<bool> hasGps,
			Func<bool> hasWps,
			Func<TizenLocationType, bool> isEnabled)
		{
			if (hasGps() && IsEnabled(TizenLocationType.Gps))
				return true;
			if (hasWps() && IsEnabled(TizenLocationType.Wps))
				return true;

			return false;

			bool IsEnabled(TizenLocationType type)
			{
				try
				{
					return isEnabled(type);
				}
				catch (NotSupportedException)
				{
					return false;
				}
				catch (ArgumentException)
				{
					return false;
				}
				catch (InvalidOperationException)
				{
					return false;
				}
			}
		}

		internal static double KilometersPerHourToMetersPerSecond(double kilometersPerHour) =>
			kilometersPerHour * 0.277778;
	}
}
