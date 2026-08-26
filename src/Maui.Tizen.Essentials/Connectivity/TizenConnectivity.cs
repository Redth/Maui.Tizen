using System;
using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using TizenCellularState = Tizen.Network.Connection.CellularState;
using TizenConnectionManager = Tizen.Network.Connection.ConnectionManager;
using TizenConnectionState = Tizen.Network.Connection.ConnectionState;
using TizenConnectionType = Tizen.Network.Connection.ConnectionType;
using TizenConnectionTypeEventArgs = Tizen.Network.Connection.ConnectionTypeEventArgs;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IConnectivity"/>, backed by <c>Tizen.Network.Connection</c>.
	/// </summary>
	public sealed class TizenConnectivity : IConnectivity
	{
		readonly object _locker = new();

		EventHandler<ConnectivityChangedEventArgs>? _connectivityChanged;
		bool _listening;

		/// <inheritdoc/>
		public NetworkAccess NetworkAccess
		{
			get
			{
				TizenPermissions.EnsureDeclared<Permissions.NetworkState>();

				return TizenConnectionManager.CurrentConnection.Type switch
				{
					TizenConnectionType.WiFi or
					TizenConnectionType.Cellular or
					TizenConnectionType.Ethernet or
					TizenConnectionType.Bluetooth => NetworkAccess.Internet,
					_ => NetworkAccess.None,
				};
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Queried on every read from <c>ConnectionManager</c>'s per-transport state properties.
		/// <para>
		/// An earlier implementation populated this list only from the profile-list refresh started
		/// when <see cref="ConnectivityChanged"/> gained its first subscriber, so an application
		/// that merely asked the question - the common case - always saw an empty sequence and had
		/// no way to tell that apart from "there are no connections". Reading the state properties
		/// requires no subscription, no asynchronous work and no cached state.
		/// </para>
		/// </remarks>
		public IEnumerable<ConnectionProfile> ConnectionProfiles
		{
			get
			{
				TizenPermissions.EnsureDeclared<Permissions.NetworkState>();

				return GetConnectionProfiles();
			}
		}

		internal static List<ConnectionProfile> GetConnectionProfiles()
		{
			var profiles = new List<ConnectionProfile>(4);

			if (IsConnected(TizenConnectionManager.WiFiState))
				profiles.Add(ConnectionProfile.WiFi);

			if (TizenConnectionManager.CellularState == TizenCellularState.Connected)
				profiles.Add(ConnectionProfile.Cellular);

			if (IsConnected(TizenConnectionManager.EthernetState))
				profiles.Add(ConnectionProfile.Ethernet);

			if (IsConnected(TizenConnectionManager.BluetoothState))
				profiles.Add(ConnectionProfile.Bluetooth);

			return profiles;
		}

		internal static bool IsConnected(TizenConnectionState state) =>
			state == TizenConnectionState.Connected;

		internal static ConnectionProfile? MapProfileType(TizenConnectionType type) =>
			type switch
			{
				TizenConnectionType.Bluetooth => ConnectionProfile.Bluetooth,
				TizenConnectionType.Cellular => ConnectionProfile.Cellular,
				TizenConnectionType.Ethernet => ConnectionProfile.Ethernet,
				TizenConnectionType.WiFi => ConnectionProfile.WiFi,
				_ => null,
			};

		/// <inheritdoc/>
		public event EventHandler<ConnectivityChangedEventArgs> ConnectivityChanged
		{
			add
			{
				lock (_locker)
				{
					var start = _connectivityChanged is null;
					_connectivityChanged += value;
					if (start && _connectivityChanged is not null)
						StartListeners();
				}
			}
			remove
			{
				lock (_locker)
				{
					_connectivityChanged -= value;
					if (_connectivityChanged is null)
						StopListeners();
				}
			}
		}

		void StartListeners()
		{
			if (_listening)
				return;

			TizenPermissions.EnsureDeclared<Permissions.NetworkState>();

			TizenConnectionManager.ConnectionTypeChanged += OnConnectionTypeChanged;
			_listening = true;
		}

		void StopListeners()
		{
			if (!_listening)
				return;

			TizenConnectionManager.ConnectionTypeChanged -= OnConnectionTypeChanged;
			_listening = false;
		}

		void OnConnectionTypeChanged(object? sender, TizenConnectionTypeEventArgs e)
		{
			// Snapshot on the native callback thread, then raise on the main thread.
			var args = new ConnectivityChangedEventArgs(NetworkAccess, GetConnectionProfiles());

			MainThread.BeginInvokeOnMainThread(() =>
			{
				EventHandler<ConnectivityChangedEventArgs>? handler;
				lock (_locker)
					handler = _connectivityChanged;

				handler?.Invoke(this, args);
			});
		}
	}
}
