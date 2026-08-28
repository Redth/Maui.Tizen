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
			base.SetVirtualView(view);
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");

			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing)
			{
				base.Dispose(disposing);
				return;
			}

			var platformView = ((IElementHandler)this).PlatformView as TizenContentViewGroup;
			TizenCleanup.Run(
				() => ClearContent(platformView),
				() => base.Dispose(disposing));
		}

		void ClearContent(TizenContentViewGroup? platformView) =>
			TizenContentOwnership.Clear(
				ref _contentView,
				ref _contentHandler,
				view => platformView?.Children.Remove(view),
				static () => { });

		void UpdateContent()
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			TizenNativeView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (VirtualView.PresentedContent is IView view)
			{
				replacementView = view.ToPlatformView(MauiContext);
				if (view.Handler is ITizenPlatformViewHandler thandler)
					replacementHandler = thandler;
			}

			TizenCleanup.Run(
				() => TizenContentOwnership.Replace(
					ref _contentView,
					ref _contentHandler,
					replacementView,
					replacementHandler,
					oldView => PlatformView.Children.Remove(oldView),
					static () => { }),
				() =>
				{
					if (_contentView is not null)
						PlatformView.Children.Add(_contentView);
				});
		}

		public static void MapContent(TizenSwipeItemViewHandler handler, ISwipeItemView page) => handler.UpdateContent();

		public static void MapVisibility(TizenSwipeItemViewHandler handler, ISwipeItemView view)
		{
			TizenViewMappers.MapVisibility(handler, view);

			var swipeView = handler.PlatformView.GetParentOfType<TizenSwipeViewGroup>();
			swipeView?.UpdateIsVisibleSwipeItem(view);
		}
	}
}
