using System;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IFlyoutView"/> (the platform side of <c>FlyoutPage</c>).
	/// </summary>
	public partial class TizenFlyoutViewHandler : ViewHandler<IFlyoutView, DrawerView>, IFlyoutViewHandler
	{
		EventHandler? _toolbarIconPressed;
		MauiToolbar? _observedToolbar;

		public static IPropertyMapper<IFlyoutView, TizenFlyoutViewHandler> Mapper =
			new PropertyMapper<IFlyoutView, TizenFlyoutViewHandler>(ViewMapper)
			{
				[nameof(IFlyoutView.Flyout)] = MapFlyout,
				[nameof(IFlyoutView.Detail)] = MapDetail,
				[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
				[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
				[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
				[nameof(IFlyoutView.IsGestureEnabled)] = MapIsGestureEnabled,
				[nameof(IToolbarElement.Toolbar)] = MapToolbar,
			};

		public static CommandMapper<IFlyoutView, TizenFlyoutViewHandler> CommandMapper = new(ViewCommandMapper);

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
			=> DeviceInfo.IsTV ? new MauiTVFlyoutView() : new MauiFlyoutView();

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

			handler.PlatformView.UpdateDetail(flyoutView, mauiContext);
		}

		public static void MapIsPresented(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateIsPresented(flyoutView);

		public static void MapFlyoutBehavior(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateFlyoutBehavior(flyoutView);

		public static void MapFlyoutWidth(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateFlyoutWidth(flyoutView);

		public static void MapIsGestureEnabled(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
			=> handler.PlatformView.UpdateIsGestureEnabled(flyoutView);

		public static void MapToolbar(TizenFlyoutViewHandler handler, IFlyoutView flyoutView)
		{
			IMauiContext mauiContext = handler.MauiContext
				?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			ViewHandler.MapToolbar(handler, flyoutView);

			handler.DetachToolbar();

			if (handler.VirtualView is not IToolbarElement { Toolbar: { } toolbar })
			{
				return;
			}

			if (toolbar.ToPlatform(mauiContext) is not MauiToolbar platformToolbar)
			{
				return;
			}

			handler._observedToolbar = platformToolbar;
			handler._toolbarIconPressed = (_, _) =>
			{
				if (!toolbar.BackButtonVisible && toolbar.IsVisible)
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
		}

		void OnToggled(object? sender, EventArgs e) => VirtualView.IsPresented = PlatformView.IsOpened;
	}
}
