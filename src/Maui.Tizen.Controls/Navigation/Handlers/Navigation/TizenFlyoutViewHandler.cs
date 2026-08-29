using System;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IFlyoutView"/> (the platform side of <c>FlyoutPage</c>).
	/// </summary>
	public partial class TizenFlyoutViewHandler : TizenViewHandler<IFlyoutView, DrawerView>, IFlyoutViewHandler
	{
		EventHandler? _toolbarIconPressed;
		TizenToolbarView? _observedToolbar;

		public static IPropertyMapper<IFlyoutView, TizenFlyoutViewHandler> Mapper =
			new PropertyMapper<IFlyoutView, TizenFlyoutViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IFlyoutView.Flyout)] = MapFlyout,
				[nameof(IFlyoutView.Detail)] = MapDetail,
				[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
				[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
				[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
				[nameof(IFlyoutView.IsGestureEnabled)] = MapIsGestureEnabled,
				[nameof(IToolbarElement.Toolbar)] = MapToolbar,

				// Controls adds this key to the neutral mapper via FlyoutPage.RemapForControls. Wave C
				// declares its own mapper rather than chaining onto the neutral one, so it must declare
				// this explicitly or a FlyoutLayoutBehavior change never reaches the platform.
				["FlyoutLayoutBehavior"] = MapFlyoutLayoutBehavior,
			};

		public static CommandMapper<IFlyoutView, TizenFlyoutViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenFlyoutViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenFlyoutViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IFlyoutView IFlyoutViewHandler.VirtualView => VirtualView;

		object IFlyoutViewHandler.PlatformView => PlatformView;

		protected override DrawerView CreatePlatformView()
			=> global::Tizen.UIExtensions.Common.DeviceInfo.IsTV ? new TizenTVFlyoutView() : new TizenFlyoutView();

		protected override void ConnectHandler(DrawerView platformView)
		{
			platformView.Toggled += OnToggled;
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(DrawerView platformView)
		{
			platformView.Toggled -= OnToggled;

			// The in-tree backend attached an anonymous handler to the toolbar's IconPressed event
			// on every MapToolbar call and never detached it, so repeated toolbar remaps leaked
			// subscriptions and opened the drawer once per remap. Track and detach instead.
			DetachToolbar();

			base.DisconnectHandler(platformView);
		}

		public static void MapFlyout(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
		{
			IMauiContext mauiContext = handler.MauiContext
				?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			handler.PlatformView.UpdateFlyout(flyoutView, mauiContext);
		}

		public static void MapDetail(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
		{
			IMauiContext mauiContext = handler.MauiContext
				?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			var toolbar = handler._observedToolbar;
			if (toolbar is not null && handler.PlatformView is ITizenToolbarContainer oldContainer)
				oldContainer.DetachToolbar(toolbar);

			handler.PlatformView.UpdateDetail(flyoutView, mauiContext);

			if (toolbar is not null && handler.PlatformView is ITizenToolbarContainer newContainer)
				newContainer.SetToolbar(toolbar);
		}

		public static void MapIsPresented(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateIsPresented(flyoutView);

		public static void MapFlyoutBehavior(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateFlyoutBehavior(flyoutView);

		public static void MapFlyoutWidth(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateFlyoutWidth(flyoutView);

		/// <summary>
		/// Re-applies the flyout behaviour when <c>FlyoutPage.FlyoutLayoutBehavior</c> changes.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Not a no-op and not a direct write. <c>FlyoutLayoutBehavior</c> (Popover / Split /
		/// SplitOnLandscape / SplitOnPortrait) is a Controls-level property that is projected into
		/// <see cref="IFlyoutView.FlyoutBehavior"/>: <c>FlyoutPage</c> computes Locked or Flyout from
		/// it. So the correct response is to re-dispatch the <c>FlyoutBehavior</c> mapping and let
		/// the projection do the work - exactly what upstream's
		/// <c>FlyoutPage.MapFlyoutLayoutBehavior</c> does.
		/// </para>
		/// <para>
		/// Without this key a runtime switch between Popover and Split leaves the Tizen drawer in
		/// its previous mode, because nothing tells the handler the projected value changed.
		/// </para>
		/// <para>
		/// Re-dispatching <c>FlyoutBehavior</c> updates the drawer but is not sufficient on its own.
		/// The drawer-toggle capability is derived from the projected behaviour, so Popover (Flyout)
		/// offers a hamburger and Split (Locked) does not. The toolbar's leading slot therefore has
		/// to be redrawn as well, or the hamburger survives a switch to Split and a switch back to
		/// Popover leaves the slot empty.
		/// </para>
		/// </remarks>
		public static void MapFlyoutLayoutBehavior(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
		{
			handler.UpdateValue(nameof(IFlyoutView.FlyoutBehavior));
			handler.RefreshToolbarLeadingIcon();
		}

		/// <summary>
		/// Re-renders the toolbar's leading icon after a change that can affect the drawer toggle.
		/// </summary>
		/// <remarks>
		/// Nothing is latched: the capability is read-only in merged upstream dotnet/maui#37863 (not yet in the pinned package), so this only
		/// asks the toolbar to recompute and redraw.
		/// </remarks>
		internal void RefreshToolbarLeadingIcon()
		{
			if (MauiContext is null
				|| VirtualView is not IToolbarElement { Toolbar: Toolbar toolbar }
				|| _observedToolbar is not { } platformToolbar)
			{
				return;
			}

			if (toolbar.Handler is TizenToolbarHandler toolbarHandler)
				toolbarHandler.UpdateNavigationIcon(toolbar, VirtualView);
			else
				platformToolbar.UpdateBackButton(toolbar, ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, VirtualView));
		}

		public static void MapIsGestureEnabled(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateIsGestureEnabled(flyoutView);

		public static void MapToolbar(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
		{
			IMauiContext mauiContext = handler.MauiContext
				?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			if (handler.VirtualView is not IToolbarElement { Toolbar: { } toolbar })
			{
				handler.DetachToolbar();
				return;
			}

			if (toolbar.ToPlatformView(mauiContext) is not TizenToolbarView platformToolbar)
			{
				handler.DetachToolbar();
				return;
			}

			if (ReferenceEquals(handler._observedToolbar, platformToolbar))
			{
				handler.RefreshToolbarLeadingIcon();
				return;
			}

			handler.DetachToolbar();

			if (handler.PlatformView is ITizenToolbarContainer container)
			{
				container.SetToolbar(platformToolbar);
			}

			handler._observedToolbar = platformToolbar;
			handler._toolbarIconPressed = (_, _) =>
			{
				// Same ownership rule as the shell view, through the same predicate: the drawer only
				// claims the press when the drawer toggle actually owns the slot. Back precedence is
				// not enough on its own, because a Split (Locked) flyout offers no toggle at all.
				if (ToolbarDrawerToggle.ShouldToggleDrawer(toolbar, handler.VirtualView))
				{
					_ = handler.PlatformView.OpenAsync(true);
				}
			};

			platformToolbar.IconPressed += handler._toolbarIconPressed;
		}

		void DetachToolbar()
		{
			if (_observedToolbar is not null && _toolbarIconPressed is not null)
			{
				_observedToolbar.IconPressed -= _toolbarIconPressed;
			}

			_observedToolbar = null;
			_toolbarIconPressed = null;

			if (PlatformView is ITizenToolbarContainer container)
				container.ClearToolbar();
		}

		void OnToggled(object? sender, EventArgs e) => VirtualView.IsPresented = PlatformView.IsOpened;
	}
}
