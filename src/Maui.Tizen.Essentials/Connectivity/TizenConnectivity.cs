using System;
using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using TizenConnectionManager = Tizen.Network.Connection.ConnectionManager;
using TizenConnectionProfileManager = Tizen.Network.Connection.ConnectionProfileManager;
using TizenConnectionProfileType = Tizen.Network.Connection.ConnectionProfileType;
using TizenConnectionType = Tizen.Network.Connection.ConnectionType;
using TizenConnectionTypeEventArgs = Tizen.Network.Connection.ConnectionTypeEventArgs;
using TizenProfileListType = Tizen.Network.Connection.ProfileListType;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IConnectivity"/>, backed by <c>Tizen.Network.Connection</c>.
	/// </summary>
	public sealed class TizenConnectivity : IConnectivity
	{
		readonly object _locker = new();

		List<ConnectionProfile> _profiles = new();
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
					TizenConnectionType.Ethernet => NetworkAccess.Internet,
					_ => NetworkAccess.None,
				};
			}
		}

		/// <inheritdoc/>
		public IEnumerable<ConnectionProfile> ConnectionProfiles
		{
			get
			{
				lock (_locker)
					return _profiles.ToArray();
			}
		}

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

			_ = RefreshProfilesAsync(raise: false);
		}

		void StopListeners()
		{
			if (!_listening)
				return;

			TizenConnectionManager.ConnectionTypeChanged -= OnConnectionTypeChanged;
			_listening = false;
		}

		void OnConnectionTypeChanged(object? sender, TizenConnectionTypeEventArgs e) =>
			_ = RefreshProfilesAsync(raise: true);

		async System.Threading.Tasks.Task RefreshProfilesAsync(bool raise)
		{
			var list = await TizenConnectionProfileManager.GetProfileListAsync(TizenProfileListType.Connected).ConfigureAwait(false);

			var profiles = new List<ConnectionProfile>();
			foreach (var result in list)
			{
				var mapped = MapProfileType(result.Type);
				if (mapped is { } profile)
					profiles.Add(profile);
			}

			lock (_locker)
				_profiles = profiles;

			if (!raise)
				return;

			var args = new ConnectivityChangedEventArgs(NetworkAccess, profiles);
			MainThread.BeginInvokeOnMainThread(() => _connectivityChanged?.Invoke(this, args));
		}

		internal static ConnectionProfile? MapProfileType(TizenConnectionProfileType type) =>
			type switch
			{
				TizenConnectionProfileType.Bt => ConnectionProfile.Bluetooth,
				TizenConnectionProfileType.Cellular => ConnectionProfile.Cellular,
				TizenConnectionProfileType.Ethernet => ConnectionProfile.Ethernet,
				TizenConnectionProfileType.WiFi => ConnectionProfile.WiFi,
				_ => null,
			};
	}
}
