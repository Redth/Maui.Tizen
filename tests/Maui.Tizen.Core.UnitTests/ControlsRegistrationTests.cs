using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Controls;
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
	[Collection(StaticMapperCollection.Name)]
	public class ControlsRegistrationTests
	{
		/// <summary>
		/// Builds an app through the PRODUCTION path only.
		/// </summary>
		/// <remarks>
		/// This used to register the Tizen handlers by hand, which made every assertion below
		/// vacuous: it proved the handlers work when someone wires them up, not that anything
		/// wires them up. Under ConfigureTizenControls alone, Label resolved to MAUI's neutral
		/// LabelHandler - the whole backend was unreachable from a real app and these tests were
		/// green throughout.
		///
		/// Nothing is registered here that an application would not get from the two calls.
		/// </remarks>
		static MauiApp BuildControlsApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<ControlsApp>();
			builder.ConfigureTizen();
			builder.ConfigureTizenControls();

			return builder.Build();
		}

		[Theory]
		[InlineData(typeof(ActivityIndicator), typeof(TizenActivityIndicatorHandler))]
		[InlineData(typeof(Button), typeof(TizenButtonHandler))]
		[InlineData(typeof(CheckBox), typeof(TizenCheckBoxHandler))]
		[InlineData(typeof(DatePicker), typeof(TizenDatePickerHandler))]
		[InlineData(typeof(Editor), typeof(TizenEditorHandler))]
		[InlineData(typeof(Entry), typeof(TizenEntryHandler))]
		[InlineData(typeof(Label), typeof(TizenLabelHandler))]
		[InlineData(typeof(Picker), typeof(TizenPickerHandler))]
		[InlineData(typeof(ProgressBar), typeof(TizenProgressBarHandler))]
		[InlineData(typeof(RadioButton), typeof(TizenRadioButtonHandler))]
		[InlineData(typeof(SearchBar), typeof(TizenSearchBarHandler))]
		[InlineData(typeof(Slider), typeof(TizenSliderHandler))]
		[InlineData(typeof(Stepper), typeof(TizenStepperHandler))]
		[InlineData(typeof(Switch), typeof(TizenSwitchHandler))]
		[InlineData(typeof(TimePicker), typeof(TizenTimePickerHandler))]
		[InlineData(typeof(Application), typeof(TizenApplicationHandler))]
		[InlineData(typeof(ContentView), typeof(TizenContentViewHandler))]
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
		[InlineData(typeof(ActivityIndicator))]
		[InlineData(typeof(Button))]
		[InlineData(typeof(CheckBox))]
		[InlineData(typeof(DatePicker))]
		[InlineData(typeof(Editor))]
		[InlineData(typeof(Entry))]
		[InlineData(typeof(Label))]
		[InlineData(typeof(Picker))]
		[InlineData(typeof(ProgressBar))]
		[InlineData(typeof(RadioButton))]
		[InlineData(typeof(SearchBar))]
		[InlineData(typeof(Slider))]
		[InlineData(typeof(Stepper))]
		[InlineData(typeof(Switch))]
		[InlineData(typeof(TimePicker))]
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
			// Scoped to actual HANDLER contracts - interfaces deriving from IElementHandler -
			// rather than to every exported ITizen* type.
			//
			// The wider form asserted an exact list of all exported ITizen* interfaces, which made
			// it fail for any downstream wave that added an unrelated ITizen* SERVICE interface.
			// That is a real cost with no benefit: a service interface cannot be a parallel handler
			// hierarchy, which is the only thing this test exists to prevent. The exported
			// inventory is pinned separately by TizenPublicInterfaceInventoryTests, which is named
			// for what it actually does.
			var handlerInterfaces = typeof(TizenLabelHandler).Assembly
				.GetExportedTypes()
				.Where(t => t.IsInterface)
				.Where(t => typeof(IElementHandler).IsAssignableFrom(t))
				.Where(t => t != typeof(IElementHandler))
				.Select(t => t.Name)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();

			// ITizenApplicationHandler survives only because MAUI Core ships no IApplicationHandler
			// to implement instead; ITizenPlatformViewHandler because MAUI's IPlatformViewHandler
			// exists only inside the net*-tizen build and re-declaring the name would be CS0433.
			// Every other handler contract is MAUI's own.
			Assert.Equal(new[] { "ITizenApplicationHandler", "ITizenPlatformViewHandler" }, handlerInterfaces);
		}

		[Fact]
		public void NoTizenPrefixedInterfaceShadowsAMauiHandlerInterface()
		{
			// The substance behind the name. A parallel hierarchy would show up as a Tizen-prefixed
			// interface whose name matches one MAUI already ships - ITizenLabelHandler alongside
			// ILabelHandler - which is what forced handlers to choose between the two and blocked
			// Controls mapper composition.
			var mauiHandlerInterfaces = typeof(ILabelHandler).Assembly
				.GetExportedTypes()
				.Where(t => t.IsInterface && typeof(IElementHandler).IsAssignableFrom(t))
				.Select(t => t.Name)
				.ToHashSet(StringComparer.Ordinal);

			Assert.NotEmpty(mauiHandlerInterfaces);

			var shadowing = typeof(TizenLabelHandler).Assembly
				.GetExportedTypes()
				.Where(t => t.IsInterface && t.Name.StartsWith("ITizen", StringComparison.Ordinal))
				// ITizenLabelHandler shadows ILabelHandler.
				.Where(t => mauiHandlerInterfaces.Contains("I" + t.Name["ITizen".Length..]))
				.Select(t => t.Name)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(shadowing);
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
