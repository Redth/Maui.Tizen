using System;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	internal static class ItemsScrollCoordinator
	{
		public static void Publish(
			int itemCount,
			int remainingItemsThreshold,
			double horizontalDelta,
			double horizontalOffset,
			double verticalDelta,
			double verticalOffset,
			int firstVisibleItemIndex,
			int centerItemIndex,
			int lastVisibleItemIndex,
			Action<ItemsViewScrolledEventArgs> scrolled,
			Action thresholdReached)
		{
			ArgumentNullException.ThrowIfNull(scrolled);
			ArgumentNullException.ThrowIfNull(thresholdReached);

			scrolled(new ItemsViewScrolledEventArgs
			{
				HorizontalDelta = horizontalDelta,
				HorizontalOffset = horizontalOffset,
				VerticalDelta = verticalDelta,
				VerticalOffset = verticalOffset,
				FirstVisibleItemIndex = firstVisibleItemIndex,
				CenterItemIndex = centerItemIndex,
				LastVisibleItemIndex = lastVisibleItemIndex,
			});

			if (remainingItemsThreshold >= 0
				&& itemCount - 1 - lastVisibleItemIndex <= remainingItemsThreshold)
			{
				thresholdReached();
			}
		}
	}

	internal sealed class CarouselFeedbackCoordinator
	{
		readonly BidirectionalUpdateGate _gate = new();
		int? _pendingManagedPosition;

		public bool IsApplyingNative => _gate.IsApplyingNative;

		public bool IsApplyingManaged => _gate.IsApplyingManaged;

		public void ApplyManaged(int? expectedPosition, Action update)
		{
			_pendingManagedPosition = expectedPosition;
			_gate.ApplyManaged(update);
		}

		public bool ApplyManagedCurrentItem(
			object? currentItem,
			int count,
			Func<object, int> getIndex,
			Action<int> setPosition,
			Action updateNative)
		{
			ArgumentNullException.ThrowIfNull(getIndex);
			ArgumentNullException.ThrowIfNull(setPosition);
			ArgumentNullException.ThrowIfNull(updateNative);
			if (IsApplyingNative || IsApplyingManaged)
				return false;

			var index = currentItem is null ? -1 : getIndex(currentItem);
			if (index < 0 || index >= count)
			{
				_pendingManagedPosition = null;
				return false;
			}
			ApplyManaged(index, () =>
			{
				setPosition(index);
				updateNative();
			});
			return true;
		}

		public bool ApplyManagedPosition(
			int position,
			int count,
			Func<int, object?> getItem,
			Action<object?> setCurrentItem,
			Action updateNative)
		{
			ArgumentNullException.ThrowIfNull(getItem);
			ArgumentNullException.ThrowIfNull(setCurrentItem);
			ArgumentNullException.ThrowIfNull(updateNative);
			if (IsApplyingNative || IsApplyingManaged)
				return false;

			if (position < 0 || position >= count)
			{
				_pendingManagedPosition = null;
				return false;
			}
			ApplyManaged(position, () =>
			{
				setCurrentItem(getItem(position));
				updateNative();
			});
			return true;
		}

		public bool ApplyNative(
			int position,
			int count,
			Func<int, object?> getItem,
			Action<int> setPosition,
			Action<object?> setCurrentItem)
		{
			if (_gate.IsApplyingManaged || position < 0 || position >= count)
				return false;

			if (_pendingManagedPosition == position)
			{
				_pendingManagedPosition = null;
				return false;
			}

			_pendingManagedPosition = null;

			return _gate.ApplyNative(() =>
			{
				setPosition(position);
				setCurrentItem(getItem(position));
			});
		}
	}

	internal sealed class DeferredCarouselPosition
	{
		(int Position, bool Animate)? _position;
		object? _currentItem;
		bool _hasCurrentItem;
		bool _animateCurrentItem;

		public void SetPosition(int position, bool animate = false)
		{
			_hasCurrentItem = false;
			_currentItem = null;
			_position = (position, animate);
		}

		public void SetCurrentItem(object? currentItem, bool animate = false)
		{
			_currentItem = currentItem;
			_hasCurrentItem = currentItem is not null;
			_animateCurrentItem = animate;
			if (_hasCurrentItem)
				_position = null;
		}

		public void Clear()
		{
			_position = null;
			_currentItem = null;
			_hasCurrentItem = false;
			_animateCurrentItem = false;
		}

		public bool TryApply(
			bool hasLayout,
			int itemCount,
			Func<object?, int> getItemIndex,
			Action<int, bool> scrollTo)
		{
			ArgumentNullException.ThrowIfNull(getItemIndex);
			ArgumentNullException.ThrowIfNull(scrollTo);

			if (!hasLayout)
				return false;

			var applied = false;
			if (_position is { } target)
			{
				if (target.Position >= 0 && target.Position < itemCount)
				{
					scrollTo(target.Position, target.Animate);
					applied = true;
				}
				_position = null;
			}

			if (_hasCurrentItem)
			{
				var index = getItemIndex(_currentItem);
				if (index >= 0 && index < itemCount)
				{
					scrollTo(index, _animateCurrentItem);
					_hasCurrentItem = false;
					_currentItem = null;
					applied = true;
				}
			}

			return applied;
		}
	}

	internal static class CarouselVisualState
	{
		public static string ForIndex(int index, int currentIndex)
		{
			if (index == currentIndex)
				return CarouselView.CurrentItemVisualState;
			if (index == currentIndex - 1)
				return CarouselView.PreviousItemVisualState;
			if (index == currentIndex + 1)
				return CarouselView.NextItemVisualState;
			return CarouselView.DefaultItemVisualState;
		}
	}

	internal sealed class CarouselInteractionState
	{
		bool _dragging;
		bool _animating;

		public bool IsDragging => _dragging;

		public bool IsScrolling => _dragging || _animating;

		public void BeginDrag() => _dragging = true;

		public void EndDrag() => _dragging = false;

		public void BeginAnimation() => _animating = true;

		public void EndAnimation() => _animating = false;

		public void Reset()
		{
			_dragging = false;
			_animating = false;
		}
	}

	internal sealed class CarouselViewportTracker
	{
		double _width;
		double _height;

		public bool Update(double width, double height)
		{
			if (width <= 0 || height <= 0 || (_width == width && _height == height))
				return false;

			_width = width;
			_height = height;
			return true;
		}

		public void Reset()
		{
			_width = 0;
			_height = 0;
		}
	}

	internal readonly record struct ItemsLayoutSnapshot(
		bool IsHorizontal,
		int Span,
		double ItemSpacing,
		double VerticalItemSpacing,
		double HorizontalItemSpacing,
		SnapPointsType SnapPointsType,
		SnapPointsAlignment SnapPointsAlignment)
	{
		public static ItemsLayoutSnapshot Capture(IItemsLayout layout)
		{
			ArgumentNullException.ThrowIfNull(layout);

			return layout switch
			{
				GridItemsLayout grid => new(
					grid.Orientation == ItemsLayoutOrientation.Horizontal,
					grid.Span,
					0,
					grid.VerticalItemSpacing,
					grid.HorizontalItemSpacing,
					grid.SnapPointsType,
					grid.SnapPointsAlignment),
				LinearItemsLayout linear => new(
					linear.Orientation == ItemsLayoutOrientation.Horizontal,
					1,
					linear.ItemSpacing,
					0,
					0,
					linear.SnapPointsType,
					linear.SnapPointsAlignment),
				_ => new(false, 1, 0, 0, 0, SnapPointsType.None, SnapPointsAlignment.Start),
			};
		}

		public int EffectiveSpan(bool forceSingleSpan) => forceSingleSpan ? 1 : Math.Max(1, Span);
	}

	internal static class SearchResultsLayout
	{
		public static bool IsVisible(
			string? query,
			int itemCount,
			bool searchEnabled,
			bool showsResults,
			bool searchBoxHidden) =>
			searchEnabled
				&& showsResults
				&& !searchBoxHidden
				&& !string.IsNullOrWhiteSpace(query)
				&& itemCount > 0;

		public static bool IsCollapsed(
			SearchBoxVisibility visibility,
			bool isFocused,
			string? query) =>
			visibility == SearchBoxVisibility.Collapsible
				&& !isFocused
				&& string.IsNullOrEmpty(query);

		public static bool ShouldFocusNative(bool requested, bool searchEnabled, bool searchBoxHidden) =>
			requested && searchEnabled && !searchBoxHidden;

		public static double ConstrainHeight(double measuredHeight, double screenHeight) =>
			Math.Min(Math.Max(0, measuredHeight), Math.Max(0, screenHeight) / 2);
	}

	internal static class LogicalItemsProjection
	{
		public static int Count(int physicalCount, bool isInternalPlaceholder) =>
			isInternalPlaceholder ? 0 : Math.Max(0, physicalCount);

		public static bool CanProject(int index, int logicalCount) =>
			index >= 0 && index < logicalCount;
	}

	internal static class CarouselPositionDecision
	{
		public static bool ShouldScroll(int targetIndex, int lastPosition) =>
			targetIndex != lastPosition;

		public static bool StartsScrolling(bool shouldScroll, bool animate) =>
			shouldScroll && animate;
	}
}
