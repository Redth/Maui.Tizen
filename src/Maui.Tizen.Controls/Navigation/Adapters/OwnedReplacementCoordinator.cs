using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>Replaces an owned resource in detach, unsubscribe, dispose, subscribe, attach order.</summary>
	internal sealed class OwnedReplacementCoordinator<T>
		where T : class
	{
		public T? Current { get; private set; }

		public void Replace(
			T? replacement,
			Action detachNative,
			Action<T> unsubscribe,
			Action<T> dispose,
			Action<T> subscribe,
			Action<T?> attachNative)
		{
			ArgumentNullException.ThrowIfNull(detachNative);
			ArgumentNullException.ThrowIfNull(unsubscribe);
			ArgumentNullException.ThrowIfNull(dispose);
			ArgumentNullException.ThrowIfNull(subscribe);
			ArgumentNullException.ThrowIfNull(attachNative);

			var outgoing = Current;
			List<Exception>? errors = null;

			Capture(detachNative, ref errors);
			if (outgoing is not null)
			{
				Capture(() => unsubscribe(outgoing), ref errors);
				if (!ReferenceEquals(outgoing, replacement))
					Capture(() => dispose(outgoing), ref errors);
			}

			Current = replacement;
			if (replacement is not null)
				Capture(() => subscribe(replacement), ref errors);
			Capture(() => attachNative(replacement), ref errors);

			if (errors is { Count: > 0 })
				throw new AggregateException(errors);
		}

		static void Capture(Action action, ref List<Exception>? errors)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				(errors ??= new()).Add(ex);
			}
		}
	}

	internal sealed class GenerationGuard
	{
		int _generation;

		public int Advance() => ++_generation;

		public int Capture() => _generation;

		public bool RunIfCurrent(int generation, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);
			if (generation != _generation)
				return false;

			action();
			return true;
		}
	}

	internal sealed class RealizedItemOwnership<TItem, TOwner>
		where TItem : class
		where TOwner : class
	{
		readonly Dictionary<TItem, TOwner> _owners = new();

		public void Track(TItem item, TOwner owner) => _owners[item] = owner;

		public void ReleaseRemoved(IEnumerable<TItem> liveItems, Action<TItem, TOwner> release)
		{
			ArgumentNullException.ThrowIfNull(liveItems);
			ArgumentNullException.ThrowIfNull(release);

			var live = liveItems.ToHashSet();
			ReleaseMany(_owners.Keys.Where(item => !live.Contains(item)).ToList(), release);
		}

		public void ReleaseAll(Action<TItem, TOwner> release)
		{
			ArgumentNullException.ThrowIfNull(release);
			ReleaseMany(_owners.Keys.ToList(), release);
		}

		void ReleaseMany(IReadOnlyList<TItem> items, Action<TItem, TOwner> release)
		{
			List<Exception>? errors = null;
			foreach (var item in items)
			{
				if (!_owners.Remove(item, out var owner))
					continue;

				try
				{
					release(item, owner);
				}
				catch (Exception ex)
				{
					(errors ??= new()).Add(ex);
				}
			}

			if (errors is { Count: > 0 })
				throw new AggregateException(errors);
		}
	}

	internal sealed class RealizedRowIndexMap<THolder, TView>
		where THolder : class
		where TView : class
	{
		readonly Dictionary<THolder, (int Index, TView View)> _holderRows =
			new(ReferenceEqualityComparer.Instance);
		readonly Dictionary<int, TView> _indexViews = new();
		readonly Dictionary<TView, int> _viewIndexes =
			new(ReferenceEqualityComparer.Instance);

		public void Bind(THolder holder, int index, TView view)
		{
			Unbind(holder);
			if (_indexViews.Remove(index, out var previousView))
				_viewIndexes.Remove(previousView);

			_holderRows[holder] = (index, view);
			_indexViews[index] = view;
			_viewIndexes[view] = index;
		}

		public bool Unbind(THolder holder)
		{
			if (!_holderRows.Remove(holder, out var row))
				return false;

			if (_indexViews.TryGetValue(row.Index, out var indexedView)
				&& ReferenceEquals(indexedView, row.View))
			{
				_indexViews.Remove(row.Index);
			}
			_viewIndexes.Remove(row.View);
			return true;
		}

		public TView? GetView(int index) =>
			_indexViews.TryGetValue(index, out var view) ? view : null;

		public bool TryGetIndex(TView view, out int index) => _viewIndexes.TryGetValue(view, out index);

		public void Apply(NotifyCollectionChangedEventArgs change)
		{
			ArgumentNullException.ThrowIfNull(change);

			switch (change.Action)
			{
				case NotifyCollectionChangedAction.Add when change.NewStartingIndex >= 0:
					ShiftIndexes(change.NewStartingIndex, change.NewItems?.Count ?? 0);
					break;
				case NotifyCollectionChangedAction.Remove when change.OldStartingIndex >= 0:
					RemoveIndexes(change.OldStartingIndex, change.OldItems?.Count ?? 0);
					break;
				case NotifyCollectionChangedAction.Replace
					when change.NewStartingIndex >= 0
						&& change.NewItems?.Count == 1
						&& change.OldItems?.Count == 1:
					InvalidateIndex(change.NewStartingIndex);
					break;
				default:
					InvalidateIndexes();
					break;
			}
		}

		public void Clear()
		{
			_holderRows.Clear();
			_indexViews.Clear();
			_viewIndexes.Clear();
		}

		void ShiftIndexes(int startIndex, int count)
		{
			if (count <= 0)
				return;

			foreach (var holder in _holderRows.Keys.ToList())
			{
				var row = _holderRows[holder];
				if (row.Index >= startIndex)
					_holderRows[holder] = (row.Index + count, row.View);
			}
			RebuildIndexes();
		}

		void RemoveIndexes(int startIndex, int count)
		{
			if (count <= 0)
				return;

			var endIndex = startIndex + count;
			foreach (var holder in _holderRows.Keys.ToList())
			{
				var row = _holderRows[holder];
				if (row.Index >= startIndex && row.Index < endIndex)
				{
					_indexViews.Remove(row.Index);
					_viewIndexes.Remove(row.View);
					_holderRows[holder] = (-1, row.View);
				}
				else if (row.Index >= endIndex)
				{
					_holderRows[holder] = (row.Index - count, row.View);
				}
			}
			RebuildIndexes();
		}

		void InvalidateIndex(int index)
		{
			if (_indexViews.Remove(index, out var view))
				_viewIndexes.Remove(view);
		}

		void InvalidateIndexes()
		{
			_indexViews.Clear();
			_viewIndexes.Clear();
		}

		void RebuildIndexes()
		{
			_indexViews.Clear();
			_viewIndexes.Clear();
			foreach (var row in _holderRows.Values)
			{
				if (row.Index < 0 || _viewIndexes.ContainsKey(row.View))
					continue;
				_indexViews[row.Index] = row.View;
				_viewIndexes[row.View] = row.Index;
			}
		}
	}
}
