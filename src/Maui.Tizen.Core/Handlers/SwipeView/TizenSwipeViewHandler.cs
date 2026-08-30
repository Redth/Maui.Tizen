// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.SwipeViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named SwipeViewHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="ISwipeView"/>.</summary>
	public class TizenSwipeViewHandler :
		TizenViewHandler<ISwipeView, TizenSwipeViewGroup>,
		ISwipeViewHandler
	{
		readonly TizenDisconnectingState _disconnecting = new();

		public static IPropertyMapper<ISwipeView, TizenSwipeViewHandler> Mapper =
			new PropertyMapper<ISwipeView, TizenSwipeViewHandler>(
				TizenHandlerMappers.Chain(SwipeViewHandler.Mapper))
			{
				[nameof(IContentView.Content)] = MapContent,
				[nameof(ISwipeView.SwipeTransitionMode)] = MapSwipeTransitionMode,
				[nameof(ISwipeView.LeftItems)] = MapLeftItems,
				[nameof(ISwipeView.TopItems)] = MapTopItems,
				[nameof(ISwipeView.RightItems)] = MapRightItems,
				[nameof(ISwipeView.BottomItems)] = MapBottomItems,
				[nameof(IView.IsEnabled)] = MapIsEnabled,
			};

		public static CommandMapper<ISwipeView, TizenSwipeViewHandler> CommandMapper =
			new(TizenHandlerMappers.ChainCommands(SwipeViewHandler.CommandMapper))
			{
				[nameof(ISwipeView.RequestOpen)] = MapRequestOpen,
				[nameof(ISwipeView.RequestClose)] = MapRequestClose,
			};

		public TizenSwipeViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSwipeViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenSwipeViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ISwipeView ISwipeViewHandler.VirtualView => VirtualView;

		object ISwipeViewHandler.PlatformView => PlatformView;

		protected override TizenSwipeViewGroup CreatePlatformView() => new TizenSwipeViewGroup(VirtualView);

		protected override void ConnectHandler(TizenSwipeViewGroup platformView)
		{
			_disconnecting.Connected();
			base.ConnectHandler(platformView);
		}

		/// <inheritdoc />
		/// <remarks>
		/// The swipe view creates handlers for its content and for every swipe item, so tearing this
		/// handler down must tear those down too or they outlive it.
		/// </remarks>
		protected override void DisconnectHandler(TizenSwipeViewGroup platformView)
		{
			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				platformView.DisposeChildHandlers,
				() => base.DisconnectHandler(platformView));
		}

		public override void SetVirtualView(IView view)
		{
			(((IElementHandler)this).PlatformView as TizenSwipeViewGroup)?.Rebind((ISwipeView)view);
			base.SetVirtualView(view);
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");

			PlatformView.Rebind(VirtualView);
			// Layout stays with the MAUI cross-platform implementation.
			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		public static void MapContent(TizenSwipeViewHandler handler, ISwipeView view)
		{
			if (handler._disconnecting.IsDisconnecting
				|| ((IElementHandler)handler).PlatformView is not TizenSwipeViewGroup platformView)
				return;

			_ = handler.MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");
			_ = handler.VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");

			platformView.UpdateContent();
		}

		public static void MapIsEnabled(TizenSwipeViewHandler handler, ISwipeView swipeView)
		{
			var platformView = Platform(handler);
			if (platformView is null)
				return;

			platformView.UpdateIsSwipeEnabled(swipeView.IsEnabled);
			TizenViewMappers.MapIsEnabled(handler, swipeView);
		}

		public static void MapSwipeTransitionMode(TizenSwipeViewHandler handler, ISwipeView swipeView)
		{
			Platform(handler)?.UpdateSwipeTransitionMode(swipeView.SwipeTransitionMode);
		}

		public static void MapRequestOpen(TizenSwipeViewHandler handler, ISwipeView swipeView, object? args)
		{
			if (args is not SwipeViewOpenRequest request)
			{
				return;
			}

			Platform(handler)?.OnOpenRequested(request);
		}

		public static void MapRequestClose(TizenSwipeViewHandler handler, ISwipeView swipeView, object? args)
		{
			if (args is not SwipeViewCloseRequest request)
			{
				return;
			}

			Platform(handler)?.OnCloseRequested(request);
		}

		public static void MapLeftItems(TizenSwipeViewHandler handler, ISwipeView view) =>
			handler.UpdateItems(TizenSwipeItemsSlot.Left, view.LeftItems);

		public static void MapTopItems(TizenSwipeViewHandler handler, ISwipeView view) =>
			handler.UpdateItems(TizenSwipeItemsSlot.Top, view.TopItems);

		public static void MapRightItems(TizenSwipeViewHandler handler, ISwipeView view) =>
			handler.UpdateItems(TizenSwipeItemsSlot.Right, view.RightItems);

		public static void MapBottomItems(TizenSwipeViewHandler handler, ISwipeView view) =>
			handler.UpdateItems(TizenSwipeItemsSlot.Bottom, view.BottomItems);

		void UpdateItems(TizenSwipeItemsSlot slot, ISwipeItems? items)
		{
			if (_disconnecting.IsDisconnecting
				|| ((IElementHandler)this).PlatformView is not TizenSwipeViewGroup platformView)
				return;

			platformView.UpdateItems(slot, items);
		}

		static TizenSwipeViewGroup? Platform(TizenSwipeViewHandler handler) =>
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenSwipeViewGroup? platformView)
				? platformView
				: null;
	}
}
