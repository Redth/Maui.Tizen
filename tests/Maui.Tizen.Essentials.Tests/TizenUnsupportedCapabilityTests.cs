using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Verifies that capabilities Tizen cannot provide fail loudly instead of returning
/// success-shaped results (empty collections, <see langword="null"/>, completed tasks).
/// </summary>
public class TizenUnsupportedCapabilityTests
{
	[Fact]
	public void ClipboardReportsNoSilentFallback()
	{
		var clipboard = new TizenClipboard();

		Assert.Throws<FeatureNotSupportedException>(() => _ = clipboard.HasText);
		Assert.Throws<FeatureNotSupportedException>(() => { _ = clipboard.GetTextAsync(); });
		Assert.Throws<FeatureNotSupportedException>(() => { _ = clipboard.SetTextAsync("hello"); });
	}

	[Fact]
	public void AppActionsReportUnsupportedRatherThanAcceptingActions()
	{
		var appActions = new TizenAppActions();

		Assert.False(appActions.IsSupported);
		Assert.Throws<FeatureNotSupportedException>(() => { _ = appActions.GetAsync(); });
		Assert.Throws<FeatureNotSupportedException>(() => { _ = appActions.SetAsync(Array.Empty<AppAction>()); });
	}

	[Fact]
	public void PasskeysReportUnsupported()
	{
		var passkeys = new TizenPasskeys();

		Assert.False(passkeys.IsSupported);
		Assert.Throws<FeatureNotSupportedException>(() => { _ = passkeys.CreateAsync(null!, TestContext.Current.CancellationToken); });
		Assert.Throws<FeatureNotSupportedException>(() => { _ = passkeys.AssertAsync(null!, TestContext.Current.CancellationToken); });
	}

	[Fact]
	public void WebAuthenticatorReportsUnsupported()
	{
		var authenticator = new TizenWebAuthenticator();

		Assert.Throws<FeatureNotSupportedException>(() => { _ = authenticator.AuthenticateAsync(null!); });
		Assert.Throws<FeatureNotSupportedException>(
			() => { _ = authenticator.AuthenticateAsync(null!, TestContext.Current.CancellationToken); });
	}

	[Fact]
	public void AppleSignInReportsUnsupported() =>
		Assert.Throws<FeatureNotSupportedException>(
			static () => { _ = new TizenAppleSignInAuthenticator().AuthenticateAsync(); });

	[Fact]
	public void GeolocationForegroundListeningReportsUnsupported()
	{
		var geolocation = new TizenGeolocation();

		Assert.False(geolocation.IsListeningForeground);
		Assert.Throws<FeatureNotSupportedException>(() => { _ = geolocation.StartListeningForegroundAsync(null!); });
		Assert.Throws<FeatureNotSupportedException>(() => geolocation.StopListeningForeground());
	}

	[Fact]
	public async Task GeolocationHasNoLastKnownLocationBeforeAnyRequest() =>
		Assert.Null(await new TizenGeolocation().GetLastKnownLocationAsync());

	[Fact]
	public void BatteryEnergySaverReportsUnsupportedRatherThanOff()
	{
		var battery = new TizenBattery();

		Assert.Throws<FeatureNotSupportedException>(() => _ = battery.EnergySaverStatus);
		Assert.Throws<FeatureNotSupportedException>(() => battery.EnergySaverStatusChanged += (_, _) => { });
		Assert.Throws<FeatureNotSupportedException>(() => battery.EnergySaverStatusChanged -= (_, _) => { });
	}

	[Fact]
	public void TextToSpeechLocalesSurfaceTheMauiApiGap()
	{
		var exception = Assert.Throws<FeatureNotSupportedException>(
			static () => { _ = new TizenTextToSpeech().GetLocalesAsync(); });

		Assert.Contains("Locale", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UnsupportedMessagesNameTheCapabilityAndTheReason()
	{
		var exception = TizenEssentialsSupport.NotSupported("IClipboard.HasText", "Because Tizen.");

		Assert.Contains("IClipboard.HasText", exception.Message, StringComparison.Ordinal);
		Assert.Contains("Because Tizen.", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ProfileScopedMessagesNameTheSupportedProfiles()
	{
		var exception = TizenEssentialsSupport.NotSupportedOnProfile(
			"ISms.ComposeAsync",
			TizenDeviceProfile.Mobile);

		Assert.Contains("ISms.ComposeAsync", exception.Message, StringComparison.Ordinal);
		Assert.Contains(nameof(TizenDeviceProfile.Mobile), exception.Message, StringComparison.Ordinal);
	}
}
