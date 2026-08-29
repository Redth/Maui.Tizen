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
}
