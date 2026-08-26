// PrivacyPrivilegeManager is marked deprecated from Tizen API level 11 onwards, but Tizen ships no
// replacement for runtime privilege checks, so this backend keeps using it (as dotnet/maui did).
#pragma warning disable CS0618 // Type or member is obsolete

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using TizenPrivacyPrivilegeManager = Tizen.Security.PrivacyPrivilegeManager;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// A Tizen privilege requirement declared by an Essentials permission.
	/// </summary>
	/// <param name="Privilege">The fully qualified Tizen privilege, for example <c>http://tizen.org/privilege/location</c>.</param>
	/// <param name="IsRuntime">
	/// <see langword="true"/> when the privilege is a privacy related privilege that must additionally be
	/// granted by the user at runtime; <see langword="false"/> when declaring it in
	/// <c>tizen-manifest.xml</c> is sufficient.
	/// </param>
	public readonly record struct TizenPrivilege(string Privilege, bool IsRuntime);

	/// <summary>
	/// Base class for user defined Tizen permissions.
	/// </summary>
	/// <remarks>
	/// dotnet/maui declared <c>Permissions.BasePlatformPermission</c> as a Tizen specific partial class.
	/// Partial classes cannot span assemblies, so this standalone backend provides an equivalent base
	/// type. Derive from it to declare additional privileges and pass the derived type to
	/// <see cref="IPermissions.CheckStatusAsync{TPermission}"/> or
	/// <see cref="IPermissions.RequestAsync{TPermission}"/>.
	/// </remarks>
	public abstract class TizenBasePlatformPermission : Permissions.BasePermission
	{
		/// <summary>
		/// Gets the privileges that must be present in the application's <c>tizen-manifest.xml</c>.
		/// </summary>
		public virtual TizenPrivilege[] RequiredPrivileges { get; } = Array.Empty<TizenPrivilege>();

		/// <inheritdoc/>
		public override Task<PermissionStatus> CheckStatusAsync() =>
			TizenPermissions.CheckPrivilegesAsync(RequiredPrivileges, ask: false);

		/// <inheritdoc/>
		public override Task<PermissionStatus> RequestAsync() =>
			TizenPermissions.CheckPrivilegesAsync(RequiredPrivileges, ask: true);

		/// <inheritdoc/>
		public override void EnsureDeclared() =>
			TizenPermissions.EnsureDeclared(RequiredPrivileges);

		/// <inheritdoc/>
		public override bool ShouldShowRationale() => false;
	}

	/// <summary>
	/// Tizen implementation of <see cref="IPermissions"/>, backed by
	/// <see cref="TizenPrivacyPrivilegeManager"/> and the application's <c>tizen-manifest.xml</c>.
	/// </summary>
	/// <remarks>
	/// The built in <c>Permissions.Camera</c>, <c>Permissions.LocationWhenInUse</c>, ... types in the
	/// neutral <c>Microsoft.Maui.Essentials</c> assembly derive from a
	/// <c>Permissions.BasePlatformPermission</c> whose members throw. This implementation therefore
	/// maps those well known permission types to their Tizen privileges itself instead of delegating
	/// to the permission instance, which keeps <c>Permissions.RequestAsync&lt;Permissions.Camera&gt;()</c>
	/// source compatible with the in-box dotnet/maui Tizen backend.
	/// </remarks>
	public sealed class TizenPermissions : IPermissions
	{
		internal static readonly IReadOnlyDictionary<Type, TizenPrivilege[]> KnownPermissionPrivileges =
			new Dictionary<Type, TizenPrivilege[]>
			{
				// Declared with no privileges: Tizen requires nothing for these, so they resolve as granted.
				[typeof(Permissions.Battery)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Bluetooth)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.CalendarRead)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.CalendarWrite)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Media)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.NearbyWifiDevices)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Phone)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Photos)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.PhotosAddOnly)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.PostNotifications)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Reminders)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Sensors)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Sms)] = Array.Empty<TizenPrivilege>(),
				[typeof(Permissions.Speech)] = Array.Empty<TizenPrivilege>(),

				[typeof(Permissions.Camera)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/camera", false),
				},
				[typeof(Permissions.ContactsRead)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/contact.read", true),
				},
				[typeof(Permissions.ContactsWrite)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/contact.write", true),
				},
				[typeof(Permissions.Flashlight)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/led", false),
				},
				[typeof(Permissions.LaunchApp)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/appmanager.launch", false),
				},
				[typeof(Permissions.LocationWhenInUse)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/location", true),
				},
				[typeof(Permissions.LocationAlways)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/location", true),
				},
				[typeof(Permissions.Maps)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/internet", false),
					new TizenPrivilege("http://tizen.org/privilege/mapservice", false),
					new TizenPrivilege("http://tizen.org/privilege/network.get", false),
				},
				[typeof(Permissions.Microphone)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/recorder", false),
				},
				[typeof(Permissions.NetworkState)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/internet", false),
					new TizenPrivilege("http://tizen.org/privilege/network.get", false),
				},
				[typeof(Permissions.StorageRead)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true),
				},
				[typeof(Permissions.StorageWrite)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true),
				},
				[typeof(Permissions.Vibrate)] = new[]
				{
					new TizenPrivilege("http://tizen.org/privilege/haptic", false),
				},
			};

		/// <inheritdoc/>
		public Task<PermissionStatus> CheckStatusAsync<TPermission>()
			where TPermission : Permissions.BasePermission, new() =>
			ResolveAsync<TPermission>(ask: false);

		/// <inheritdoc/>
		public Task<PermissionStatus> RequestAsync<TPermission>()
			where TPermission : Permissions.BasePermission, new() =>
			ResolveAsync<TPermission>(ask: true);

		/// <inheritdoc/>
		/// <remarks>Tizen has no rationale UI contract, so this always returns <see langword="false"/>.</remarks>
		public bool ShouldShowRationale<TPermission>()
			where TPermission : Permissions.BasePermission, new() => false;

		static Task<PermissionStatus> ResolveAsync<TPermission>(bool ask)
			where TPermission : Permissions.BasePermission, new()
		{
			if (TryGetKnownPrivileges(typeof(TPermission), out var privileges))
				return CheckPrivilegesAsync(privileges, ask);

			// Custom permission types own their behaviour (including TizenBasePlatformPermission).
			var permission = new TPermission();
			return ask ? permission.RequestAsync() : permission.CheckStatusAsync();
		}

		internal static bool TryGetKnownPrivileges(Type permissionType, [NotNullWhen(true)] out TizenPrivilege[]? privileges)
		{
			for (var type = permissionType; type is not null; type = type.BaseType)
			{
				if (KnownPermissionPrivileges.TryGetValue(type, out privileges))
					return true;
			}

			privileges = null;
			return false;
		}

		/// <summary>
		/// Determines whether the supplied privilege is declared in the application's <c>tizen-manifest.xml</c>.
		/// </summary>
		/// <param name="tizenPrivilege">The fully qualified Tizen privilege.</param>
		/// <returns><see langword="true"/> when the privilege is declared; otherwise <see langword="false"/>.</returns>
		public static bool IsPrivilegeDeclared(string tizenPrivilege)
		{
			if (string.IsNullOrEmpty(tizenPrivilege))
				return false;

			return TizenPlatform.CurrentPackage.Privileges.Contains(tizenPrivilege);
		}

		/// <summary>
		/// Throws when any privilege required by <typeparamref name="TPermission"/> is missing from
		/// the application's <c>tizen-manifest.xml</c>.
		/// </summary>
		/// <typeparam name="TPermission">The Essentials permission type.</typeparam>
		/// <exception cref="PermissionException">A required privilege is not declared.</exception>
		public static void EnsureDeclared<TPermission>()
			where TPermission : Permissions.BasePermission, new()
		{
			if (TryGetKnownPrivileges(typeof(TPermission), out var privileges))
				EnsureDeclared(privileges);
			else
				new TPermission().EnsureDeclared();
		}

		/// <summary>
		/// Throws when any of the supplied privileges is missing from the application's <c>tizen-manifest.xml</c>.
		/// </summary>
		/// <param name="privileges">The privileges to validate.</param>
		/// <exception cref="PermissionException">A required privilege is not declared.</exception>
		public static void EnsureDeclared(IEnumerable<TizenPrivilege> privileges)
		{
			ArgumentNullException.ThrowIfNull(privileges);

			foreach (var privilege in privileges)
			{
				if (!IsPrivilegeDeclared(privilege.Privilege))
				{
					throw new PermissionException(
						$"You need to declare the privilege: `{privilege.Privilege}` in your tizen-manifest.xml");
				}
			}
		}

		/// <summary>
		/// Ensures the supplied permission is granted, throwing when the user denies it.
		/// </summary>
		/// <typeparam name="TPermission">The Essentials permission type.</typeparam>
		/// <returns>The resulting <see cref="PermissionStatus"/>, which is always <see cref="PermissionStatus.Granted"/>.</returns>
		/// <exception cref="PermissionException">The permission was not granted.</exception>
		public static async Task<PermissionStatus> EnsureGrantedAsync<TPermission>()
			where TPermission : Permissions.BasePermission, new()
		{
			var status = await ResolveAsync<TPermission>(ask: true).ConfigureAwait(false);

			if (status != PermissionStatus.Granted)
				throw new PermissionException($"{typeof(TPermission).Name} permission was not granted: {status}");

			return status;
		}

		internal static async Task<PermissionStatus> CheckPrivilegesAsync(TizenPrivilege[] privileges, bool ask)
		{
			if (privileges is null || privileges.Length == 0)
				return PermissionStatus.Granted;

			EnsureDeclared(privileges);

			foreach (var privilege in privileges.Where(static p => p.IsRuntime))
			{
				var checkResult = TizenPrivacyPrivilegeManager.CheckPermission(privilege.Privilege);

				if (checkResult == global::Tizen.Security.CheckResult.Allow)
					continue;

				if (checkResult == global::Tizen.Security.CheckResult.Deny)
					return PermissionStatus.Denied;

				// CheckResult.Ask
				if (!ask)
					return PermissionStatus.Denied;

				if (!await RequestPrivilegeAsync(privilege.Privilege).ConfigureAwait(false))
					return PermissionStatus.Denied;
			}

			return PermissionStatus.Granted;
		}

		static async Task<bool> RequestPrivilegeAsync(string privilege)
		{
			if (!TizenPrivacyPrivilegeManager.GetResponseContext(privilege).TryGetTarget(out var context))
				return false;

			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			void OnResponseFetched(object? sender, global::Tizen.Security.RequestResponseEventArgs e) =>
				tcs.TrySetResult(e.result == global::Tizen.Security.RequestResult.AllowForever);

			context.ResponseFetched += OnResponseFetched;

			try
			{
				TizenPrivacyPrivilegeManager.RequestPermission(privilege);
				return await tcs.Task.ConfigureAwait(false);
			}
			finally
			{
				context.ResponseFetched -= OnResponseFetched;
			}
		}
	}
}
