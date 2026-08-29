using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Tizen.UIExtensions.NUI
{
	public class ScrollView : Microsoft.Maui.Platforms.Tizen.TizenPlatformView
	{
		EventHandler? _scrolling;
		EventHandler? _scrollAnimationEnded;
		EventHandler? _relayout;

		public int ScrollingSubscriberCount => _scrolling?.GetInvocationList().Length ?? 0;
		public int ScrollAnimationEndedSubscriberCount => _scrollAnimationEnded?.GetInvocationList().Length ?? 0;
		public int RelayoutSubscriberCount => _relayout?.GetInvocationList().Length ?? 0;

		public event EventHandler? Scrolling
		{
			add => _scrolling += value;
			remove => _scrolling -= value;
		}

		public event EventHandler? ScrollAnimationEnded
		{
			add => _scrollAnimationEnded += value;
			remove => _scrollAnimationEnded -= value;
		}

		public event EventHandler? Relayout
		{
			add => _relayout += value;
			remove => _relayout -= value;
		}

		public Microsoft.Maui.Platforms.Tizen.TizenPlatformContainer ContentContainer { get; } = new();

		public Rect ScrollBound { get; set; }

		public void ScrollTo(int position, bool animated)
		{
		}
	}

	public class Image : Microsoft.Maui.Platforms.Tizen.TizenPlatformView
	{
		public string? ResourceUrl { get; set; }
		public global::Tizen.NUI.Color? ImageColor { get; set; }
	}

	public class Button : Microsoft.Maui.Platforms.Tizen.TizenPlatformView
	{
		public global::Tizen.NUI.Color? BackgroundColor { get; set; }
		public object? IconRelativeOrientation { get; set; }
		public float CornerRadius { get; set; }
		public Image Icon { get; } = new();
		public Tizen.UIExtensions.Common.Color TextColor { get; set; }
	}
}

namespace Tizen.UIExtensions.Common
{
	public static class Log
	{
		public static void Error(string message)
		{
		}
	}

	public readonly struct Color
	{
		public static Color Default => default;
		public static Color Transparent => default;

		public static bool operator !=(Color left, Color right) => !left.Equals(right);

		public static bool operator ==(Color left, Color right) => left.Equals(right);

		public override bool Equals(object? obj) => obj is Color;

		public override int GetHashCode() => 0;
	}

	public sealed class LayoutEventArgs : EventArgs
	{
	}
}

namespace Tizen.NUI
{
	public sealed class Color
	{
		public static Color White { get; } = new();
		public static Color Transparent { get; } = new();

		public Color()
		{
		}

		public Color(float red, float green, float blue, float alpha)
		{
		}
	}
}

namespace Tizen.NUI.Components
{
	public static class Button
	{
		public enum IconOrientation
		{
			Top,
		}
	}
}

namespace Microsoft.Maui.Platforms.Tizen
{
	public sealed class TizenImageSource
		: IDisposable
	{
		public string? ResourceUrl { get; set; }

		public void Dispose()
		{
		}
	}

	public sealed class TizenPlatformContainer
	{
		public System.Collections.Generic.List<TizenPlatformView> Children { get; } = new();

		public float SizeWidth { get; set; }
		public float SizeHeight { get; set; }

		public void Add(TizenPlatformView? view)
		{
			if (view is not null && !Children.Contains(view))
				Children.Add(view);
		}

		public void Remove(TizenPlatformView? view)
		{
			if (view is not null)
				Children.Remove(view);
		}
	}

	public sealed class TizenScrollViewGroup : global::Tizen.UIExtensions.NUI.ScrollView
	{
		public TizenScrollViewGroup(IScrollView view)
		{
		}
	}

	public sealed class TizenImageButtonView : TizenPlatformView
	{
		EventHandler? _clicked;
		EventHandler? _pressed;
		EventHandler? _released;

		public bool Focusable { get; set; }
		public string? ResourceUrl { get; set; }
		public event EventHandler? Clicked
		{
			add => _clicked += value;
			remove => _clicked -= value;
		}
		public event EventHandler? Pressed
		{
			add => _pressed += value;
			remove => _pressed -= value;
		}
		public event EventHandler? Released
		{
			add => _released += value;
			remove => _released -= value;
		}
	}

	public sealed class TizenTouchGraphicsView : TizenPlatformView
	{
		public void Connect(IGraphicsView view)
		{
		}

		public void Disconnect()
		{
		}
	}

	public sealed class TizenShapeView : TizenPlatformView
	{
		public ShapeDrawable? Drawable { get; set; } = new();
	}

	public sealed class ShapeDrawable
	{
		public void UpdateRenderTransform(System.Numerics.Matrix3x2 value)
		{
		}

		public void UpdateFillRule(Microsoft.Maui.Controls.Shapes.FillRule fillRule)
		{
		}

		public void UpdateWindingMode(WindingMode mode)
		{
		}
	}

	public enum WindingMode
	{
		EvenOdd,
		NonZero,
	}

	public sealed class TizenRefreshLayout : TizenPlatformView
	{
		EventHandler? _refreshing;
		TaskCompletionSource? _nativeIdle;
		TizenPlatformView? _contentView;
		ITizenPlatformViewHandler? _contentHandler;
		long _contentGeneration;
		bool _disconnected;

		public event EventHandler? Refreshing
		{
			add => _refreshing += value;
			remove => _refreshing -= value;
		}

		public bool IsRefreshing { get; set; }
		public TizenRefreshStateMachine RefreshState { get; } = new();
		public TizenPlatformView? Content { get; set; }

		public TizenRefreshAction UpdateIsRefreshing(bool refreshing) =>
			RefreshState.Request(refreshing);

		public void ApplyRefreshState(bool refreshing)
		{
			if (_disconnected)
				return;

			if (!refreshing)
				_nativeIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			IsRefreshing = refreshing;
		}

		public void DisposeContentHandler()
		{
			TizenContentOwnership.Clear(
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				view =>
				{
					if (ReferenceEquals(Content, view))
						Content = null;
				},
				static () => { });
		}

		public void MarkDisconnected()
		{
			_disconnected = true;
		}

		public Task WaitForNativeIdleAsync(CancellationToken token) =>
			_nativeIdle?.Task.WaitAsync(token) ?? Task.CompletedTask;

		public void RaiseRefreshing()
		{
			IsRefreshing = true;
			_refreshing?.Invoke(this, EventArgs.Empty);
		}

		public void NotifyNativeIdle() => _nativeIdle?.TrySetResult();

		public void UpdateContent(IView? content, IMauiContext? context)
		{
			TizenPlatformView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (content is not null && context is not null)
			{
				replacementView = content.ToPlatformView(context);
				replacementHandler = content.Handler as ITizenPlatformViewHandler;
			}

			TizenContentOwnership.Replace(
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				replacementView,
				replacementHandler,
				view =>
				{
					if (ReferenceEquals(Content, view))
						Content = null;
				},
				view => Content = view,
				static () => { });
		}
	}

	public sealed class TizenSwipeViewGroup : TizenContentViewGroup
	{
		readonly TizenSwipeItemsSnapshot?[] _items = new TizenSwipeItemsSnapshot?[4];
		public TizenSwipeViewGroup(ISwipeView view)
			: base(view)
		{
		}

		public int StructuralInvalidationCount { get; private set; }

		public void DisposeChildHandlers()
		{
		}

		public void UpdateContent()
		{
		}

		internal void UpdateItems(TizenSwipeItemsSlot slot, ISwipeItems? items)
		{
			var index = (int)slot;
			if (_items[index]?.Matches(items) == true)
				return;

			_items[index] = TizenSwipeItemsSnapshot.Capture(items);
			StructuralInvalidationCount++;
		}

		public void UpdateIsSwipeEnabled(bool enabled)
		{
		}

		public void UpdateSwipeTransitionMode(SwipeTransitionMode mode)
		{
		}

		public void OnOpenRequested(SwipeViewOpenRequest request)
		{
		}

		public void OnCloseRequested(SwipeViewCloseRequest request)
		{
		}

		public void UpdateIsVisibleSwipeItem(ISwipeItem item)
		{
		}
	}

	public sealed class TizenPageControl : TizenPlatformView
	{
		public TizenPageControl(IIndicatorView view) => BoundView = view;

		public IIndicatorView BoundView { get; private set; }
		public bool IsShown { get; private set; }

		public void Rebind(IIndicatorView view)
		{
			BoundView = view;
		}

		public void DisposeTemplatedViewHandler()
		{
		}

		public void UpdateCount()
		{
			IsShown =
				BoundView.Visibility == Visibility.Visible &&
				!(BoundView.HideSingle && BoundView.Count <= 1);
		}

		public void UpdatePosition()
		{
		}

		public void ResetIndicators()
		{
		}
	}

	public static class WaveBHostPlatformExtensions
	{
		public static bool HasBody(this TizenPlatformView view) => !view.IsDisposed;

		public static void Clear(this global::Tizen.UIExtensions.NUI.Image view) =>
			view.ResourceUrl = null;

		public static void Show(this TizenPlatformView view)
		{
		}

		public static void Hide(this TizenPlatformView view)
		{
		}

		public static Rect GetBounds(this TizenPlatformView view) => default;

		public static Rect ToDP(this Rect rect) => rect;

		public static bool ToPlatformVisibility(this Visibility visibility) =>
			visibility == Visibility.Visible;

		public static TizenPlatformView ToPlatformView(this IView view, IMauiContext context)
		{
			if (view.Handler?.PlatformView is TizenPlatformView existing)
				return existing;

			var handler = context.Handlers.GetHandler(view.GetType())
				?? throw new InvalidOperationException($"No handler registered for {view.GetType().Name}.");
			handler.SetMauiContext(context);
			handler.SetVirtualView((IElement)view);

			return handler.PlatformView as TizenPlatformView
				?? throw new InvalidOperationException($"{handler.GetType().Name} created no Tizen platform view.");
		}

		public static T? GetParentOfType<T>(this TizenPlatformView view)
			where T : TizenPlatformView => null;

		public static void UpdateBackground(this TizenPlatformView view, IView virtualView)
		{
		}

		public static void UpdateBackground(this TizenPlatformView view, Paint? paint)
		{
		}

		public static void UpdateAspect(this TizenPlatformView view, IImage virtualView)
		{
		}

		public static void UpdateIsAnimationPlaying(this TizenPlatformView view, IImage virtualView)
		{
		}

		public static void UpdateStrokeColor(this TizenPlatformView view, IButtonStroke virtualView)
		{
		}

		public static void UpdateStrokeThickness(this TizenPlatformView view, IButtonStroke virtualView)
		{
		}

		public static void UpdateCornerRadius(this TizenPlatformView view, IButtonStroke virtualView)
		{
		}

		public static void UpdateDrawable(this TizenPlatformView view, IGraphicsView virtualView)
		{
		}

		public static void UpdateFlowDirection(this TizenPlatformView view, IView virtualView)
		{
		}

		public static void Invalidate(this TizenPlatformView view)
		{
		}

		public static void UpdateShape(this TizenShapeView view, IShapeView virtualView)
		{
		}

		public static void InvalidateShape(this TizenShapeView view, IShapeView virtualView)
		{
		}

		public static void UpdateVisibility(this TizenPlatformView view, IView virtualView)
		{
		}

		public static void UpdateRefreshColor(this TizenRefreshLayout view, IRefreshView virtualView)
		{
		}

		public static void UpdateHorizontalScrollBarVisibility(
			this global::Tizen.UIExtensions.NUI.ScrollView view,
			ScrollBarVisibility visibility)
		{
		}

		public static void UpdateVerticalScrollBarVisibility(
			this global::Tizen.UIExtensions.NUI.ScrollView view,
			ScrollBarVisibility visibility)
		{
		}

		public static void UpdateOrientation(
			this global::Tizen.UIExtensions.NUI.ScrollView view,
			ScrollOrientation orientation)
		{
		}

		public static void UpdateText(this global::Tizen.UIExtensions.NUI.Button view, ISwipeItemMenuItem item)
		{
		}

		public static void UpdateTextColor(this global::Tizen.UIExtensions.NUI.Button view, ITextStyle item)
		{
		}

		public static void UpdateCharacterSpacing(this global::Tizen.UIExtensions.NUI.Button view, ITextStyle item)
		{
		}

		public static void UpdateTizenFont(
			this global::Tizen.UIExtensions.NUI.Button view,
			ITextStyle item,
			IFontManager? manager)
		{
		}

		public static global::Tizen.UIExtensions.Common.Color ToTizenCommonColor(this Color color) => default;

		public static Task<IImageSourceServiceResult<TizenImageSource>?> GetTizenImageAsync(
			this IImageSourceServiceProvider provider,
			IImageSource source,
			CancellationToken token) =>
			Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);
	}
}
