using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Wraps a grouped items source and flattens it for Tizen's CollectionView.
	/// </summary>
	/// <remarks>
	/// Provides indexing that maps a flat index to either a group header, item, or group footer.
	/// </remarks>
	public class TizenGroupItemSource : IReadOnlyList<object>
	{
		readonly IEnumerable _groupItems;
		readonly List<object> _items = new();
		readonly Dictionary<object, GroupInfo> _groupHeader = new();
		readonly bool _hasGroupFooter;

		public TizenGroupItemSource(GroupableItemsView itemsView)
		{
			_groupItems = itemsView.ItemsSource ?? System.Array.Empty<object>();
			_hasGroupFooter = itemsView.GroupFooterTemplate != null;
			UpdateItemsSource();
		}

		public int Count => _items.Count;

		public object this[int index] => _items[index];

		public bool IsGroupHeader(int index) => _items[index] is GroupHeaderItem;

		public bool IsGroupFooter(int index) => _items[index] is GroupFooterItem;

		public int GetGroupStartIndex(int itemIndex)
		{
			if (itemIndex < 0 || itemIndex >= _items.Count)
				return -1;

			object? item = _items[itemIndex];
			if (item is GroupHeaderItem header)
				return _groupHeader[header.Data].GroupHeaderIndex;
			if (item is GroupFooterItem footer)
				return _groupHeader[footer.Data].GroupHeaderIndex;

			// Find the data's group header
			for (int i = itemIndex; i >= 0; i--)
			{
				if (_items[i] is GroupHeaderItem h)
					return i;
			}
			return -1;
		}

		public int GetLastIndex(int groupStartIndex)
		{
			if (groupStartIndex < 0 || groupStartIndex >= _items.Count)
				return -1;

			if (_items[groupStartIndex] is GroupHeaderItem header && _groupHeader.TryGetValue(header.Data, out var info))
			{
				return info.GroupHeaderIndex + info.ItemCount + (_hasGroupFooter ? 1 : 0);
			}
			return groupStartIndex;
		}

		public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		void UpdateItemsSource()
		{
			_items.Clear();
			_groupHeader.Clear();

			foreach (var group in _groupItems)
			{
				int groupStartIndex = _items.Count;
				int itemCount = 0;

				// Add group header
				_items.Add(new GroupHeaderItem(group));

				// Add group items
				if (group is IEnumerable itemsInGroup)
				{
					foreach (var item in itemsInGroup)
					{
						_items.Add(item);
						itemCount++;
					}
				}

				// Add group footer if template exists
				if (_hasGroupFooter)
				{
					_items.Add(new GroupFooterItem(group));
				}

				_groupHeader[group] = new GroupInfo(groupStartIndex, itemCount);
			}
		}

		readonly struct GroupInfo
		{
			public readonly int GroupHeaderIndex;
			public readonly int ItemCount;

			public GroupInfo(int headerIndex, int itemCount)
			{
				GroupHeaderIndex = headerIndex;
				ItemCount = itemCount;
			}
		}

		/// <summary>
		/// Marker class for group header entries.
		/// </summary>
		public sealed class GroupHeaderItem
		{
			public object Data { get; }
			public GroupHeaderItem(object data) => Data = data;
		}

		/// <summary>
		/// Marker class for group footer entries.
		/// </summary>
		public sealed class GroupFooterItem
		{
			public object Data { get; }
			public GroupFooterItem(object data) => Data = data;
		}
	}
}
