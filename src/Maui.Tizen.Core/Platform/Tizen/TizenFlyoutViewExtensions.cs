using System;
using Microsoft.Maui;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Applies <see cref="IFlyoutView"/> state to a NUI <see cref="DrawerView"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.FlyoutViewExtensions</c> in dotnet/maui. Behaviour is
	/// preserved exactly, including closing the drawer when the behaviour changes to
	/// <c>Drawer</c>.
	/// </para>
	/// <para>
	/// Owned here because the drawer is a platform primitive; the flyout <em>handler</em> that
	/// drives these belongs to Wave C.
	/// </para>
	/// </remarks>
	public static class TizenFlyoutViewExtensions
	{
		/// <summary>Applies <see cref="IFlyoutView.Flyout"/> as the drawer panel.</summary>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		/// <param name="context">The MAUI context used to realise the flyout content.</param>
		public static void UpdateFlyout(this DrawerView platformDrawerView, IFlyoutView flyoutView, IMauiContext context)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);
			ArgumentNullException.ThrowIfNull(context);

			platformDrawerView.Drawer = flyoutView.Flyout.ToPlatformView(context);
		}

		/// <summary>Applies <see cref="IFlyoutView.Detail"/> as the drawer content.</summary>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		/// <param name="context">The MAUI context used to realise the detail content.</param>
		public static void UpdateDetail(this DrawerView platformDrawerView, IFlyoutView flyoutView, IMauiContext context)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);
			ArgumentNullException.ThrowIfNull(context);

			platformDrawerView.Content = flyoutView.Detail.ToPlatformView(context);
		}

		/// <summary>Opens or closes the drawer to match <see cref="IFlyoutView.IsPresented"/>.</summary>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		public static void UpdateIsPresented(this DrawerView platformDrawerView, IFlyoutView flyoutView)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);

			// Fire and forget, matching dotnet/maui: the animation completing is not something the
			// mapper can await, and awaiting here would deadlock the NUI main loop.
			if (flyoutView.IsPresented)
				_ = platformDrawerView.OpenAsync(true);
			else
				_ = platformDrawerView.CloseAsync(true);
		}

		/// <summary>Applies <see cref="IFlyoutView.FlyoutBehavior"/>.</summary>
		/// <remarks>
		/// Switching to <see cref="DrawerBehavior.Drawer"/> closes the drawer without animating.
		/// Without that, a flyout that was locked open stays visibly open while behaving as a
		/// dismissible drawer.
		/// </remarks>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		public static void UpdateFlyoutBehavior(this DrawerView platformDrawerView, IFlyoutView flyoutView)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);

			platformDrawerView.DrawerBehavior = flyoutView.FlyoutBehavior.ToTizenDrawerBehavior();

			if (platformDrawerView.DrawerBehavior == DrawerBehavior.Drawer)
				_ = platformDrawerView.CloseAsync(false);
		}

		/// <summary>Applies <see cref="IFlyoutView.FlyoutWidth"/>.</summary>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		public static void UpdateFlyoutWidth(this DrawerView platformDrawerView, IFlyoutView flyoutView)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);

			platformDrawerView.DrawerWidth = flyoutView.FlyoutWidth.ToScaledPixel();
		}

		/// <summary>Applies <see cref="IFlyoutView.IsGestureEnabled"/>.</summary>
		/// <param name="platformDrawerView">The platform drawer.</param>
		/// <param name="flyoutView">The cross-platform flyout view.</param>
		public static void UpdateIsGestureEnabled(this DrawerView platformDrawerView, IFlyoutView flyoutView)
		{
			ArgumentNullException.ThrowIfNull(platformDrawerView);
			ArgumentNullException.ThrowIfNull(flyoutView);

			platformDrawerView.IsGestureEnabled = flyoutView.IsGestureEnabled;
		}
	}

	/// <summary>
	/// Maps <see cref="FlyoutBehavior"/> onto the native drawer behaviour.
	/// </summary>
	/// <remarks>
	/// Ported from the <c>ToPlatform(this FlyoutBehavior)</c> overload in dotnet/maui's
	/// <c>FlyoutViewExtensions</c>. Split into its own type, and named
	/// <see cref="ToTizenDrawerBehavior"/> rather than <c>ToPlatform</c>, because MAUI already
	/// defines a large family of <c>ToPlatform</c> extensions - an identically named one on an
	/// enum would be an ambiguity hazard wherever both namespaces are imported.
	/// </remarks>
	public static class TizenFlyoutBehaviorExtensions
	{
		/// <summary>Converts a MAUI flyout behaviour to its NUI drawer counterpart.</summary>
		/// <param name="behavior">The flyout behaviour.</param>
		/// <returns>The native drawer behaviour.</returns>
		public static DrawerBehavior ToTizenDrawerBehavior(this FlyoutBehavior behavior) => behavior switch
		{
			FlyoutBehavior.Disabled => DrawerBehavior.Disabled,
			FlyoutBehavior.Locked => DrawerBehavior.Locked,
			_ => DrawerBehavior.Drawer,
		};

		/// <summary>Gets the native drawer behaviour for a flyout view.</summary>
		/// <param name="flyoutView">The flyout view.</param>
		/// <returns>The native drawer behaviour.</returns>
		public static DrawerBehavior ToTizenDrawerBehavior(this IFlyoutView flyoutView)
		{
			ArgumentNullException.ThrowIfNull(flyoutView);

			return flyoutView.FlyoutBehavior.ToTizenDrawerBehavior();
		}
	}
}
