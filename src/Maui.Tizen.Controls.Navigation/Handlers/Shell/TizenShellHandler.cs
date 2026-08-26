using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="Shell"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The in-tree backend split Shell handling across a neutral partial and a Tizen partial.
	/// This out-of-tree handler declares one complete mapper over the concrete <see cref="Shell"/>
	/// type using only public API.
	/// </para>
	/// </remarks>
	public partial class TizenShellHandler : ViewHandler<Shell, TizenShellView>
	{
		public static IPropertyMapper<Shell, TizenShellHandler> Mapper =
			new PropertyMapper<Shell, TizenShellHandler>(ViewMapper)
			{
				[nameof(IFlyoutView.Flyout)] = MapFlyout,
				[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
				[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
				[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
				[nameof(Shell.FlyoutBackground)] = MapFlyoutBackground,
				[nameof(Shell.CurrentItem)] = MapCurrentItem,
				[nameof(Shell.FlyoutBackdrop)] = MapFlyoutBackdrop,
				[nameof(Shell.FlyoutFooter)] = MapFlyoutFooter,
				[nameof(Shell.FlyoutHeader)] = MapFlyoutHeader,
				[nameof(Shell.FlyoutHeaderBehavior)] = MapFlyoutHeaderBehavior,
				[nameof(Shell.Items)] = MapItems,
				[nameof(Shell.FlyoutContent)] = MapFlyoutContent,
				[nameof(Shell.FlowDirection)] = MapFlowDirection,
				[nameof(Shell.FlyoutBackgroundImage)] = MapFlyoutBackgroundImage,
				[nameof(Shell.FlyoutBackgroundImageAspect)] = MapFlyoutBackgroundImageAspect,
				[nameof(Shell.FlyoutVerticalScrollMode)] = MapFlyoutVerticalScrollMode,
				[nameof(Shell.FlyoutIcon)] = MapFlyoutIcon,
			};

		public static CommandMapper<Shell, TizenShellHandler> CommandMapper =
			new CommandMapper<Shell, TizenShellHandler>(ViewCommandMapper);

		public TizenShellHandler() : base(Mapper, CommandMapper)
		{
		}

		public TizenShellHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenShellView CreatePlatformView()
		{
			var shellView = new TizenShellView();
			shellView.SetElement(VirtualView, MauiContext!);
			return shellView;
		}

		protected override void ConnectHandler(TizenShellView platformView)
		{
			platformView.Toggled += OnToggled;
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenShellView platformView)
		{
			platformView.Toggled -= OnToggled;
			base.DisconnectHandler(platformView);
		}

		public static void MapFlyout(TizenShellHandler handler, IFlyoutView flyoutView)
		{
			handler.PlatformView.UpdateFlyout(flyoutView.Flyout);
		}

		public static void MapIsPresented(TizenShellHandler handler, IFlyoutView flyoutView)
		{
			handler.PlatformView.IsOpened = flyoutView.IsPresented;
		}

		public static void MapFlyoutBehavior(TizenShellHandler handler, IFlyoutView flyoutView)
		{
			handler.PlatformView.UpdateFlyoutBehavior(flyoutView.FlyoutBehavior);
		}

		public static void MapFlyoutWidth(TizenShellHandler handler, IFlyoutView flyoutView)
		{
			handler.PlatformView.UpdateDrawerWidth(flyoutView.FlyoutWidth);
		}

		public static void MapFlyoutBackground(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateBackgroundColor(view.BackgroundColor);
		}

		public static void MapCurrentItem(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateCurrentItem(view.CurrentItem);
		}

		public static void MapFlyoutBackdrop(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateFlyoutBackDrop(view.FlyoutBackdrop);
		}

		public static void MapFlyoutFooter(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateFlyoutFooter(view);
		}

		public static void MapFlyoutHeader(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateFlyoutHeader(view);
		}

		public static void MapFlyoutHeaderBehavior(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateFlyoutHeader(view);
		}

		public static void MapItems(TizenShellHandler handler, IFlyoutView flyoutView)
		{
			handler.PlatformView.UpdateItems();
		}

		public static void MapFlyoutContent(TizenShellHandler handler, Shell view)
		{
			handler.PlatformView.UpdateFlyoutContent(view.FlyoutContent);
		}

		/// <summary>No-op: Tizen does not support FlowDirection on Shell flyout.</summary>
		public static void MapFlowDirection(TizenShellHandler handler, Shell view)
		{
		}

		/// <summary>No-op: Tizen does not support FlyoutBackgroundImage.</summary>
		public static void MapFlyoutBackgroundImage(TizenShellHandler handler, Shell view)
		{
		}

		/// <summary>No-op: Tizen does not support FlyoutBackgroundImageAspect.</summary>
		public static void MapFlyoutBackgroundImageAspect(TizenShellHandler handler, Shell view)
		{
		}

		/// <summary>No-op: Tizen does not support FlyoutVerticalScrollMode.</summary>
		public static void MapFlyoutVerticalScrollMode(TizenShellHandler handler, Shell view)
		{
		}

		/// <summary>No-op: Tizen does not support custom FlyoutIcon.</summary>
		public static void MapFlyoutIcon(TizenShellHandler handler, Shell view)
		{
		}

		void OnToggled(object? sender, EventArgs e)
		{
			if (sender is TizenShellView shellView)
			{
				VirtualView.FlyoutIsPresented = shellView.IsOpened;
			}
		}
	}
}
