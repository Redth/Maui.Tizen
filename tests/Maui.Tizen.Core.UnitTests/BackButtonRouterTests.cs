using System.Collections.Generic;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

public class BackButtonRouterTests
{
	[Fact]
	public void PopupGetsFirstRefusal()
	{
		var route = new List<string>();
		var router = new TizenBackButtonRouter(() =>
		{
			route.Add("Popup");
			return true;
		});
		using var registration = router.Register(() =>
		{
			route.Add("Registered");
			return true;
		});
		router.SetFallback(() =>
		{
			route.Add("Fallback");
			return true;
		});

		Assert.True(router.Invoke());
		Assert.Equal(new[] { "Popup" }, route);
	}

	[Fact]
	public void UnclosedPopupFallsThroughToTheWindowFallback()
	{
		var route = new List<string>();
		var router = new TizenBackButtonRouter(() =>
		{
			route.Add("Popup");
			return false;
		});
		router.SetFallback(() =>
		{
			route.Add("Fallback");
			return true;
		});

		Assert.True(router.Invoke());
		Assert.Equal(new[] { "Popup", "Fallback" }, route);
	}

	[Fact]
	public void DisposedRegistrationRestoresTheFallbackRoute()
	{
		var registeredCalls = 0;
		var fallbackCalls = 0;
		var router = new TizenBackButtonRouter(static () => false);
		router.SetFallback(() =>
		{
			fallbackCalls++;
			return true;
		});
		var registration = router.Register(() =>
		{
			registeredCalls++;
			return true;
		});

		Assert.True(router.Invoke());
		registration.Dispose();
		Assert.True(router.Invoke());

		Assert.Equal(1, registeredCalls);
		Assert.Equal(1, fallbackCalls);
	}
}
