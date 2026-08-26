using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenBatteryInfo = Tizen.System.Battery;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IBattery"/>, backed by <c>Tizen.System.Battery</c>.
	/// </summary>
	/// <remarks>
	/// Tizen has no power-saving-mode query that is available to applications, so
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
		public BatteryState State =>
			TizenBatteryInfo.IsCharging ? BatteryState.Charging : BatteryState.Discharging;

		/// <inheritdoc/>
		public BatteryPowerSource PowerSource =>
			TizenBatteryInfo.IsCharging ? BatteryPowerSource.Usb : BatteryPowerSource.Battery;

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

		void OnChanged(object? sender, object e) =>
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var args = new BatteryInfoChangedEventArgs(ChargeLevel, State, PowerSource);
				_batteryInfoChanged?.Invoke(this, args);
			});
	}
}
