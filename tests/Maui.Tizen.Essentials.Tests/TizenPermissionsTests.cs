using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Verifies the Tizen privilege mapping that keeps
/// <c>Permissions.RequestAsync&lt;Permissions.Camera&gt;()</c> source compatible with the in-box
/// dotnet/maui Tizen backend.
/// </summary>
public class TizenPermissionsTests
{
	[Theory]
	[InlineData(typeof(Permissions.Camera), "http://tizen.org/privilege/camera", false)]
	[InlineData(typeof(Permissions.ContactsRead), "http://tizen.org/privilege/contact.read", true)]
	[InlineData(typeof(Permissions.ContactsWrite), "http://tizen.org/privilege/contact.write", true)]
	[InlineData(typeof(Permissions.Flashlight), "http://tizen.org/privilege/led", false)]
	[InlineData(typeof(Permissions.LaunchApp), "http://tizen.org/privilege/appmanager.launch", false)]
	[InlineData(typeof(Permissions.LocationWhenInUse), "http://tizen.org/privilege/location", true)]
	[InlineData(typeof(Permissions.LocationAlways), "http://tizen.org/privilege/location", true)]
	[InlineData(typeof(Permissions.Microphone), "http://tizen.org/privilege/recorder", false)]
	[InlineData(typeof(Permissions.StorageRead), "http://tizen.org/privilege/mediastorage", true)]
	[InlineData(typeof(Permissions.StorageWrite), "http://tizen.org/privilege/mediastorage", true)]
	[InlineData(typeof(Permissions.Vibrate), "http://tizen.org/privilege/haptic", false)]
	public void MapsWellKnownPermissionsToTizenPrivileges(Type permission, string privilege, bool isRuntime)
	{
		Assert.True(TizenPermissions.TryGetKnownPrivileges(permission, out var privileges));

		var match = Assert.Single(privileges!, p => p.Privilege == privilege);
		Assert.Equal(isRuntime, match.IsRuntime);
	}

	[Fact]
	public void MapsMapsPermissionToAllThreeRequiredPrivileges()
	{
		Assert.True(TizenPermissions.TryGetKnownPrivileges(typeof(Permissions.Maps), out var privileges));

		Assert.Equal(
			new[]
			{
				"http://tizen.org/privilege/internet",
				"http://tizen.org/privilege/mapservice",
				"http://tizen.org/privilege/network.get",
			},
			privileges!.Select(p => p.Privilege).OrderBy(p => p, StringComparer.Ordinal));

		Assert.All(privileges!, p => Assert.False(p.IsRuntime));
	}

	[Fact]
	public void MapsNetworkStatePermissionToInternetAndNetworkGet()
	{
		Assert.True(TizenPermissions.TryGetKnownPrivileges(typeof(Permissions.NetworkState), out var privileges));

		Assert.Equal(
			new[]
			{
				"http://tizen.org/privilege/internet",
				"http://tizen.org/privilege/network.get",
			},
			privileges!.Select(p => p.Privilege).OrderBy(p => p, StringComparer.Ordinal));
	}

	[Theory]
	[InlineData(typeof(Permissions.Battery))]
	[InlineData(typeof(Permissions.Bluetooth))]
	[InlineData(typeof(Permissions.CalendarRead))]
	[InlineData(typeof(Permissions.Media))]
	[InlineData(typeof(Permissions.Phone))]
	[InlineData(typeof(Permissions.Photos))]
	[InlineData(typeof(Permissions.PostNotifications))]
	[InlineData(typeof(Permissions.Sensors))]
	[InlineData(typeof(Permissions.Speech))]
	public void KnowsPermissionsThatTizenDoesNotGate(Type permission)
	{
		Assert.True(TizenPermissions.TryGetKnownPrivileges(permission, out var privileges));
		Assert.Empty(privileges!);
	}

	[Fact]
	public void CoversEveryBuiltInMauiPermissionType()
	{
		var builtIn = typeof(Permissions)
			.GetNestedTypes()
			.Where(t => t.IsClass && !t.IsAbstract && typeof(Permissions.BasePermission).IsAssignableFrom(t))
			.ToList();

		Assert.NotEmpty(builtIn);

		var unmapped = builtIn
			.Where(t => !TizenPermissions.TryGetKnownPrivileges(t, out _))
			.Select(t => t.Name)
			.ToList();

		Assert.Empty(unmapped);
	}

	[Fact]
	public void FallsBackToTheDerivedMappingForSubclassedPermissions()
	{
		Assert.True(TizenPermissions.TryGetKnownPrivileges(typeof(DerivedCameraPermission), out var privileges));
		Assert.Equal("http://tizen.org/privilege/camera", Assert.Single(privileges!).Privilege);
	}

	[Fact]
	public void LeavesCustomPermissionsToOwnTheirBehaviour() =>
		Assert.False(TizenPermissions.TryGetKnownPrivileges(typeof(CustomPermission), out _));

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

	sealed class DerivedCameraPermission : Permissions.Camera
	{
	}

	sealed class CustomPermission : TizenBasePlatformPermission
	{
	}
}
