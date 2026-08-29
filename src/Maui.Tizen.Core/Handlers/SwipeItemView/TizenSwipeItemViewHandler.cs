// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.SwipeItemViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named SwipeItemViewHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="ISwipeItemView"/>.</summary>
	public class TizenSwipeItemViewHandler : TizenViewHandler<ISwipeItemView, TizenContentViewGroup>
	{
		public static IPropertyMapper<ISwipeItemView, TizenSwipeItemViewHandler> Mapper =
			new PropertyMapper<ISwipeItemView, TizenSwipeItemViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(ISwipeItemView.Content)] = MapContent,
				[nameof(ISwipeItemView.Visibility)] = MapVisibility,
			};

		public static CommandMapper<ISwipeItemView, TizenSwipeItemViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		ITizenPlatformViewHandler? _contentHandler;
		TizenNativeView? _contentView;
		long _contentGeneration;
		readonly TizenDisconnectingState _disconnecting = new();

		public TizenSwipeItemViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSwipeItemViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenSwipeItemViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenContentViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a TizenContentViewGroup");

			return new TizenContentViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange
			};
		}

		public override void SetVirtualView(IView view)
		{
			(((IElementHandler)this).PlatformView as TizenContentViewGroup)?.Rebind(view);
			base.SetVirtualView(view);
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");

			PlatformView.Rebind(VirtualView);
			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		protected override void ConnectHandler(TizenContentViewGroup platformView)
		{
			_disconnecting.Connected();
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenContentViewGroup platformView)
		{
			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				() => ClearContent(platformView),
				() => base.DisconnectHandler(platformView));
		}

		void ClearContent(TizenContentViewGroup? platformView)
		{
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenContentOwnership.Clear(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				view => platformView?.Children.Remove(view),
				static () => { },
				static () => true);
		}

		void UpdateContent()
		{
			if (_disconnecting.IsDisconnecting
				|| ((IElementHandler)this).PlatformView is not TizenContentViewGroup)
				return;

			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var virtualView = VirtualView;
			var expectedContent = virtualView.PresentedContent;
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenNativeView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (expectedContent is IView view)
			{
				replacementView = view.ToPlatformView(MauiContext);
				if (view.Handler is ITizenPlatformViewHandler thandler)
					replacementHandler = thandler;
			}

			TizenContentOwnership.Replace(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				replacementView,
				replacementHandler,
				oldView => PlatformView.Children.Remove(oldView),
				newView => PlatformView.Children.Add(newView),
				static () => { },
				() =>
					ReferenceEquals(VirtualView, virtualView) &&
					ReferenceEquals(VirtualView.PresentedContent, expectedContent));
		}

		public static void MapContent(TizenSwipeItemViewHandler handler, ISwipeItemView page) => handler.UpdateContent();

		public static void MapVisibility(TizenSwipeItemViewHandler handler, ISwipeItemView view)
		{
			var platformView = Platform(handler);
			if (platformView is null)
				return;

			TizenViewMappers.MapVisibility(handler, view);

			var swipeView = platformView.GetParentOfType<TizenSwipeViewGroup>();
			swipeView?.UpdateIsVisibleSwipeItem(view);
		}

		static TizenContentViewGroup? Platform(TizenSwipeItemViewHandler handler) =>
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenContentViewGroup? platformView)
				? platformView
				: null;
	}
}
