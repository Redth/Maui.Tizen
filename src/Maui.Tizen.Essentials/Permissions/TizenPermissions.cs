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
	/// How a MAUI permission relates to the Tizen privilege model.
	/// </summary>
	public enum TizenPermissionKind
	{
		/// <summary>
		/// Tizen gates the capability. <see cref="TizenPermissionMapping.Privileges"/> lists the
		/// privileges that must be declared, and any privacy privileges among them must also be
		/// granted by the user at runtime.
		/// </summary>
		Requires,

		/// <summary>
		/// Tizen genuinely does not gate the capability, so it is always available. This is an
		/// affirmative statement about the platform, not an absence of mapping.
		/// </summary>
		Ungated,

		/// <summary>
		/// Tizen has no equivalent capability, so the permission cannot meaningfully be checked or
		/// requested. Callers get <see cref="FeatureNotSupportedException"/> rather than a status.
		/// </summary>
		Unsupported,
	}

	/// <summary>
	/// The Tizen meaning of a single MAUI permission.
	/// </summary>
	/// <remarks>
	/// The three cases are kept distinct on purpose. Collapsing "Tizen does not gate this" and
	/// "Tizen cannot do this at all" into a single empty privilege list makes both report
	/// <see cref="PermissionStatus.Granted"/>, which tells an application it may proceed with a
	/// capability the platform will never provide.
	/// </remarks>
	public readonly record struct TizenPermissionMapping
	{
		TizenPermissionMapping(TizenPermissionKind kind, TizenPrivilege[] privileges, string? reason)
		{
			Kind = kind;
			Privileges = privileges;
			Reason = reason;
		}

		/// <summary>Gets how Tizen treats this permission.</summary>
		public TizenPermissionKind Kind { get; }

		/// <summary>Gets the required privileges. Empty unless <see cref="Kind"/> is <see cref="TizenPermissionKind.Requires"/>.</summary>
		public TizenPrivilege[] Privileges { get; }

		/// <summary>Gets why the permission is ungated or unsupported, for diagnostics.</summary>
		public string? Reason { get; }

		/// <summary>Creates a mapping for a capability Tizen gates behind privileges.</summary>
		/// <param name="privileges">The required privileges.</param>
		/// <returns>The mapping.</returns>
		public static TizenPermissionMapping Requires(params TizenPrivilege[] privileges)
		{
			ArgumentNullException.ThrowIfNull(privileges);

			if (privileges.Length == 0)
				throw new ArgumentException("A 'Requires' mapping must list at least one privilege.", nameof(privileges));

			return new TizenPermissionMapping(TizenPermissionKind.Requires, privileges, null);
		}

		/// <summary>Creates a mapping for a capability Tizen does not gate.</summary>
		/// <param name="reason">Why no privilege is required.</param>
		/// <returns>The mapping.</returns>
		public static TizenPermissionMapping Ungated(string reason) =>
			new(TizenPermissionKind.Ungated, Array.Empty<TizenPrivilege>(), reason);

		/// <summary>Creates a mapping for a capability Tizen does not have.</summary>
		/// <param name="reason">Why Tizen cannot provide the capability.</param>
		/// <returns>The mapping.</returns>
		public static TizenPermissionMapping Unsupported(string reason) =>
			new(TizenPermissionKind.Unsupported, Array.Empty<TizenPrivilege>(), reason);
	}

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
		internal static readonly IReadOnlyDictionary<Type, TizenPermissionMapping> KnownPermissions =
			new Dictionary<Type, TizenPermissionMapping>
			{
				// ---------------------------------------------------------------------------
				// Genuinely ungated on Tizen.
				//
				// Reporting Granted here is a true statement, not a fallback: Tizen.System.Battery
				// reads require no privilege at all.
				// ---------------------------------------------------------------------------
				[typeof(Permissions.Battery)] = TizenPermissionMapping.Ungated(
					"Tizen.System.Battery requires no privilege."),

				// ---------------------------------------------------------------------------
				// Declaration-only privileges. Present in tizen-manifest.xml is sufficient;
				// Tizen does not prompt the user for these.
				// ---------------------------------------------------------------------------
				[typeof(Permissions.Bluetooth)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/bluetooth", false)),

				[typeof(Permissions.Flashlight)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/led", false)),

				[typeof(Permissions.LaunchApp)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/appmanager.launch", false)),

				// MAUI's NearbyWifiDevices models discovering nearby Wi-Fi devices. On Tizen that
				// is Tizen.Network.WiFi scanning, which is gated by these two.
				[typeof(Permissions.NearbyWifiDevices)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/network.get", false),
					new TizenPrivilege("http://tizen.org/privilege/network.profile", false)),

				[typeof(Permissions.NetworkState)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/internet", false),
					new TizenPrivilege("http://tizen.org/privilege/network.get", false)),

				[typeof(Permissions.Phone)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/telephony", false)),

				[typeof(Permissions.PostNotifications)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/notification", false)),

				[typeof(Permissions.Vibrate)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/haptic", false)),

				// ---------------------------------------------------------------------------
				// Tizen privacy privileges. Declaration is necessary but NOT sufficient - the
				// user must also grant them at runtime, so these carry isRuntime: true and are
				// resolved through PrivacyPrivilegeManager.
				// ---------------------------------------------------------------------------
				[typeof(Permissions.CalendarRead)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/calendar.read", true)),

				[typeof(Permissions.CalendarWrite)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/calendar.write", true)),

				[typeof(Permissions.Camera)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/camera", true)),

				[typeof(Permissions.ContactsRead)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/contact.read", true)),

				[typeof(Permissions.ContactsWrite)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/contact.write", true)),

				// Tizen draws no foreground/background distinction for location, so both MAUI
				// location permissions resolve to the same privilege. LocationAlways therefore
				// reports the state of the one consent Tizen actually has, rather than implying a
				// background grant the platform never issued.
				[typeof(Permissions.LocationWhenInUse)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/location", true)),

				[typeof(Permissions.LocationAlways)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/location", true)),

				[typeof(Permissions.Media)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true)),

				[typeof(Permissions.Microphone)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/recorder", true)),

				[typeof(Permissions.Photos)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true)),

				[typeof(Permissions.PhotosAddOnly)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true),
					new TizenPrivilege("http://tizen.org/privilege/content.write", false)),

				// MAUI's Permissions.Sensors models body sensors. On Tizen the body sensors
				// (heart rate, pedometer, sleep monitor) are the ones behind healthinfo; the
				// motion sensors this package exposes need no privilege.
				[typeof(Permissions.Sensors)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/healthinfo", true)),

				// Reading messages is privacy gated. Composing an SMS through an AppControl is
				// not - that path only needs appmanager.launch, which TizenSms declares itself.
				[typeof(Permissions.Sms)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/message.read", true)),

				// Speech recognition on Tizen is Tizen.Uix.Stt, which is gated by recorder.
				[typeof(Permissions.Speech)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/recorder", true)),

				[typeof(Permissions.StorageRead)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true)),

				[typeof(Permissions.StorageWrite)] = TizenPermissionMapping.Requires(
					new TizenPrivilege("http://tizen.org/privilege/mediastorage", true)),

				// ---------------------------------------------------------------------------
				// No Tizen equivalent. These throw rather than reporting Granted, because a
				// caller cannot distinguish "granted" from "this platform silently ignored you".
				// ---------------------------------------------------------------------------
				[typeof(Permissions.Maps)] = TizenPermissionMapping.Unsupported(
					"Tizen.Maps (MapService) was deprecated in TizenFX API11 and removed by API15, " +
					"so the http://tizen.org/privilege/mapservice privilege no longer gates anything " +
					"this platform can do."),

				[typeof(Permissions.Reminders)] = TizenPermissionMapping.Unsupported(
					"Tizen has no reminders store. MAUI's Permissions.Reminders models the Apple " +
					"Reminders database, which has no Tizen counterpart; the calendar permissions " +
					"cover Tizen's calendar."),
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
			if (TryGetKnownMapping(typeof(TPermission), out var mapping))
			{
				return mapping.Kind switch
				{
					TizenPermissionKind.Requires => CheckPrivilegesAsync(mapping.Privileges, ask),
					TizenPermissionKind.Ungated => Task.FromResult(PermissionStatus.Granted),
					_ => throw TizenEssentialsSupport.NotSupported(
						$"{nameof(Permissions)}.{typeof(TPermission).Name}",
						mapping.Reason ?? "Tizen has no equivalent capability."),
				};
			}

			// Custom permission types own their behaviour (including TizenBasePlatformPermission).
			var permission = new TPermission();
			return ask ? permission.RequestAsync() : permission.CheckStatusAsync();
		}

		internal static bool TryGetKnownMapping(Type permissionType, out TizenPermissionMapping mapping)
		{
			for (var type = permissionType; type is not null; type = type.BaseType)
			{
				if (KnownPermissions.TryGetValue(type, out mapping))
					return true;
			}

			mapping = default;
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
			if (!TryGetKnownMapping(typeof(TPermission), out var mapping))
			{
				new TPermission().EnsureDeclared();
				return;
			}

			switch (mapping.Kind)
			{
				case TizenPermissionKind.Requires:
					EnsureDeclared(mapping.Privileges);
					break;

				case TizenPermissionKind.Ungated:
					break;

				default:
					throw TizenEssentialsSupport.NotSupported(
						$"{nameof(Permissions)}.{typeof(TPermission).Name}",
						mapping.Reason ?? "Tizen has no equivalent capability.");
			}
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

			void OnResponseFetched(object? sender, global::Tizen.Security.RequestResponseEventArgs e)
			{
				try
				{
					tcs.TrySetResult(InterpretRequestResponse(
						privilege,
						e.cause,
						e.result,
						e.privilege));
				}
				catch (Exception exception)
				{
					tcs.TrySetException(exception);
				}
			}

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

		internal static bool InterpretRequestResponse(
			string requestedPrivilege,
			global::Tizen.Security.CallCause cause,
			global::Tizen.Security.RequestResult result,
			string responsePrivilege)
		{
			if (cause != global::Tizen.Security.CallCause.Answer)
			{
				throw new InvalidOperationException(
					$"Tizen reported an error while requesting '{requestedPrivilege}'.");
			}

			if (!string.Equals(requestedPrivilege, responsePrivilege, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					$"Tizen returned a permission answer for '{responsePrivilege}' while " +
					$"'{requestedPrivilege}' was pending.");
			}

			return result == global::Tizen.Security.RequestResult.AllowForever;
		}
	}
}
