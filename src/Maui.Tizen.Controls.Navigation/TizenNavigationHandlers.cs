using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Registers the Tizen navigation, Shell, menu and items handlers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Without this, every handler in this assembly is unreachable: MAUI resolves handlers from the
	/// registry, so a handler that is implemented, mapped and tested but never registered is dead
	/// code that silently falls back to whatever the neutral registry provides - which on a
	/// non-Tizen-aware build is nothing at all.
	/// </para>
	/// <para>
	/// These deliberately <b>replace</b> the neutral registrations rather than chaining onto them.
	/// The Tizen handlers declare their own mappers instead of extending the neutral ones, so a
	/// chained registration would run both and double-apply every mapping.
	/// </para>
	/// </remarks>
	public static class TizenNavigationHandlers
	{
		/// <summary>
		/// Adds every Tizen navigation, Shell, menu and items handler to <paramref name="handlers"/>.
		/// </summary>
		/// <remarks>
		/// Call this from a <c>MauiAppBuilder</c> after <c>UseMauiApp</c>:
		/// <code>
		/// builder.ConfigureMauiHandlers(handlers => handlers.AddMauiTizenNavigationHandlers());
		/// </code>
		/// A source test asserts that every concrete handler in this assembly appears here, so
		/// adding a handler without registering it fails the build rather than going unnoticed.
		/// </remarks>
		public static IMauiHandlersCollection AddMauiTizenNavigationHandlers(this IMauiHandlersCollection handlers)
		{
			ArgumentNullException.ThrowIfNull(handlers);

			// Toolbar and menus.
			handlers.AddHandler<Toolbar, TizenToolbarHandler>();
			handlers.AddHandler<MenuBar, TizenMenuBarHandler>();
			handlers.AddHandler<MenuBarItem, TizenMenuBarItemHandler>();
			handlers.AddHandler<MenuFlyout, TizenMenuFlyoutHandler>();
			handlers.AddHandler<MenuFlyoutItem, TizenMenuFlyoutItemHandler>();
			handlers.AddHandler<MenuFlyoutSeparator, TizenMenuFlyoutSeparatorHandler>();
			handlers.AddHandler<MenuFlyoutSubItem, TizenMenuFlyoutSubItemHandler>();

			// Navigation.
			handlers.AddHandler<NavigationPage, TizenNavigationViewHandler>();
			handlers.AddHandler<FlyoutPage, TizenFlyoutViewHandler>();
			handlers.AddHandler<TabbedPage, TizenTabbedPageHandler>();

			// Shell.
			handlers.AddHandler<Shell, TizenShellHandler>();
			handlers.AddHandler<ShellItem, TizenShellItemHandler>();
			handlers.AddHandler<ShellSection, TizenShellSectionHandler>();

			// Items.
			handlers.AddHandler<CollectionView, TizenCollectionViewHandler>();
			handlers.AddHandler<CarouselView, TizenCarouselViewHandler>();

			return handlers;
		}

		/// <summary>
		/// Adds every Tizen navigation, Shell, menu and items handler to <paramref name="builder"/>.
		/// </summary>
		public static MauiAppBuilder UseMauiTizenNavigation(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.ConfigureMauiHandlers(static handlers => handlers.AddMauiTizenNavigationHandlers());

			return builder;
		}
	}
}
