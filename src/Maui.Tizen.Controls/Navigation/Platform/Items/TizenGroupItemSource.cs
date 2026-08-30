using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	internal interface ITizenIndexTransformSource
	{
		event NotifyCollectionChangedEventHandler? BeforeCollectionChanged;
	}

	/// <summary>
	/// Observable flattened view of a grouped ItemsSource, including configured group decorations.
	/// </summary>
	internal sealed class TizenGroupItemSource : IList, IReadOnlyList<object>, INotifyCollectionChanged, ITizenIndexTransformSource, IDisposable
	{
		readonly IEnumerable _source;
		readonly bool _hasGroupHeader;
		readonly bool _hasGroupFooter;
		readonly List<object> _groups = new();
		readonly List<object> _items = new();
		readonly Dictionary<object, GroupMarkers> _markers =
			new(ReferenceEqualityComparer.Instance);
		readonly List<INotifyCollectionChanged> _innerSubscriptions = new();
		INotifyCollectionChanged? _outerSubscription;
		bool _disposed;

		public TizenGroupItemSource(GroupableItemsView itemsView)
		{
			ArgumentNullException.ThrowIfNull(itemsView);

			_source = itemsView.ItemsSource ?? Array.Empty<object>();
			_hasGroupHeader = itemsView.GroupHeaderTemplate is not null;
			_hasGroupFooter = itemsView.GroupFooterTemplate is not null;
			RebuildSubscriptionsAndItems();
		}

		public event NotifyCollectionChangedEventHandler? CollectionChanged;

		public event NotifyCollectionChangedEventHandler? BeforeCollectionChanged;

		public int Count => _items.Count;

		public object this[int index] => _items[index];

		object? IList.this[int index]
		{
			get => _items[index];
			set => throw new NotSupportedException();
		}

		bool IList.IsFixedSize => true;

		bool IList.IsReadOnly => true;

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

		public bool IsGroupHeader(int index) => index >= 0 && index < Count && _items[index] is GroupHeaderItem;

		public bool IsGroupFooter(int index) => index >= 0 && index < Count && _items[index] is GroupFooterItem;

		public int GetAbsoluteIndex(int groupIndex, int itemIndex)
		{
			if (groupIndex < 0 || groupIndex >= _groups.Count)
				return -1;

			var count = ItemCount(_groups[groupIndex]);
			if (itemIndex < 0 || itemIndex >= count)
				return -1;

			return GroupStart(_groups, groupIndex) + (_hasGroupHeader ? 1 : 0) + itemIndex;
		}

		public int GetAbsoluteIndex(object? item)
		{
			for (var index = 0; index < _items.Count; index++)
			{
				if (_items[index] is GroupHeaderItem or GroupFooterItem)
					continue;

				if (ReferenceEquals(_items[index], item) || Equals(_items[index], item))
					return index;
			}

			return -1;
		}

		public int GetAbsoluteIndex(object? group, object? item)
		{
			var groupIndex = _groups.FindIndex(candidate =>
				ReferenceEquals(candidate, group) || Equals(candidate, group));
			if (groupIndex < 0 || _groups[groupIndex] is not IEnumerable groupItems)
				return -1;

			var itemIndex = 0;
			foreach (var candidate in groupItems)
			{
				if (ReferenceEquals(candidate, item) || Equals(candidate, item))
					return GetAbsoluteIndex(groupIndex, itemIndex);
				itemIndex++;
			}

			return -1;
		}

		public bool TryGetItem(int index, out object? item)
		{
			if (index < 0 || index >= Count || IsGroupHeader(index) || IsGroupFooter(index))
			{
				item = null;
				return false;
			}

			item = _items[index];
			return true;
		}

		public int GetGroupStartIndex(int itemIndex)
		{
			if (itemIndex < 0 || itemIndex >= _items.Count)
				return -1;

			for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
			{
				var start = GroupStart(_groups, groupIndex);
				var end = start + (_hasGroupHeader ? 1 : 0) + ItemCount(_groups[groupIndex])
					+ (_hasGroupFooter ? 1 : 0);
				if (itemIndex >= start && itemIndex < end)
					return start;
			}

			return -1;
		}

		public int GetLastIndex(int groupStartIndex)
		{
			if (groupStartIndex < 0 || groupStartIndex >= _items.Count)
				return -1;

			for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
			{
				if (GroupStart(_groups, groupIndex) == groupStartIndex)
					return groupStartIndex + (_hasGroupHeader ? 1 : 0)
						+ ItemCount(_groups[groupIndex]) + (_hasGroupFooter ? 1 : 0) - 1;
			}

			return -1;
		}

		public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		int IList.Add(object? value) => throw new NotSupportedException();

		void IList.Clear() => throw new NotSupportedException();

		bool IList.Contains(object? value) => ((IList)_items).Contains(value);

		int IList.IndexOf(object? value) => ((IList)_items).IndexOf(value);

		void IList.Insert(int index, object? value) => throw new NotSupportedException();

		void IList.Remove(object? value) => throw new NotSupportedException();

		void IList.RemoveAt(int index) => throw new NotSupportedException();

		void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

		void OnOuterCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (_disposed)
				return;

			var oldGroups = _groups.ToList();
			var oldItems = _items.ToList();

			RebuildSubscriptionsAndItems();
			if (_disposed)
				return;
			RaiseTranslatedOuter(e, oldGroups, oldItems);
		}

		void OnInnerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (_disposed)
				return;

			var groupIndex = _groups.FindIndex(group => ReferenceEquals(group, sender));
			if (groupIndex < 0)
			{
				RebuildItems();
				Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
				return;
			}

			var oldItems = _items.ToList();
			var oldStart = GroupStart(_groups, groupIndex) + (_hasGroupHeader ? 1 : 0);
			RebuildItems();
			if (_disposed)
				return;

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add when e.NewItems is not null && e.NewStartingIndex >= 0:
					Raise(
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Add,
							e.NewItems,
							oldStart + e.NewStartingIndex));
					break;
				case NotifyCollectionChangedAction.Remove when e.OldItems is not null && e.OldStartingIndex >= 0:
					Raise(
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Remove,
							e.OldItems,
							oldStart + e.OldStartingIndex));
					break;
				case NotifyCollectionChangedAction.Replace
					when e.NewItems is not null
						&& e.OldItems is not null
						&& e.NewStartingIndex >= 0
						&& e.OldStartingIndex >= 0
						&& e.NewItems.Count == e.OldItems.Count:
					if (e.NewItems.Count == 1)
					{
						Raise(
							new NotifyCollectionChangedEventArgs(
								NotifyCollectionChangedAction.Replace,
								e.NewItems,
								e.OldItems,
								oldStart + e.NewStartingIndex));
					}
					else
					{
						Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
					}
					break;
				case NotifyCollectionChangedAction.Move:
					Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
					break;
				default:
					Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
					break;
			}
		}

		void RaiseTranslatedOuter(
			NotifyCollectionChangedEventArgs e,
			IReadOnlyList<object> oldGroups,
			IReadOnlyList<object> oldItems)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add when e.NewItems is not null && e.NewStartingIndex >= 0:
					{
						var index = GroupStart(_groups, e.NewStartingIndex);
						var added = FlattenGroups(e.NewItems.Cast<object>()).ToList();
						Raise(
							new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, index));
						break;
					}
				case NotifyCollectionChangedAction.Remove when e.OldItems is not null && e.OldStartingIndex >= 0:
					{
						var index = GroupStart(oldGroups, e.OldStartingIndex);
						var removed = oldItems
							.Skip(index)
							.Take(e.OldItems.Cast<object>().Sum(FlattenedCount))
							.ToList();
						Raise(
							new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
						break;
					}
				case NotifyCollectionChangedAction.Move
					when e.OldItems is not null
						&& e.NewStartingIndex >= 0
						&& e.OldStartingIndex >= 0:
					{
						Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
						break;
					}
				case NotifyCollectionChangedAction.Replace
					when e.NewItems is not null
						&& e.OldItems is not null
						&& e.NewStartingIndex >= 0
						&& e.OldStartingIndex >= 0:
					{
						var oldIndex = GroupStart(oldGroups, e.OldStartingIndex);
						var newIndex = GroupStart(_groups, e.NewStartingIndex);
						var removed = oldItems
							.Skip(oldIndex)
							.Take(e.OldItems.Cast<object>().Sum(FlattenedCount))
							.ToList();
						var added = FlattenGroups(e.NewItems.Cast<object>()).ToList();

						if (oldIndex == newIndex && removed.Count == 1 && added.Count == 1)
						{
							Raise(
								new NotifyCollectionChangedEventArgs(
									NotifyCollectionChangedAction.Replace,
									added,
									removed,
									newIndex));
						}
						else
						{
							Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
						}
						break;
					}
				default:
					Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
					break;
			}
		}

		void RebuildSubscriptionsAndItems()
		{
			if (_disposed)
				return;

			if (_outerSubscription is not null)
				_outerSubscription.CollectionChanged -= OnOuterCollectionChanged;

			foreach (var subscription in _innerSubscriptions)
				subscription.CollectionChanged -= OnInnerCollectionChanged;
			_innerSubscriptions.Clear();

			_groups.Clear();
			_groups.AddRange(_source.Cast<object>());
			if (_disposed)
				return;
			foreach (var removed in _markers.Keys.Where(group => !_groups.Any(current => ReferenceEquals(current, group))).ToList())
				_markers.Remove(removed);

			_outerSubscription = _source as INotifyCollectionChanged;
			if (_outerSubscription is not null)
				_outerSubscription.CollectionChanged += OnOuterCollectionChanged;

			foreach (var group in _groups.OfType<INotifyCollectionChanged>())
			{
				if (_disposed)
					return;
				group.CollectionChanged += OnInnerCollectionChanged;
				_innerSubscriptions.Add(group);
			}

			RebuildItems();
		}

		void RebuildItems()
		{
			if (_disposed)
				return;

			var rebuilt = new List<object>();
			foreach (var group in _groups)
			{
				if (_disposed)
					return;
				rebuilt.AddRange(FlattenGroup(group));
			}

			if (_disposed)
				return;
			_items.Clear();
			_items.AddRange(rebuilt);
		}

		IEnumerable<object> FlattenGroups(IEnumerable<object> groups) =>
			groups.SelectMany(FlattenGroup);

		IEnumerable<object> FlattenGroup(object group)
		{
			if (!_markers.TryGetValue(group, out var markers))
			{
				markers = new GroupMarkers(new GroupHeaderItem(group), new GroupFooterItem(group));
				_markers[group] = markers;
			}

			if (_hasGroupHeader)
				yield return markers.Header;
			if (group is IEnumerable items)
			{
				foreach (var item in items)
					yield return item!;
			}
			if (_hasGroupFooter)
				yield return markers.Footer;
		}

		int FlattenedCount(object group) =>
			(_hasGroupHeader ? 1 : 0) + ItemCount(group) + (_hasGroupFooter ? 1 : 0);

		void Raise(NotifyCollectionChangedEventArgs args)
		{
			var handlers = CollectionChanged;
			RaiseHandlers(BeforeCollectionChanged, args);
			if (_disposed)
				return;
			RaiseHandlers(handlers, args);
		}

		void RaiseHandlers(NotifyCollectionChangedEventHandler? handlers, NotifyCollectionChangedEventArgs args)
		{
			if (_disposed || handlers is null)
				return;
			foreach (NotifyCollectionChangedEventHandler handler in handlers.GetInvocationList())
			{
				if (_disposed)
					break;
				handler(this, args);
			}
		}

		int GroupStart(IReadOnlyList<object> groups, int groupIndex)
		{
			var start = 0;
			for (var index = 0; index < groupIndex && index < groups.Count; index++)
				start += (_hasGroupHeader ? 1 : 0) + ItemCount(groups[index]) + (_hasGroupFooter ? 1 : 0);
			return start;
		}

		static int ItemCount(object group) =>
			group is ICollection collection
				? collection.Count
				: group is IEnumerable enumerable
					? enumerable.Cast<object>().Count()
					: 0;

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			if (_outerSubscription is not null)
				_outerSubscription.CollectionChanged -= OnOuterCollectionChanged;
			foreach (var subscription in _innerSubscriptions)
				subscription.CollectionChanged -= OnInnerCollectionChanged;
			_innerSubscriptions.Clear();
			BeforeCollectionChanged = null;
			CollectionChanged = null;
		}

		readonly record struct GroupMarkers(GroupHeaderItem Header, GroupFooterItem Footer);

		internal sealed class GroupHeaderItem
		{
			public GroupHeaderItem(object data) => Data = data;
			public object Data { get; }
		}

		internal sealed class GroupFooterItem
		{
			public GroupFooterItem(object data) => Data = data;
			public object Data { get; }
		}
	}

	internal sealed class TizenObservableItemSource : IList, INotifyCollectionChanged, ITizenIndexTransformSource, IDisposable
	{
		readonly IEnumerable _source;
		IList _items;
		INotifyCollectionChanged? _observable;
		bool _disposed;

		public TizenObservableItemSource(IEnumerable? source)
		{
			_source = source ?? Array.Empty<object>();
			_items = _source as IList ?? _source.Cast<object>().ToList();
			_observable = _source as INotifyCollectionChanged;
			if (_observable is not null)
				_observable.CollectionChanged += OnCollectionChanged;
		}

		public event NotifyCollectionChangedEventHandler? CollectionChanged;

		public event NotifyCollectionChangedEventHandler? BeforeCollectionChanged;

		public int Count => _items.Count;
		public object? this[int index]
		{
			get => _items[index];
			set => throw new NotSupportedException();
		}
		public bool IsFixedSize => true;
		public bool IsReadOnly => true;
		public bool IsSynchronized => false;
		public object SyncRoot => _items.SyncRoot;
		public int Add(object? value) => throw new NotSupportedException();
		public void Clear() => throw new NotSupportedException();
		public bool Contains(object? value) => _items.Contains(value);
		public int IndexOf(object? value) => _items.IndexOf(value);
		public void Insert(int index, object? value) => throw new NotSupportedException();
		public void Remove(object? value) => throw new NotSupportedException();
		public void RemoveAt(int index) => throw new NotSupportedException();
		public void CopyTo(Array array, int index) => _items.CopyTo(array, index);
		public IEnumerator GetEnumerator() => _items.GetEnumerator();

		void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (_disposed)
				return;

			if (_source is not IList)
				_items = _source.Cast<object>().ToList();

			var normalized = e.Action switch
			{
				NotifyCollectionChangedAction.Move =>
					new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
				NotifyCollectionChangedAction.Add when e.NewStartingIndex < 0 =>
					new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
				NotifyCollectionChangedAction.Remove when e.OldStartingIndex < 0 =>
					new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
				NotifyCollectionChangedAction.Replace
					when e.NewStartingIndex < 0
						|| e.OldStartingIndex < 0
						|| e.NewItems?.Count != 1
						|| e.OldItems?.Count != 1 =>
					new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
				_ => e,
			};

			var handlers = CollectionChanged;
			RaiseHandlers(BeforeCollectionChanged, normalized);
			if (_disposed)
				return;
			RaiseHandlers(handlers, normalized);
		}

		void RaiseHandlers(NotifyCollectionChangedEventHandler? handlers, NotifyCollectionChangedEventArgs args)
		{
			if (_disposed || handlers is null)
				return;
			foreach (NotifyCollectionChangedEventHandler handler in handlers.GetInvocationList())
			{
				if (_disposed)
					break;
				handler(this, args);
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			if (_observable is not null)
				_observable.CollectionChanged -= OnCollectionChanged;
			_observable = null;
			BeforeCollectionChanged = null;
			CollectionChanged = null;
		}
	}
}
