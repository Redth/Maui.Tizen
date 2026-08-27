using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Controls
{
	/// <summary>
	/// Startup integration that installs the Controls-to-Tizen mappings.
	/// </summary>
	/// <remarks>
	/// Without this, <see cref="TizenControlsMappings.Register"/> had no production caller at all:
	/// the bridge compiled, shipped, and did nothing. Every Controls property it binds -
	/// LineBreakMode, the accessibility annotations - stayed unmapped in a real app while the unit
	/// tests, which call Register directly, passed.
	/// </remarks>
	public static class TizenControlsHostingExtensions
	{
		/// <summary>
		/// Registers the Tizen Controls mappings so they are installed during app startup.
		/// </summary>
		/// <param name="builder">The app builder.</param>
		/// <returns>The builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizenControls(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.TryAddEnumerable(
				ServiceDescriptor.Singleton<IMauiInitializeService, TizenControlsMappingsInitializer>());

			// Register the CONCRETE Controls types, not just the mappers.
			//
			// Core registers its handlers against Core INTERFACES - ILabel, ILayout, IWindow and so
			// on - which is right for a Core-only app. But UseMauiApp registers MAUI's neutral
			// handlers against Controls' CONCRETE types, and a concrete registration always beats
			// an interface one in the handler lookup.
			//
			// So a real Controls app resolved Label to Microsoft.Maui.Handlers.LabelHandler rather
			// than TizenLabelHandler - verified by resolving through IMauiHandlersFactory, which
			// returned the neutral handler for Label, ContentPage, VerticalStackLayout and Window.
			// Every backend handler was unreachable from an actual app while every unit test that
			// registered them by hand passed.
			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Label, TizenLabelHandler>();
				handlers.AddHandler<Microsoft.Maui.Controls.Layout, TizenLayoutHandler>();
				handlers.AddHandler<Page, TizenPageHandler>();
				handlers.AddHandler<Microsoft.Maui.Controls.Window, TizenWindowHandler>();
				handlers.AddHandler<Microsoft.Maui.Controls.Application, TizenApplicationHandler>();
			});

			return builder;
		}
	}

	/// <summary>
	/// Installs the Controls-to-Tizen mappings from <c>MauiApp.Build()</c>.
	/// </summary>
	/// <remarks>
	/// An initialize service rather than a call inside ConfigureTizen, because ordering matters and
	/// this is the point where it can be guaranteed: initializers run during Build, after the app
	/// class and its handlers are registered but before any handler is connected to a view. That is
	/// the window in which the static mappers can still be extended and the extension is certain to
	/// be seen.
	/// </remarks>
	internal sealed class TizenControlsMappingsInitializer : IMauiInitializeService
	{
		/// <inheritdoc />
		public void Initialize(IServiceProvider services) => TizenControlsMappings.Register();
	}
}
