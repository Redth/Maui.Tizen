// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal enum TizenSwipeItemsSlot
	{
		Left,
		Top,
		Right,
		Bottom,
	}

	internal sealed class TizenSwipeItemsSnapshot
	{
		readonly ISwipeItem[] _items;
		readonly SwipeMode _mode;
		readonly SwipeBehaviorOnInvoked _behavior;

		TizenSwipeItemsSnapshot(ISwipeItems? items)
		{
			_items = items?.ToArray() ?? [];
			_mode = items?.Mode ?? default;
			_behavior = items?.SwipeBehaviorOnInvoked ?? default;
		}

		public static TizenSwipeItemsSnapshot Capture(ISwipeItems? items) => new(items);

		public bool Matches(ISwipeItems? items)
		{
			if ((items?.Mode ?? default) != _mode
				|| (items?.SwipeBehaviorOnInvoked ?? default) != _behavior
				|| (items?.Count ?? 0) != _items.Length)
				return false;

			var current = items?.ToArray() ?? [];
			return _items.SequenceEqual(current, ReferenceEqualityComparer.Instance);
		}
	}

	internal static class TizenSwipeStructureCoordinator
	{
		public static SwipeDirection? Invalidate(
			bool wasOpen,
			SwipeDirection? previousDirection,
			ref bool isOpen,
			ref SwipeDirection? direction,
			ref double offset,
			ref double threshold,
			Func<SwipeDirection, bool> isSideValid)
		{
			ArgumentNullException.ThrowIfNull(isSideValid);

			isOpen = false;
			direction = null;
			offset = 0;
			threshold = 0;

			return wasOpen && previousDirection is { } candidate && isSideValid(candidate)
				? candidate
				: null;
		}
	}

	internal sealed class TizenSwipeItemRegistry<TItem, TView>
		where TItem : class
		where TView : class
	{
		readonly Dictionary<TItem, TView> _items = new();
		long _generation;

		public long CurrentGeneration => System.Threading.Volatile.Read(ref _generation);

		public void Add(TItem item, TView view) => _items.Add(item, view);

		public bool TryGetValue(TItem item, out TView? view) => _items.TryGetValue(item, out view);

		public bool IsCurrent(long generation, TItem item, TView view) =>
			CurrentGeneration == generation &&
			_items.TryGetValue(item, out var current) &&
			ReferenceEquals(current, view);

		public IReadOnlyList<KeyValuePair<TItem, TView>> Drain()
		{
			System.Threading.Interlocked.Increment(ref _generation);
			var snapshot = _items.ToArray();
			_items.Clear();
			return snapshot;
		}
	}
}
