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

		public void ApplyManaged(int? expectedPosition, Action update)
		{
			_pendingManagedPosition = expectedPosition;
			_gate.ApplyManaged(update);
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
		int? _position;
		object? _currentItem;
		bool _hasCurrentItem;

		public void SetPosition(int position)
		{
			_hasCurrentItem = false;
			_currentItem = null;
			_position = position;
		}

		public void SetCurrentItem(object? currentItem)
		{
			_currentItem = currentItem;
			_hasCurrentItem = currentItem is not null;
			if (_hasCurrentItem)
				_position = null;
		}

		public bool TryApply(
			bool hasLayout,
			int itemCount,
			Func<object?, int> getItemIndex,
			Action<int> scrollTo)
		{
			ArgumentNullException.ThrowIfNull(getItemIndex);
			ArgumentNullException.ThrowIfNull(scrollTo);

			if (!hasLayout)
				return false;

			var applied = false;
			if (_position is int position)
			{
				if (position >= 0 && position < itemCount)
				{
					scrollTo(position);
					applied = true;
				}
				_position = null;
			}

			if (_hasCurrentItem)
			{
				var index = getItemIndex(_currentItem);
				if (index >= 0 && index < itemCount)
				{
					scrollTo(index);
					_hasCurrentItem = false;
					_currentItem = null;
					applied = true;
				}
			}

			return applied;
		}
	}

	internal static class SearchResultsLayout
	{
		public static bool IsVisible(string? query, int itemCount, bool searchBoxHidden) =>
			!searchBoxHidden && !string.IsNullOrWhiteSpace(query) && itemCount > 0;

		public static double ConstrainHeight(double measuredHeight, double screenHeight) =>
			Math.Min(Math.Max(0, measuredHeight), Math.Max(0, screenHeight) / 2);
	}
}
