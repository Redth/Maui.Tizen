using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(DisplayDensityCollection.Name)]
	public class DisplayDensityTests : IDisposable
	{
		public void Dispose() => TizenDisplayDensity.SetDensityOverride(null);

		[Fact]
		public void DefaultsToOneWithoutANuiWindow()
		{
			TizenDisplayDensity.SetDensityOverride(null);

			Assert.Equal(1.0, TizenDisplayDensity.Current);
		}

		[Theory]
		[InlineData(1.0, 100.0, 100)]
		[InlineData(2.0, 100.0, 200)]
		[InlineData(2.5, 10.0, 25)]
		[InlineData(1.5, 0.0, 0)]
		public void ToScaledPixelUsesTheCurrentDensity(double density, double dp, int expected)
		{
			TizenDisplayDensity.SetDensityOverride(density);

			Assert.Equal(expected, dp.ToScaledPixel());
		}

		[Fact]
		public void ToScaledPixelMapsInfinityToIntMaxValue()
		{
			TizenDisplayDensity.SetDensityOverride(2.0);

			Assert.Equal(int.MaxValue, double.PositiveInfinity.ToScaledPixel());
		}

		[Fact]
		public void ToScaledDpMapsIntMaxValueBackToInfinity()
		{
			TizenDisplayDensity.SetDensityOverride(2.0);

			Assert.Equal(double.PositiveInfinity, int.MaxValue.ToScaledDP());
		}

		[Theory]
		[InlineData(1.0, 100, 100.0)]
		[InlineData(2.0, 200, 100.0)]
		[InlineData(4.0, 100, 25.0)]
		public void ToScaledDpUsesTheCurrentDensity(double density, int pixels, double expected)
		{
			TizenDisplayDensity.SetDensityOverride(density);

			Assert.Equal(expected, pixels.ToScaledDP());
		}

		[Fact]
		public void PixelAndDpConversionsRoundTrip()
		{
			TizenDisplayDensity.SetDensityOverride(2.0);

			Assert.Equal(64.0, ((double)64.0.ToScaledPixel()).ToScaledDP());
		}

		[Fact]
		public void BaselineDpiMatchesTheMauiConvention() =>
			Assert.Equal(160.0, TizenDisplayDensity.BaselineDpi);
	}

	public class MauiContextTests
	{
		[Fact]
		public void SpecificInstancesShadowTheInnerProvider()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var window = new TizenPlatformWindow();
			var context = new TizenMauiContext(app.Services).AddSpecific(window);

			Assert.Same(window, context.GetPlatformWindow());
		}

		[Fact]
		public void GetPlatformWindowThrowsAClearErrorWhenUnregistered()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var context = new TizenMauiContext(app.Services);

			var ex = Assert.Throws<InvalidOperationException>(() => context.GetPlatformWindow());
			Assert.Contains("TizenMauiApplication", ex.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void GetPlatformWindowOrDefaultReturnsNullWhenUnregistered()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			Assert.Null(new TizenMauiContext(app.Services).GetPlatformWindowOrDefault());
		}

		[Fact]
		public void HandlersFactoryFlowsThroughTheContext()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var context = new TizenMauiContext(app.Services);

			Assert.NotNull(context.Handlers);
			Assert.Equal(
				typeof(Handlers.TizenLabelHandler),
				context.Handlers.GetHandlerType(typeof(ILabel)));
		}

		[Fact]
		public void MakeApplicationScopePublishesThePlatformApplication()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var platformApplication = new TizenPlatformApplication();
			var context = new TizenMauiContext(app.Services).MakeApplicationScope(platformApplication);

			Assert.Same(platformApplication, context.GetPlatformApplicationOrDefault());
		}

		[Fact]
		public void MakeWindowScopePublishesTheWindowAndKeepsTheApplication()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var platformApplication = new TizenPlatformApplication();
			var platformWindow = new TizenPlatformWindow();

			var applicationContext = new TizenMauiContext(app.Services).MakeApplicationScope(platformApplication);
			var windowContext = applicationContext.MakeWindowScope(platformWindow, out var scope);

			using (scope)
			{
				Assert.Same(platformWindow, windowContext.GetPlatformWindow());
				Assert.Same(platformApplication, windowContext.GetPlatformApplicationOrDefault());

				// The application scope must not be polluted by the window scope.
				Assert.Null(applicationContext.GetPlatformWindowOrDefault());
			}
		}

		[Fact]
		public void WithServicesDoesNotMutateTheOriginalContext()
		{
			using var app = MauiApp.CreateBuilder(useDefaults: false).ConfigureTizen().Build();

			var original = new TizenMauiContext(app.Services).AddSpecific(new TizenPlatformApplication());
			using var scope = app.Services.CreateScope();

			var derived = original.WithServices(scope.ServiceProvider).AddSpecific(new TizenPlatformWindow());

			Assert.NotNull(derived.GetPlatformWindowOrDefault());
			Assert.Null(original.GetPlatformWindowOrDefault());
		}

		[Fact]
		public void ConstructorRejectsNullServices() =>
			Assert.Throws<ArgumentNullException>(() => new TizenMauiContext(null!));
	}

	public class TickerTests
	{
		[Fact]
		public void TickerStartsAndStops()
		{
			using var ticker = new TizenTicker();

			Assert.False(ticker.IsRunning);

			ticker.Start();
			Assert.True(ticker.IsRunning);

			ticker.Stop();
			Assert.False(ticker.IsRunning);
		}

		[Fact]
		public void FrameIntervalMatchesDotnetMaui() =>
			Assert.Equal(16, TizenTicker.FrameIntervalMilliseconds);
	}
}
