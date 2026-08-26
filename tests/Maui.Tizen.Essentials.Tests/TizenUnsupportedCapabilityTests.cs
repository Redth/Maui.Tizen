using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
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

		// The token-less overload is part of the contract under test, so it is called as declared.
#pragma warning disable xUnit1051
		Assert.Throws<FeatureNotSupportedException>(() => { _ = authenticator.AuthenticateAsync(null!); });
#pragma warning restore xUnit1051
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
	public void GeocodingReportsUnsupportedBecauseTizenMapsWasRemoved()
	{
		var geocoding = new TizenGeocoding();

		// Tizen.Maps was deprecated at API11 and removed by API15, so neither operation can be
		// backed by anything. Returning an empty sequence would be indistinguishable from a
		// genuine "no match".
		var placemarks = Assert.Throws<FeatureNotSupportedException>(
			() => { _ = geocoding.GetPlacemarksAsync(0, 0); });
		Assert.Contains("Tizen.Maps", placemarks.Message, StringComparison.Ordinal);

		Assert.Throws<FeatureNotSupportedException>(() => { _ = geocoding.GetLocationsAsync("nowhere"); });
	}

	[Fact]
	public void GeocodingStillAcceptsAConfiguredMapServiceTokenWithoutThrowing()
	{
		// The .NET 11 Essentials DI bridge reads and writes IPlatformGeocoding.MapServiceToken
		// during MauiApp initialization. Throwing here would turn a ConfigureEssentials line into
		// a startup crash, so the token is accepted and simply never used.
		var geocoding = new TizenGeocoding { MapServiceToken = "token" };

		Assert.Equal("token", geocoding.MapServiceToken);
	}

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

	/// <summary>
	/// Tripwire for the upstream MAUI fix that would let this backend implement
	/// <see cref="ITextToSpeech.GetLocalesAsync"/> properly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>When this test fails, that is good news: delete it and implement the API.</b>
	/// </para>
	/// <para>
	/// <c>Microsoft.Maui.Media.Locale</c> currently has only an <c>internal</c> constructor, so no
	/// assembly outside <c>Microsoft.Maui.Essentials</c> can construct the values
	/// <see cref="ITextToSpeech.GetLocalesAsync"/> is required to return. That is why
	/// <see cref="TizenTextToSpeech.GetLocalesAsync"/> throws instead of returning an empty
	/// sequence a caller could not distinguish from "this device supports no voices".
	/// </para>
	/// <para>
	/// Asserting the gap rather than re-checking it by hand on every MAUI package bump means the
	/// moment a constructor becomes public, this fails and says exactly what to do next. Verified
	/// still closed against Microsoft.Maui.Essentials 11.0.0-preview.7.26426.4.
	/// </para>
	/// </remarks>
	[Fact]
	public void MauiLocaleStillExposesNoPublicConstructor()
	{
		var constructors = typeof(Locale).GetConstructors(
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

		Assert.True(
			constructors.Length == 0,
			$"Microsoft.Maui.Media.Locale now exposes {constructors.Length} public constructor(s). " +
			$"The blocking API gap is closed: implement {nameof(TizenTextToSpeech)}." +
			$"{nameof(ITextToSpeech.GetLocalesAsync)} properly, drop the " +
			$"{nameof(TizenTextToSpeech.GetSupportedVoiceLanguagesAsync)} workaround from the " +
			"coverage matrix, and delete this test.");
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
