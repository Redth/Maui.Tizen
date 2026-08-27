using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Proves that MAUI <b>Controls</b> types resolve to this backend's handlers, and that the
	/// handlers implement MAUI's own handler interfaces so Controls' mapper composition can reach
	/// them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This suite is the reason the backend-owned <c>ITizen*Handler</c> hierarchy was removed. That
	/// hierarchy rested on the belief that MAUI's handler interfaces bound <c>PlatformView</c> to a
	/// per-TFM alias and could not be implemented out of repo. On the neutral package they declare
	/// <c>object PlatformView</c> and are implementable; the compiler error that prompted the
	/// workaround came from returning the concrete platform type from the explicit implementation.
	/// </para>
	/// <para>
	/// Implementing MAUI's interfaces is what makes Controls composition possible at all:
	/// <c>Label.RemapForControls()</c> mutates the <b>static</b> <c>LabelHandler.Mapper</c>, so a
	/// backend can only see those entries by chaining that same mapper - which in turn requires the
	/// mapper to be typed against <c>ILabelHandler</c>.
	/// </para>
	/// </remarks>
	public class ControlsRegistrationTests
	{
		static MauiApp BuildControlsApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<ControlsApp>();
			builder.ConfigureTizen();
			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Label, TizenLabelHandler>();
				handlers.AddHandler<ContentPage, TizenPageHandler>();
				handlers.AddHandler<Microsoft.Maui.Controls.Layout, TizenLayoutHandler>();
				handlers.AddHandler<Window, TizenWindowHandler>();
			});

			return builder.Build();
		}

		[Theory]
		[InlineData(typeof(Label), typeof(TizenLabelHandler))]
		[InlineData(typeof(ContentPage), typeof(TizenPageHandler))]
		[InlineData(typeof(VerticalStackLayout), typeof(TizenLayoutHandler))]
		[InlineData(typeof(Grid), typeof(TizenLayoutHandler))]
		[InlineData(typeof(Window), typeof(TizenWindowHandler))]
		public void ControlsTypeResolvesToTheTizenHandler(Type controlType, Type expectedHandler)
		{
			using var app = BuildControlsApp();

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(expectedHandler, handlers.GetHandlerType(controlType));
		}

		[Theory]
		[InlineData(typeof(Label))]
		[InlineData(typeof(VerticalStackLayout))]
		public void ResolvedControlsHandlerIsConstructible(Type controlType)
		{
			using var app = BuildControlsApp();

			var handler = app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(controlType);

			Assert.NotNull(handler);
			Assert.IsAssignableFrom<IViewHandler>(handler);
		}

		[Fact]
		public void HandlersImplementMauisOwnHandlerInterfaces()
		{
			// The whole point of the correction: these are MAUI's interfaces, not parallel ones.
			Assert.IsAssignableFrom<ILabelHandler>(new TizenLabelHandler());
			Assert.IsAssignableFrom<IContentViewHandler>(new TizenContentViewHandler());
			Assert.IsAssignableFrom<IPageHandler>(new TizenPageHandler());
			Assert.IsAssignableFrom<ILayoutHandler>(new TizenLayoutHandler());
			Assert.IsAssignableFrom<IWindowHandler>(new TizenWindowHandler());
		}

		[Fact]
		public void NoParallelTizenHandlerInterfacesRemain()
		{
			// ITizenApplicationHandler is the sole survivor, and only because MAUI Core ships no
			// IApplicationHandler to implement instead.
			//
			// Wave A adds two more ITizen* names, and neither is a parallel handler hierarchy -
			// this test's pattern is a name prefix, so it sees them:
			//   ITizenFontManager - MAUI's neutral IFontManager carries only DefaultFontSize; the
			//     font-resolution members exist solely in each platform's own build of MAUI, which
			//     this backend does not consume, so the resolution contract is declared here.
			//   ITizenModalHost   - the seam to the navigation wave's modal stack that Wave A's
			//     pickers open through, pending that wave supplying a real implementation.
			var backendInterfaces = typeof(TizenLabelHandler).Assembly
				.GetExportedTypes()
				.Where(t => t.IsInterface && t.Name.StartsWith("ITizen", StringComparison.Ordinal))
				.Select(t => t.Name)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();

			Assert.Equal(
				new[]
				{
					"ITizenApplicationHandler",
					"ITizenFontManager",
					"ITizenModalHost",
					"ITizenPlatformViewHandler",
				},
				backendInterfaces);
		}

		[Fact]
		public void MapperChainsMauisStaticMapperSoControlsRemappingIsReachable()
		{
			// Label.RemapForControls() mutates the static LabelHandler.Mapper. Chaining it is the
			// only way a backend handler can observe those entries.
			var chained = ((PropertyMapper)TizenLabelHandler.Mapper).Chained;

			Assert.NotNull(chained);
			Assert.Contains(LabelHandler.Mapper, chained!);
		}

		[Fact]
		public void LayoutMapperChainsMauisStaticLayoutMapper()
		{
			var chained = ((PropertyMapper)TizenLayoutHandler.Mapper).Chained;

			Assert.NotNull(chained);
			Assert.Contains(LayoutHandler.Mapper, chained!);
		}

		[Fact]
		public void ControlsOnlyMapperKeysResolveThroughTheChain()
		{
			// A key that exists only because Controls remapped it must be reachable from this
			// backend's mapper. Touching Controls' Label type forces RemapForControls to run.
			_ = new Label();

			var controlsOnlyKeys = LabelHandler.Mapper.GetKeys()
				.Where(k => TizenLabelHandler.Mapper.GetProperty(k) is null)
				.ToArray();

			Assert.Empty(controlsOnlyKeys);
		}

		[Fact]
		public void TizenBehaviourStillWinsForKeysThisBackendImplements()
		{
			// Chaining must not let MAUI's neutral no-op bodies shadow the real Tizen ones.
			_ = new Label();

			Assert.NotSame(
				LabelHandler.Mapper.GetProperty(nameof(ILabel.Text)),
				TizenLabelHandler.Mapper.GetProperty(nameof(ILabel.Text)));
		}

		sealed class ControlsApp : Application
		{
			protected override Window CreateWindow(IActivationState? activationState) =>
				new(new ContentPage());
		}
	}
}
