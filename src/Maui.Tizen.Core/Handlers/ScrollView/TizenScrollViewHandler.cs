// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.ScrollViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so that neutral handler no
// longer has a Tizen half to complete and a partial declaration would not bind. This is a
// standalone handler that owns its own mappers.
//
// It is deliberately NOT named ScrollViewHandler: that type still exists in Microsoft.Maui.Core
// and re-declaring the name would be ambiguous for consumers referencing both assemblies.

using System;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;
using TizenScrollView = Tizen.UIExtensions.NUI.ScrollView;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IScrollView"/>.</summary>
	public class TizenScrollViewHandler : TizenViewHandler<IScrollView, TizenScrollView>
	{
		public static IPropertyMapper<IScrollView, TizenScrollViewHandler> Mapper =
			new PropertyMapper<IScrollView, TizenScrollViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IScrollView.Content)] = MapContent,
				[nameof(IScrollView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
				[nameof(IScrollView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
				[nameof(IScrollView.Orientation)] = MapOrientation,
			};

		public static CommandMapper<IScrollView, TizenScrollViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(IScrollView.RequestScrollTo)] = MapRequestScrollTo,
			};

		ITizenPlatformViewHandler? _contentHandler;
		double _cachedWidth;
		double _cachedHeight;
		Size _measureCache;

		public TizenScrollViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenScrollViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenScrollViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenScrollView CreatePlatformView() => new TizenScrollViewGroup(VirtualView);

		protected override void ConnectHandler(TizenScrollView platformView)
		{
			base.ConnectHandler(platformView);

			platformView.Scrolling += OnScrolled;
			platformView.ScrollAnimationEnded += ScrollAnimationEnded;
			platformView.Relayout += OnRelayout;
		}

		protected override void DisconnectHandler(TizenScrollView platformView)
		{
			var contentHandler = _contentHandler;
			_contentHandler = null;
			_cachedWidth = 0;
			_cachedHeight = 0;
			_measureCache = default;

			// ElementHandler clears its PlatformView before this typed callback runs. Every cleanup
			// action therefore uses the captured parameter and the child snapshot, never the
			// PlatformView property.
			TizenCleanup.Run(
				() =>
				{
					if (contentHandler?.PlatformView is TizenLayoutViewGroup viewGroup)
						viewGroup.LayoutUpdated -= OnContentLayoutUpdated;
				},
				() => platformView.ContentContainer.Remove(contentHandler?.PlatformView),
				() =>
				{
					platformView.ContentContainer.SizeWidth = 0;
					platformView.ContentContainer.SizeHeight = 0;
				},
				() => platformView.Scrolling -= OnScrolled,
				() => platformView.ScrollAnimationEnded -= ScrollAnimationEnded,
				() => platformView.Relayout -= OnRelayout,
				() => contentHandler?.Dispose(),
				() => base.DisconnectHandler(platformView));
		}

		void ScrollAnimationEnded(object? sender, EventArgs e)
		{
			VirtualView.ScrollFinished();
		}

		void OnScrolled(object? sender, EventArgs e)
		{
			var region = PlatformView.ScrollBound.ToDP();
			VirtualView.HorizontalOffset = region.X;
			VirtualView.VerticalOffset = region.Y;
		}

		void OnRelayout(object? sender, EventArgs e)
		{
			OnContentLayoutUpdated();
		}

		void UpdateContentSize()
		{
			if (VirtualView != null && VirtualView.PresentedContent != null)
			{
				var width = Math.Max((VirtualView.PresentedContent.Margin.HorizontalThickness + VirtualView.PresentedContent.Frame.Width + VirtualView.Padding.HorizontalThickness).ToScaledPixel(), 100);
				var height = Math.Max((VirtualView.PresentedContent.Margin.VerticalThickness + VirtualView.PresentedContent.Frame.Height + VirtualView.Padding.VerticalThickness).ToScaledPixel(), 100);

				if (_cachedWidth != width)
				{
					PlatformView.ContentContainer.SizeWidth = width;
					_cachedWidth = width;
				}

				if (_cachedHeight != height)
				{
					PlatformView.ContentContainer.SizeHeight = height;
					_cachedHeight = height;
				}
			}
		}

		void UpdateContent(ITizenPlatformViewHandler? content)
		{
			if (_contentHandler != null)
			{
				if (_contentHandler.PlatformView is TizenLayoutViewGroup viewgroup)
				{
					viewgroup.LayoutUpdated -= OnContentLayoutUpdated;
				}

				PlatformView.ContentContainer.Remove(_contentHandler.PlatformView);
				_contentHandler.Dispose();
				_contentHandler = null;
			}
			_contentHandler = content;

			if (_contentHandler is null)
			{
				// Reset the cached extents too, otherwise the next real content is compared against
				// the removed child's size and the container is never resized.
				_cachedWidth = 0;
				_cachedHeight = 0;
				_measureCache = default;
				PlatformView.ContentContainer.SizeWidth = 0;
				PlatformView.ContentContainer.SizeHeight = 0;
				return;
			}

			if (_contentHandler != null)
			{
				PlatformView.ContentContainer.Add(_contentHandler.PlatformView);

				if (_contentHandler.PlatformView is TizenLayoutViewGroup viewgroup)
				{
					viewgroup.LayoutUpdated += OnContentLayoutUpdated;
				}
			}
			UpdateContentSize();
		}

		void OnContentLayoutUpdated(object? sender, global::Tizen.UIExtensions.Common.LayoutEventArgs e)
		{
			OnContentLayoutUpdated();
		}

		// Measurement and arrangement stay with the MAUI cross-platform implementation; this only
		// forwards the platform geometry and syncs the native content container size.
		void OnContentLayoutUpdated()
		{
			var viewGroup = _contentHandler?.PlatformView as TizenLayoutViewGroup;
			if (viewGroup != null)
			{
				viewGroup.IsLayoutUpdating++;
			}

			var platformGeometry = PlatformView.GetBounds().ToDP();
			var measuredSize = VirtualView.CrossPlatformMeasure(platformGeometry.Width, platformGeometry.Height);

			if (_measureCache != measuredSize)
			{
				platformGeometry.X = 0;
				platformGeometry.Y = 0;
				VirtualView.CrossPlatformArrange(platformGeometry);
				UpdateContentSize();
			}
			_measureCache = measuredSize;

			if (viewGroup != null)
			{
				viewGroup.IsLayoutUpdating--;
			}
		}

		public static void MapContent(TizenScrollViewHandler handler, IScrollView scrollView)
		{
			if (handler.MauiContext is null)
			{
				return;
			}

			// A null content is a real state change. The imported code returned early here, so the
			// old child stayed parented and the content container kept its previous size - the
			// scroll view carried on showing content that had been removed.
			if (scrollView.PresentedContent is null)
			{
				handler.UpdateContent(null);
				return;
			}

			scrollView.PresentedContent.ToPlatformView(handler.MauiContext);
			if (scrollView.PresentedContent.Handler is ITizenPlatformViewHandler contentHandler)
			{
				handler.UpdateContent(contentHandler);
			}
		}

		public static void MapHorizontalScrollBarVisibility(TizenScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView?.UpdateHorizontalScrollBarVisibility(scrollView.HorizontalScrollBarVisibility);
		}

		public static void MapVerticalScrollBarVisibility(TizenScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView?.UpdateVerticalScrollBarVisibility(scrollView.VerticalScrollBarVisibility);
		}

		public static void MapOrientation(TizenScrollViewHandler handler, IScrollView scrollView)
		{
			handler.PlatformView?.UpdateOrientation(scrollView.Orientation);
		}

		/// <summary>
		/// Scrolls to the requested offset.
		/// </summary>
		/// <remarks>
		/// <para>
		/// NUI's <c>ScrollableBase.ScrollTo</c> takes a single position along the view's own
		/// scrolling axis; there is no two-axis overload. The imported code handled that by using the
		/// vertical offset only for <see cref="ScrollOrientation.Vertical"/> and the HORIZONTAL
		/// offset for everything else — so a <see cref="ScrollOrientation.Both"/> scroll view sent
		/// the X offset as its vertical position, and scrolling to (0, 500) went nowhere.
		/// </para>
		/// <para>
		/// Each orientation now uses its own axis. For <see cref="ScrollOrientation.Both"/> the
		/// vertical offset is applied, because that is the axis <c>ScrollableBase</c> scrolls by
		/// default; the horizontal component of a simultaneous two-axis programmatic scroll is
		/// UNSUPPORTED and documented in docs/wave-b-mapper-parity.md rather than silently
		/// mistranslated. <see cref="ScrollOrientation.Neither"/> does not scroll at all.
		/// </para>
		/// </remarks>
		public static void MapRequestScrollTo(TizenScrollViewHandler handler, IScrollView scrollView, object? args)
		{
			if (args is not ScrollToRequest request)
			{
				return;
			}

			if (scrollView.Orientation != ScrollOrientation.Neither)
			{
				var offset = scrollView.Orientation == ScrollOrientation.Horizontal
					? request.HorizontalOffset
					: request.VerticalOffset;

				handler.PlatformView.ScrollTo(offset.ToPixel(), !request.Instant);
			}

			// A request that cannot move the view must still complete, or the caller waits forever.
			if (request.Instant || scrollView.Orientation == ScrollOrientation.Neither)
			{
				scrollView.ScrollFinished();
			}
		}
	}
}
