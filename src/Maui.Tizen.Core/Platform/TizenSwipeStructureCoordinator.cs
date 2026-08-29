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

		public IReadOnlyList<ISwipeItem> Items => _items;

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
		public static IReadOnlyList<(TItem Item, TView View)> PairVisible<TItem, TView>(
			IEnumerable<TItem> items,
			Func<TItem, bool> isItemVisible,
			IEnumerable<TView> views,
			Func<TView, bool> isViewVisible)
		{
			ArgumentNullException.ThrowIfNull(items);
			ArgumentNullException.ThrowIfNull(isItemVisible);
			ArgumentNullException.ThrowIfNull(views);
			ArgumentNullException.ThrowIfNull(isViewVisible);

			return items
				.Where(isItemVisible)
				.Zip(views.Where(isViewVisible), static (item, view) => (item, view))
				.ToArray();
		}

		public static SwipeDirection? Invalidate(
			bool wasOpen,
			SwipeDirection? previousDirection,
			ref bool isOpen,
			ref SwipeDirection? direction,
			ref double offset,
			ref double threshold,
			Action restorePosition,
			Func<SwipeDirection, bool> isSideValid)
		{
			ArgumentNullException.ThrowIfNull(restorePosition);
			ArgumentNullException.ThrowIfNull(isSideValid);

			if (wasOpen)
				restorePosition();

			isOpen = false;
			direction = null;
			offset = 0;
			threshold = 0;

			return wasOpen && previousDirection is { } candidate && isSideValid(candidate)
				? candidate
				: null;
		}

		public static bool DisableGesture(
			ref bool isSwiping,
			ref bool isResetting,
			ref bool isOpen,
			ref SwipeDirection? direction,
			ref double offset,
			ref double threshold,
			Action cancelAnimation,
			Action restorePosition)
		{
			var wasActive =
				isSwiping ||
				isResetting ||
				isOpen ||
				direction is not null ||
				Math.Abs(offset) > double.Epsilon;

			if (!wasActive)
				return false;

			try
			{
				TizenCleanup.Run(cancelAnimation, restorePosition);
			}
			finally
			{
				isSwiping = false;
				isResetting = false;
				isOpen = false;
				direction = null;
				offset = 0;
				threshold = 0;
			}

			return true;
		}
	}

	internal sealed class TizenSwipeItemRegistry<TItem, TView>
		where TItem : class
		where TView : class
	{
		readonly Dictionary<TItem, Entry> _items = new();
		long _generation;

		sealed record Entry(TView View, object? Handler, object? Owner);

		public long CurrentGeneration => System.Threading.Volatile.Read(ref _generation);

		public long ReserveMaterialization() =>
			System.Threading.Interlocked.Increment(ref _generation);

		public void Add(TItem item, TView view) => _items.Add(item, new(view, null, null));

		public bool CommitPrepared(
			long operation,
			TItem item,
			TView view,
			object? handler,
			object owner,
			Func<bool> isExpected,
			Action disposePrepared)
		{
			ArgumentNullException.ThrowIfNull(owner);
			ArgumentNullException.ThrowIfNull(isExpected);
			ArgumentNullException.ThrowIfNull(disposePrepared);

			if (CurrentGeneration != operation || !isExpected())
			{
				if (!Owns(item, view, handler))
					disposePrepared();
				return false;
			}

			_items.Add(item, new(view, handler, owner));
			return true;
		}

		public void MaterializeFrozen(
			long operation,
			IEnumerable<TItem> items,
			object owner,
			Func<TItem, TView?> materialize,
			Func<TItem, object?> getHandler,
			Func<bool> isExpected,
			Action<TItem, TView> adopt,
			Action<TItem, TView, object?> disposePrepared)
		{
			ArgumentNullException.ThrowIfNull(items);
			ArgumentNullException.ThrowIfNull(owner);
			ArgumentNullException.ThrowIfNull(materialize);
			ArgumentNullException.ThrowIfNull(getHandler);
			ArgumentNullException.ThrowIfNull(isExpected);
			ArgumentNullException.ThrowIfNull(adopt);
			ArgumentNullException.ThrowIfNull(disposePrepared);

			foreach (var item in items)
			{
				var view = materialize(item);
				if (view is null)
					continue;

				var handler = getHandler(item);
				if (!CommitPrepared(
					operation,
					item,
					view,
					handler,
					owner,
					isExpected,
					() => disposePrepared(item, view, handler)))
					continue;

				adopt(item, view);
			}
		}

		public bool IsOperationCurrent(long operation) => CurrentGeneration == operation;

		public bool TryGetValue(TItem item, out TView? view)
		{
			if (_items.TryGetValue(item, out var entry))
			{
				view = entry.View;
				return true;
			}

			view = null;
			return false;
		}

		public bool Owns(TItem item, TView view, object? handler) =>
			_items.TryGetValue(item, out var current) &&
			ReferenceEquals(current.View, view) &&
			ReferenceEquals(current.Handler, handler) &&
			current.Owner is not null;

		public bool IsCurrent(long generation, TItem item, TView view) =>
			CurrentGeneration == generation &&
			_items.TryGetValue(item, out var current) &&
			ReferenceEquals(current.View, view);

		public IReadOnlyList<KeyValuePair<TItem, TView>> Drain()
		{
			System.Threading.Interlocked.Increment(ref _generation);
			var snapshot = _items
				.Select(static pair => new KeyValuePair<TItem, TView>(pair.Key, pair.Value.View))
				.ToArray();
			_items.Clear();
			return snapshot;
		}
	}

	internal enum TizenSwipeOpenDecision
	{
		Open,
		AlreadyOpen,
		ResetThenOpen,
		Queued,
	}

	internal readonly record struct TizenQueuedSwipeOpen(OpenSwipeItem Item, bool Animated);

	internal sealed class TizenSwipeOpenCoordinator
	{
		bool _closing;
		TizenQueuedSwipeOpen? _queued;

		public bool BeginAnimatedClose()
		{
			if (_closing)
				return false;

			_closing = true;
			return true;
		}

		public TizenSwipeOpenDecision RequestOpen(
			bool isOpen,
			OpenSwipeItem previous,
			OpenSwipeItem requested,
			bool animated)
		{
			if (_closing)
			{
				_queued = new TizenQueuedSwipeOpen(requested, animated);
				return TizenSwipeOpenDecision.Queued;
			}

			return TizenSwipeMetrics.GetProgrammaticOpenAction(isOpen, previous, requested) switch
			{
				TizenSwipeOpenAction.AlreadyOpen => TizenSwipeOpenDecision.AlreadyOpen,
				TizenSwipeOpenAction.ResetThenOpen => TizenSwipeOpenDecision.ResetThenOpen,
				_ => TizenSwipeOpenDecision.Open,
			};
		}

		public TizenQueuedSwipeOpen? CompleteClose()
		{
			_closing = false;
			var queued = _queued;
			_queued = null;
			return queued;
		}

		public void Reset()
		{
			_closing = false;
			_queued = null;
		}
	}
}
