using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Verifies the Tizen privilege mapping behind <see cref="TizenPermissions"/>.
/// </summary>
/// <remarks>
/// The central invariant is that a permission never reports <see cref="PermissionStatus.Granted"/>
/// for a capability Tizen actually gates. Every mapping is one of three explicit kinds, and each is
/// asserted for what it claims rather than merely for being present.
/// </remarks>
public class TizenPermissionsTests
{
	/// <summary>
	/// Privileges Tizen treats as privacy privileges: declaring them is necessary but not
	/// sufficient, because the user must also consent at runtime.
	/// </summary>
	/// <remarks>
	/// Taken from the privilege annotations in the TizenFX XML documentation. A privilege in this
	/// set mapped with <c>isRuntime: false</c> would skip the <c>PrivacyPrivilegeManager</c> check
	/// and report Granted for a capability the user may have denied.
	/// </remarks>
	static readonly string[] PrivacyPrivileges =
	[
		"http://tizen.org/privilege/apphistory.read",
		"http://tizen.org/privilege/calendar.read",
		"http://tizen.org/privilege/calendar.write",
		"http://tizen.org/privilege/callhistory.read",
		"http://tizen.org/privilege/callhistory.write",
		"http://tizen.org/privilege/camera",
		"http://tizen.org/privilege/contact.read",
		"http://tizen.org/privilege/contact.write",
		"http://tizen.org/privilege/externalstorage",
		"http://tizen.org/privilege/healthinfo",
		"http://tizen.org/privilege/location",
		"http://tizen.org/privilege/location.coarse",
		"http://tizen.org/privilege/mediastorage",
		"http://tizen.org/privilege/message.read",
		"http://tizen.org/privilege/recorder",
	];

	static TizenPermissionMapping Mapping(Type permission)
	{
		Assert.True(
			TizenPermissions.TryGetKnownMapping(permission, out var mapping),
			$"{permission.Name} has no Tizen mapping.");

		return mapping;
	}

	static (Type Permission, TizenPermissionMapping Mapping)[] KnownMappings() =>
		BuiltInPermissions()
			.Select(t => (Permission: t, Found: TizenPermissions.TryGetKnownMapping(t, out var m), Mapping: m))
			.Where(x => x.Found)
			.Select(x => (x.Permission, x.Mapping))
			.ToArray();

	static Type[] BuiltInPermissions() =>
		typeof(Permissions)
			.GetNestedTypes()
			.Where(t => t.IsClass && !t.IsAbstract && typeof(Permissions.BasePermission).IsAssignableFrom(t))
			.ToArray();

	[Theory]
	[InlineData(typeof(Permissions.Bluetooth), "http://tizen.org/privilege/bluetooth")]
	[InlineData(typeof(Permissions.CalendarRead), "http://tizen.org/privilege/calendar.read")]
	[InlineData(typeof(Permissions.CalendarWrite), "http://tizen.org/privilege/calendar.write")]
	[InlineData(typeof(Permissions.Camera), "http://tizen.org/privilege/camera")]
	[InlineData(typeof(Permissions.ContactsRead), "http://tizen.org/privilege/contact.read")]
	[InlineData(typeof(Permissions.ContactsWrite), "http://tizen.org/privilege/contact.write")]
	[InlineData(typeof(Permissions.Flashlight), "http://tizen.org/privilege/led")]
	[InlineData(typeof(Permissions.LaunchApp), "http://tizen.org/privilege/appmanager.launch")]
	[InlineData(typeof(Permissions.LocationAlways), "http://tizen.org/privilege/location")]
	[InlineData(typeof(Permissions.LocationWhenInUse), "http://tizen.org/privilege/location")]
	[InlineData(typeof(Permissions.Media), "http://tizen.org/privilege/mediastorage")]
	[InlineData(typeof(Permissions.Microphone), "http://tizen.org/privilege/recorder")]
	[InlineData(typeof(Permissions.NearbyWifiDevices), "http://tizen.org/privilege/network.get")]
	[InlineData(typeof(Permissions.Phone), "http://tizen.org/privilege/telephony")]
	[InlineData(typeof(Permissions.Photos), "http://tizen.org/privilege/mediastorage")]
	[InlineData(typeof(Permissions.PhotosAddOnly), "http://tizen.org/privilege/mediastorage")]
	[InlineData(typeof(Permissions.PostNotifications), "http://tizen.org/privilege/notification")]
	[InlineData(typeof(Permissions.Sensors), "http://tizen.org/privilege/healthinfo")]
	[InlineData(typeof(Permissions.Sms), "http://tizen.org/privilege/message.read")]
	[InlineData(typeof(Permissions.Speech), "http://tizen.org/privilege/recorder")]
	[InlineData(typeof(Permissions.StorageRead), "http://tizen.org/privilege/mediastorage")]
	[InlineData(typeof(Permissions.StorageWrite), "http://tizen.org/privilege/mediastorage")]
	[InlineData(typeof(Permissions.Vibrate), "http://tizen.org/privilege/haptic")]
	public void MapsGatedPermissionsToTheirTizenPrivilege(Type permission, string privilege)
	{
		var mapping = Mapping(permission);

		Assert.Equal(TizenPermissionKind.Requires, mapping.Kind);
		Assert.Contains(privilege, mapping.Privileges.Select(p => p.Privilege));
	}

	[Fact]
	public void MarksEveryPrivacyPrivilegeAsRuntime()
	{
		// The regression that motivated this review: Camera and Microphone were mapped with
		// isRuntime: false, so resolution skipped PrivacyPrivilegeManager entirely and reported
		// Granted for a capability the user may well have denied.
		var wrong = KnownMappings()
			.SelectMany(entry => entry.Mapping.Privileges.Select(p => (entry.Permission, Privilege: p)))
			.Where(x => PrivacyPrivileges.Contains(x.Privilege.Privilege, StringComparer.Ordinal) && !x.Privilege.IsRuntime)
			.Select(x => $"{x.Permission.Name} => {x.Privilege.Privilege}")
			.ToList();

		Assert.Empty(wrong);
	}

	[Fact]
	public void NeverClaimsToRequirePrivilegesItDoesNotList()
	{
		// A 'Requires' mapping with no privileges would silently degrade to unconditional Granted.
		var empty = KnownMappings()
			.Where(entry => entry.Mapping.Kind == TizenPermissionKind.Requires && entry.Mapping.Privileges.Length == 0)
			.Select(entry => entry.Permission.Name)
			.ToList();

		Assert.Empty(empty);
	}

	[Fact]
	public void OnlyBatteryIsClaimedUngatedAndItSaysWhy()
	{
		var ungated = KnownMappings()
			.Where(entry => entry.Mapping.Kind == TizenPermissionKind.Ungated)
			.ToList();

		// Ungated is an affirmative claim about the platform, so the set is deliberately tiny and
		// every member has to justify itself.
		Assert.Equal(
			[nameof(Permissions.Battery)],
			ungated.Select(e => e.Permission.Name).OrderBy(n => n, StringComparer.Ordinal));

		Assert.All(ungated, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Mapping.Reason)));
	}

	[Theory]
	[InlineData(typeof(Permissions.Maps))]
	[InlineData(typeof(Permissions.Reminders))]
	public void ReportsUnsupportedForCapabilitiesTizenDoesNotHave(Type permission)
	{
		var mapping = Mapping(permission);

		Assert.Equal(TizenPermissionKind.Unsupported, mapping.Kind);
		Assert.False(string.IsNullOrWhiteSpace(mapping.Reason));
		Assert.Empty(mapping.Privileges);
	}

	[Fact]
	public void UnsupportedPermissionsThrowRatherThanReportingAStatus()
	{
		var permissions = new TizenPermissions();

		Assert.Throws<FeatureNotSupportedException>(() => { _ = permissions.CheckStatusAsync<Permissions.Maps>(); });
		Assert.Throws<FeatureNotSupportedException>(() => { _ = permissions.RequestAsync<Permissions.Maps>(); });
		Assert.Throws<FeatureNotSupportedException>(TizenPermissions.EnsureDeclared<Permissions.Reminders>);
	}

	[Fact]
	public void NoLongerReferencesTheRemovedMapServicePrivilege()
	{
		// Tizen.Maps is gone as of API15, so mapservice gates nothing this package can do.
		// Keeping it would make applications declare a privilege for a dead capability.
		var mapService = KnownMappings()
			.SelectMany(entry => entry.Mapping.Privileges.Select(p => (entry.Permission, p.Privilege)))
			.Where(x => x.Privilege.Contains("mapservice", StringComparison.Ordinal))
			.Select(x => x.Permission.Name)
			.ToList();

		Assert.Empty(mapService);
	}

	[Fact]
	public void ExplainsWhyTheMapsPermissionIsUnsupported()
	{
		var mapping = Mapping(typeof(Permissions.Maps));

		Assert.Equal(TizenPermissionKind.Unsupported, mapping.Kind);
		Assert.Contains("API15", mapping.Reason!, StringComparison.Ordinal);
	}

	[Fact]
	public void MapsNetworkStatePermissionToInternetAndNetworkGet()
	{
		var mapping = Mapping(typeof(Permissions.NetworkState));

		Assert.Equal(
			new[]
			{
				"http://tizen.org/privilege/internet",
				"http://tizen.org/privilege/network.get",
			},
			mapping.Privileges.Select(p => p.Privilege).OrderBy(p => p, StringComparer.Ordinal));

		Assert.All(mapping.Privileges, p => Assert.False(p.IsRuntime));
	}

	[Fact]
	public void CoversEveryBuiltInMauiPermissionType()
	{
		var builtIn = BuiltInPermissions();

		Assert.NotEmpty(builtIn);

		var unmapped = builtIn
			.Where(t => !TizenPermissions.TryGetKnownMapping(t, out _))
			.Select(t => t.Name)
			.ToList();

		Assert.Empty(unmapped);
	}

	[Fact]
	public void UsesFullyQualifiedTizenPrivilegeUris()
	{
		var malformed = KnownMappings()
			.SelectMany(entry => entry.Mapping.Privileges.Select(p => (entry.Permission, p.Privilege)))
			.Where(x => !x.Privilege.StartsWith("http://tizen.org/privilege/", StringComparison.Ordinal))
			.Select(x => $"{x.Permission.Name} => {x.Privilege}")
			.ToList();

		Assert.Empty(malformed);
	}

	[Fact]
	public void FallsBackToTheDerivedMappingForSubclassedPermissions()
	{
		var mapping = Mapping(typeof(DerivedCameraPermission));

		Assert.Equal(TizenPermissionKind.Requires, mapping.Kind);
		Assert.Equal("http://tizen.org/privilege/camera", Assert.Single(mapping.Privileges).Privilege);
	}

	[Fact]
	public void LeavesCustomPermissionsToOwnTheirBehaviour() =>
		Assert.False(TizenPermissions.TryGetKnownMapping(typeof(CustomPermission), out _));

	[Fact]
	public void NeverShowsARationaleBecauseTizenHasNoRationaleContract() =>
		Assert.False(new TizenPermissions().ShouldShowRationale<Permissions.Camera>());

	[Fact]
	public void CustomTizenPermissionsDefaultToNoPrivileges() =>
		Assert.Empty(new CustomPermission().RequiredPrivileges);

	[Fact]
	public void RejectsANullPrivilegeCollection() =>
		Assert.Throws<ArgumentNullException>(static () => TizenPermissions.EnsureDeclared(null!));

	[Fact]
	public void TreatsAnEmptyPrivilegeNameAsUndeclared() =>
		Assert.False(TizenPermissions.IsPrivilegeDeclared(string.Empty));

	[Fact]
	public void RejectsARequiresMappingWithNoPrivileges() =>
		Assert.Throws<ArgumentException>(static () => TizenPermissionMapping.Requires());

	[Theory]
	[InlineData(global::Tizen.Security.RequestResult.AllowForever, true)]
	[InlineData(global::Tizen.Security.RequestResult.DenyForever, false)]
	[InlineData(global::Tizen.Security.RequestResult.DenyOnce, false)]
	public void PermissionAnswerInterpretsOnlyTheRequestedPrivilege(
		global::Tizen.Security.RequestResult result,
		bool expected) =>
		Assert.Equal(
			expected,
			TizenPermissions.InterpretRequestResponse(
				"http://tizen.org/privilege/location",
				global::Tizen.Security.CallCause.Answer,
				result,
				"http://tizen.org/privilege/location"));

	[Fact]
	public void PermissionErrorCauseFailsInsteadOfInterpretingResult() =>
		Assert.Throws<InvalidOperationException>(() =>
			TizenPermissions.InterpretRequestResponse(
				"http://tizen.org/privilege/location",
				global::Tizen.Security.CallCause.Error,
				global::Tizen.Security.RequestResult.AllowForever,
				"http://tizen.org/privilege/location"));

	[Fact]
	public void PermissionAnswerForAnotherPrivilegeFailsClosed() =>
		Assert.Throws<InvalidOperationException>(() =>
			TizenPermissions.InterpretRequestResponse(
				"http://tizen.org/privilege/location",
				global::Tizen.Security.CallCause.Answer,
				global::Tizen.Security.RequestResult.AllowForever,
				"http://tizen.org/privilege/camera"));

	sealed class DerivedCameraPermission : Permissions.Camera
	{
	}

	sealed class CustomPermission : TizenBasePlatformPermission
	{
	}
}
