using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Observable flattened view of a grouped ItemsSource, including group header/footer rows.
	/// </summary>
	internal sealed class TizenGroupItemSource : IReadOnlyList<object>, INotifyCollectionChanged, IDisposable
	{
		readonly IEnumerable _source;
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
			_hasGroupFooter = itemsView.GroupFooterTemplate is not null;
			RebuildSubscriptionsAndItems();
		}

		public event NotifyCollectionChangedEventHandler? CollectionChanged;

		public int Count => _items.Count;

		public object this[int index] => _items[index];

		public bool IsGroupHeader(int index) => index >= 0 && index < Count && _items[index] is GroupHeaderItem;

		public bool IsGroupFooter(int index) => index >= 0 && index < Count && _items[index] is GroupFooterItem;

		public int GetAbsoluteIndex(int groupIndex, int itemIndex)
		{
			if (groupIndex < 0 || groupIndex >= _groups.Count)
				return -1;

			var count = ItemCount(_groups[groupIndex]);
			if (itemIndex < 0 || itemIndex >= count)
				return -1;

			return GroupStart(_groups, groupIndex) + 1 + itemIndex;
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

			for (var index = itemIndex; index >= 0; index--)
			{
				if (_items[index] is GroupHeaderItem)
					return index;
			}

			return -1;
		}

		public int GetLastIndex(int groupStartIndex)
		{
			if (groupStartIndex < 0 || groupStartIndex >= _items.Count
				|| _items[groupStartIndex] is not GroupHeaderItem header)
				return -1;

			var groupIndex = _groups.FindIndex(group => ReferenceEquals(group, header.Data));
			return groupIndex < 0
				? -1
				: groupStartIndex + ItemCount(_groups[groupIndex]) + (_hasGroupFooter ? 1 : 0);
		}

		public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		void OnOuterCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			var oldGroups = _groups.ToList();
			var oldItems = _items.ToList();

			RebuildSubscriptionsAndItems();
			RaiseTranslatedOuter(e, oldGroups, oldItems);
		}

		void OnInnerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			var groupIndex = _groups.FindIndex(group => ReferenceEquals(group, sender));
			if (groupIndex < 0)
			{
				RebuildItems();
				CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
				return;
			}

			var oldItems = _items.ToList();
			var oldStart = GroupStart(_groups, groupIndex) + 1;
			RebuildItems();

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add when e.NewItems is not null:
					CollectionChanged?.Invoke(
						this,
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Add,
							e.NewItems,
							oldStart + e.NewStartingIndex));
					break;
				case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
					CollectionChanged?.Invoke(
						this,
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Remove,
							e.OldItems,
							oldStart + e.OldStartingIndex));
					break;
				case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
					CollectionChanged?.Invoke(
						this,
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Replace,
							e.NewItems,
							e.OldItems,
							oldStart + e.NewStartingIndex));
					break;
				case NotifyCollectionChangedAction.Move when e.NewItems is not null:
					CollectionChanged?.Invoke(
						this,
						new NotifyCollectionChangedEventArgs(
							NotifyCollectionChangedAction.Move,
							e.NewItems,
							oldStart + e.NewStartingIndex,
							oldStart + e.OldStartingIndex));
					break;
				default:
					CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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
				case NotifyCollectionChangedAction.Add when e.NewItems is not null:
					{
						var index = GroupStart(_groups, e.NewStartingIndex);
						var added = FlattenGroups(e.NewItems.Cast<object>()).ToList();
						CollectionChanged?.Invoke(
							this,
							new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, index));
						break;
					}
				case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
					{
						var index = GroupStart(oldGroups, e.OldStartingIndex);
						var removed = FlattenGroups(e.OldItems.Cast<object>()).ToList();
						CollectionChanged?.Invoke(
							this,
							new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
						break;
					}
				case NotifyCollectionChangedAction.Move when e.OldItems is not null:
					{
						var oldIndex = GroupStart(oldGroups, e.OldStartingIndex);
						var newIndex = GroupStart(_groups, e.NewStartingIndex);
						var moved = FlattenGroups(e.OldItems.Cast<object>()).ToList();
						CollectionChanged?.Invoke(
							this,
							new NotifyCollectionChangedEventArgs(
								NotifyCollectionChangedAction.Move,
								moved,
								newIndex,
								oldIndex));
						break;
					}
				case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
					{
						var oldIndex = GroupStart(oldGroups, e.OldStartingIndex);
						var newIndex = GroupStart(_groups, e.NewStartingIndex);
						var removed = FlattenGroups(e.OldItems.Cast<object>()).ToList();
						var added = FlattenGroups(e.NewItems.Cast<object>()).ToList();

						if (oldIndex == newIndex && removed.Count == added.Count)
						{
							CollectionChanged?.Invoke(
								this,
								new NotifyCollectionChangedEventArgs(
									NotifyCollectionChangedAction.Replace,
									added,
									removed,
									newIndex));
						}
						else
						{
							CollectionChanged?.Invoke(
								this,
								new NotifyCollectionChangedEventArgs(
									NotifyCollectionChangedAction.Remove,
									removed,
									oldIndex));
							CollectionChanged?.Invoke(
								this,
								new NotifyCollectionChangedEventArgs(
									NotifyCollectionChangedAction.Add,
									added,
									newIndex));
						}
						break;
					}
				default:
					CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
					break;
			}
		}

		void RebuildSubscriptionsAndItems()
		{
			if (_outerSubscription is not null)
				_outerSubscription.CollectionChanged -= OnOuterCollectionChanged;

			foreach (var subscription in _innerSubscriptions)
				subscription.CollectionChanged -= OnInnerCollectionChanged;
			_innerSubscriptions.Clear();

			_groups.Clear();
			_groups.AddRange(_source.Cast<object>());
			foreach (var removed in _markers.Keys.Where(group => !_groups.Any(current => ReferenceEquals(current, group))).ToList())
				_markers.Remove(removed);

			_outerSubscription = _source as INotifyCollectionChanged;
			if (_outerSubscription is not null)
				_outerSubscription.CollectionChanged += OnOuterCollectionChanged;

			foreach (var group in _groups.OfType<INotifyCollectionChanged>())
			{
				group.CollectionChanged += OnInnerCollectionChanged;
				_innerSubscriptions.Add(group);
			}

			RebuildItems();
		}

		void RebuildItems()
		{
			_items.Clear();
			foreach (var group in _groups)
				_items.AddRange(FlattenGroup(group));
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

			yield return markers.Header;
			if (group is IEnumerable items)
			{
				foreach (var item in items)
					yield return item!;
			}
			if (_hasGroupFooter)
				yield return markers.Footer;
		}

		int GroupStart(IReadOnlyList<object> groups, int groupIndex)
		{
			var start = 0;
			for (var index = 0; index < groupIndex && index < groups.Count; index++)
				start += 1 + ItemCount(groups[index]) + (_hasGroupFooter ? 1 : 0);
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
}
