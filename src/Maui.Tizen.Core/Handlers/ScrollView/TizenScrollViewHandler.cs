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
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Tizen handler for <see cref="IScrollView"/>.</summary>
	public class TizenScrollViewHandler : ViewHandler<IScrollView, ScrollView>
	{
		public static IPropertyMapper<IScrollView, TizenScrollViewHandler> Mapper =
			new PropertyMapper<IScrollView, TizenScrollViewHandler>(ViewMapper)
			{
				[nameof(IScrollView.Content)] = MapContent,
				[nameof(IScrollView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
				[nameof(IScrollView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
				[nameof(IScrollView.Orientation)] = MapOrientation,
			};

		public static CommandMapper<IScrollView, TizenScrollViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
				[nameof(IScrollView.RequestScrollTo)] = MapRequestScrollTo,
			};

		IPlatformViewHandler? _contentHandler;
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

		protected override ScrollView CreatePlatformView() => new MauiScrollView(VirtualView);

		protected override void ConnectHandler(ScrollView platformView)
		{
			base.ConnectHandler(platformView);

			platformView.Scrolling += OnScrolled;
			platformView.ScrollAnimationEnded += ScrollAnimationEnded;
			platformView.Relayout += OnRelayout;
		}

		protected override void DisconnectHandler(ScrollView platformView)
		{
			if (!platformView.HasBody())
				return;

			base.DisconnectHandler(platformView);
			platformView.Scrolling -= OnScrolled;
			platformView.ScrollAnimationEnded -= ScrollAnimationEnded;
			platformView.Relayout -= OnRelayout;
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

		void UpdateContent(IPlatformViewHandler? content)
		{
			if (_contentHandler != null)
			{
				if (_contentHandler.PlatformView is LayoutViewGroup viewgroup)
				{
					viewgroup.LayoutUpdated -= OnContentLayoutUpdated;
				}

				PlatformView.ContentContainer.Remove(_contentHandler.PlatformView);
				_contentHandler.Dispose();
				_contentHandler = null;
			}
			_contentHandler = content;

			if (_contentHandler != null)
			{
				PlatformView.ContentContainer.Add(_contentHandler.PlatformView);

				if (_contentHandler.PlatformView is LayoutViewGroup viewgroup)
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
			var viewGroup = _contentHandler?.PlatformView as LayoutViewGroup;
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
			if (handler.MauiContext == null || scrollView.PresentedContent == null)
			{
				return;
			}

			scrollView.PresentedContent.ToPlatform(handler.MauiContext);
			if (scrollView.PresentedContent.Handler is IPlatformViewHandler contentHandler)
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

		public static void MapRequestScrollTo(TizenScrollViewHandler handler, IScrollView scrollView, object? args)
		{
			if (args is ScrollToRequest request)
			{
				var x = request.HorizontalOffset;
				var y = request.VerticalOffset;

				var pos = scrollView.Orientation == ScrollOrientation.Vertical ? y : x;

				handler.PlatformView.ScrollTo(pos.ToPixel(), !request.Instant);

				if (request.Instant)
				{
					scrollView.ScrollFinished();
				}
			}
		}
	}
}
