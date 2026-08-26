using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenBatteryInfo = Tizen.System.Battery;
using TizenBatteryPowerSource = Tizen.System.BatteryPowerSource;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IBattery"/>, backed by <c>Tizen.System.Battery</c>.
	/// </summary>
	/// <remarks>
	/// Tizen has no power-saving-mode query available to applications, so
	/// <see cref="EnergySaverStatus"/> and <see cref="EnergySaverStatusChanged"/> throw rather than
	/// reporting <see cref="Microsoft.Maui.Devices.EnergySaverStatus.Off"/>, which would be
	/// indistinguishable from a real "power saving is disabled" answer.
	/// </remarks>
	public sealed class TizenBattery : IBattery
	{
		const string EnergySaverReason =
			"Tizen exposes no application-visible power saving mode state.";

		readonly object _locker = new();

		EventHandler<BatteryInfoChangedEventArgs>? _batteryInfoChanged;
		bool _listening;

		/// <inheritdoc/>
		public double ChargeLevel => (double)TizenBatteryInfo.Percent / 100;

		/// <inheritdoc/>
		/// <remarks>
		/// Derived from the charging flag, the charge percentage and the connected power source, so
		/// that a device sitting on a charger at 100% reports <see cref="BatteryState.Full"/> and a
		/// device plugged in but not drawing charge reports <see cref="BatteryState.NotCharging"/>
		/// instead of both collapsing into <see cref="BatteryState.Discharging"/>.
		/// </remarks>
		public BatteryState State =>
			MapState(TizenBatteryInfo.IsCharging, TizenBatteryInfo.Percent, TizenBatteryInfo.PowerSource);

		/// <inheritdoc/>
		/// <remarks>Read from Tizen's own power-source reading rather than inferred from charging state.</remarks>
		public BatteryPowerSource PowerSource => MapPowerSource(TizenBatteryInfo.PowerSource);

		internal static BatteryState MapState(bool isCharging, int percent, TizenBatteryPowerSource source)
		{
			var pluggedIn = source != TizenBatteryPowerSource.None;

			if (percent >= 100 && pluggedIn)
				return BatteryState.Full;

			if (isCharging)
				return BatteryState.Charging;

			// Connected to a power source but not charging is a distinct state, and the one users
			// notice: a device on a weak charger reports it while the level slowly falls.
			return pluggedIn ? BatteryState.NotCharging : BatteryState.Discharging;
		}

		internal static BatteryPowerSource MapPowerSource(TizenBatteryPowerSource source) =>
			source switch
			{
				TizenBatteryPowerSource.Ac => BatteryPowerSource.AC,
				TizenBatteryPowerSource.Usb => BatteryPowerSource.Usb,
				TizenBatteryPowerSource.Wireless => BatteryPowerSource.Wireless,
				TizenBatteryPowerSource.None => BatteryPowerSource.Battery,
				_ => BatteryPowerSource.Unknown,
			};

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public EnergySaverStatus EnergySaverStatus =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IBattery)}.{nameof(EnergySaverStatus)}", EnergySaverReason);

		/// <inheritdoc/>
		public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged
		{
			add
			{
				lock (_locker)
				{
					var start = _batteryInfoChanged is null;
					_batteryInfoChanged += value;
					if (start && _batteryInfoChanged is not null)
						StartListeners();
				}
			}
			remove
			{
				lock (_locker)
				{
					_batteryInfoChanged -= value;
					if (_batteryInfoChanged is null)
						StopListeners();
				}
			}
		}

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Thrown when subscribing or unsubscribing.</exception>
		public event EventHandler<EnergySaverStatusChangedEventArgs> EnergySaverStatusChanged
		{
			add => throw TizenEssentialsSupport.NotSupported($"{nameof(IBattery)}.{nameof(EnergySaverStatusChanged)}", EnergySaverReason);
			remove => throw TizenEssentialsSupport.NotSupported($"{nameof(IBattery)}.{nameof(EnergySaverStatusChanged)}", EnergySaverReason);
		}

		void StartListeners()
		{
			if (_listening)
				return;

			TizenBatteryInfo.PercentChanged += OnChanged;
			TizenBatteryInfo.ChargingStateChanged += OnChanged;
			TizenBatteryInfo.LevelChanged += OnChanged;
			_listening = true;
		}

		void StopListeners()
		{
			if (!_listening)
				return;

			TizenBatteryInfo.PercentChanged -= OnChanged;
			TizenBatteryInfo.ChargingStateChanged -= OnChanged;
			TizenBatteryInfo.LevelChanged -= OnChanged;
			_listening = false;
		}

		void OnChanged(object? sender, object e)
		{
			// Snapshot on the native callback thread, then raise on the main thread.
			var args = new BatteryInfoChangedEventArgs(ChargeLevel, State, PowerSource);

			MainThread.BeginInvokeOnMainThread(() =>
			{
				EventHandler<BatteryInfoChangedEventArgs>? handler;
				lock (_locker)
					handler = _batteryInfoChanged;

				handler?.Invoke(this, args);
			});
		}
	}
}
