using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;

using NView = Tizen.NUI.BaseComponents.View;
using TSize = Tizen.UIExtensions.Common.Size;
using XLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts grouped items to Tizen's CollectionView with support for group headers and footers.
	/// </summary>
	internal class TizenGroupItemTemplateAdaptor : ItemAdaptor, ITizenItemTemplateAdaptor, ITizenSelectableItemFilter
	{
		static readonly object HeaderCategory = new();
		static readonly object FooterCategory = new();

		readonly Dictionary<NView, View> _nativeMauiTable =
			new(ReferenceEqualityComparer.Instance);
		readonly RealizedRowIndexMap<NView, View> _realizedRows = new();
		readonly GroupableItemsView _itemsView;
		readonly TizenGroupItemSource _groupItemSource;
		readonly TizenHeaderFooterPresenter _headerFooter;

		public TizenGroupItemTemplateAdaptor(GroupableItemsView itemsView)
			: this(itemsView, new TizenGroupItemSource(itemsView))
		{
		}

		TizenGroupItemTemplateAdaptor(GroupableItemsView itemsView, TizenGroupItemSource groupItemSource)
			: base(groupItemSource)
		{
			_itemsView = itemsView;
			_groupItemSource = groupItemSource;
			_headerFooter = new TizenHeaderFooterPresenter(
				itemsView,
				() => MauiContext,
				() => CollectionView?.ItemMeasureInvalidated(-1));
			_groupItemSource.CollectionChanged += OnGroupItemsChanged;
		}

		/// <summary>
		/// Raised when the user changes selection from the UI.
		/// </summary>
		public event EventHandler<TizenCollectionViewSelectionChangedEventArgs>? SelectionChanged;

		public event EventHandler? ItemsChanged;

		public event EventHandler? ItemMeasureInvalidated;

		protected IMauiContext MauiContext => _itemsView.Handler!.MauiContext!;

		protected DataTemplate? ItemTemplate => _itemsView.ItemTemplate;

		protected DataTemplate? GroupHeaderTemplate => _itemsView.GroupHeaderTemplate;

		protected DataTemplate? GroupFooterTemplate => _itemsView.GroupFooterTemplate;

		protected virtual bool IsSelectable => true;

		public override void SendItemSelected(IEnumerable<int> selected)
		{
			var indexes = selected.Where(index => index >= 0 && index < Count).ToList();
			var items = new List<object>();
			foreach (var idx in indexes)
			{
				if (idx < 0 || Count <= idx)
					continue;

				// Don't include headers/footers in selection
				if (_groupItemSource.IsGroupHeader(idx) || _groupItemSource.IsGroupFooter(idx))
					continue;

				var selectedObject = this[idx];
				if (selectedObject != null)
					items.Add(selectedObject);
			}

			SelectionChanged?.Invoke(this, new TizenCollectionViewSelectionChangedEventArgs
			{
				SelectedItems = items,
				SelectedIndexes = indexes,
			});
		}

		public int GetAbsoluteIndex(int groupIndex, int itemIndex) =>
			_groupItemSource.GetAbsoluteIndex(groupIndex, itemIndex);

		public int GetAbsoluteIndex(object? item) => _groupItemSource.GetAbsoluteIndex(item);

		public int GetAbsoluteIndex(object? group, object? item) =>
			_groupItemSource.GetAbsoluteIndex(group, item);

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (_nativeMauiTable.TryGetValue(view, out View? formsView))
			{
				switch (state)
				{
					case ViewHolderState.Focused:
						ItemSelectionState.SetItemFocused(formsView, true);
						break;
					case ViewHolderState.Normal:
						// Reset clears selection, pointer-over and focus together and recomputes once,
						// so a recycled row cannot come back still carrying a previous item's state.
						ItemSelectionState.Reset(formsView);
						break;
					case ViewHolderState.Selected:
						if (IsSelectable)
						{
							// Selected means selected and NOT focused. Clearing the stored focus in
							// the same call keeps it to one recompute, so the row cannot flash as
							// Focused on the way.
							ItemSelectionState.SetItemSelectedAndUnfocused(formsView, true);
						}
						else
						{
							// Not selectable, but the row may still have been re-enabled since it was
							// last painted. MAUI's own recompute applies Normal on re-enable with no
							// knowledge of selection (MAUI-TIZEN-API-0010), so this is the point that
							// puts the stored state back.
							ItemSelectionState.Refresh(formsView);
						}
						break;
				}
			}
		}

		public View? GetTemplatedView(NView view)
		{
			return _nativeMauiTable.TryGetValue(view, out var formsView) ? formsView : null;
		}

		public View? GetTemplatedView(int index)
		{
			return index >= 0 && index < Count ? _realizedRows.GetView(index) : null;
		}

		public override object GetViewCategory(int index)
		{
			if (_groupItemSource.IsGroupHeader(index))
				return ResolveGroupTemplate(GroupHeaderTemplate, GroupData(index)) ?? HeaderCategory;
			if (_groupItemSource.IsGroupFooter(index))
				return ResolveGroupTemplate(GroupFooterTemplate, GroupData(index)) ?? FooterCategory;

			if (ItemTemplate is DataTemplateSelector selector)
			{
				return selector.SelectTemplate(this[index], _itemsView);
			}
			return base.GetViewCategory(index);
		}

		/// <summary>
		/// Determines if the item at the specified index is selectable.
		/// Headers and footers are not selectable.
		/// </summary>
		public bool IsItemSelectableAt(int index)
		{
			// Headers and footers are not selectable
			return !_groupItemSource.IsGroupHeader(index) && !_groupItemSource.IsGroupFooter(index);
		}

		/// <summary>
		/// Gets the header view. Not used for grouped items (uses per-group headers instead).
		/// </summary>
		public override NView? GetHeaderView() => _headerFooter.GetHeaderView();

		/// <summary>
		/// Gets the footer view. Not used for grouped items (uses per-group footers instead).
		/// </summary>
		public override NView? GetFooterView() => _headerFooter.GetFooterView();

		/// <summary>
		/// Measures the header. Returns zero size since headers are per-group, not top-level.
		/// </summary>
		public override TSize MeasureHeader(double widthConstraint, double heightConstraint) =>
			_headerFooter.MeasureHeader(widthConstraint, heightConstraint);

		/// <summary>
		/// Measures the footer. Returns zero size since footers are per-group, not top-level.
		/// </summary>
		public override TSize MeasureFooter(double widthConstraint, double heightConstraint) =>
			_headerFooter.MeasureFooter(widthConstraint, heightConstraint);

		public override NView CreateNativeView(int index)
		{
			View view;
			if (_groupItemSource.IsGroupHeader(index))
			{
				view = CreateGroupHeaderView(index);
			}
			else if (_groupItemSource.IsGroupFooter(index))
			{
				view = CreateGroupFooterView(index);
			}
			else
			{
				view = CreateItemView(index);
			}

			var native = view.ToPlatformView(MauiContext);
			_nativeMauiTable[native] = view;

			// IsEnabled is an input to the selection precedence chain that the app can change at any
			// time, so it has to be observed rather than sampled once.
			ItemSelectionState.TrackEnabledState(view);

			return native;
		}

		public override NView CreateNativeView()
		{
			return CreateNativeView(0);
		}

		public override void RemoveNativeView(NView native)
		{
			UnBinding(native);
			if (_nativeMauiTable.Remove(native, out View? view))
			{
				ItemSelectionState.UntrackEnabledState(view);
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
			}
		}

		public override void SetBinding(NView native, int index)
		{
			if (_nativeMauiTable.TryGetValue(native, out View? view))
			{
				ResetBindedView(native, view);

				object? bindingContext;
				if (_groupItemSource.IsGroupHeader(index) && this[index] is TizenGroupItemSource.GroupHeaderItem headerItem)
				{
					bindingContext = headerItem.Data;
				}
				else if (_groupItemSource.IsGroupFooter(index) && this[index] is TizenGroupItemSource.GroupFooterItem footerItem)
				{
					bindingContext = footerItem.Data;
				}
				else
				{
					bindingContext = this[index];
				}

				if (bindingContext != null)
					view.BindingContext = bindingContext;
				_realizedRows.Bind(native, index, view);
				view.MeasureInvalidated += OnItemMeasureInvalidated;
				view.Parent = _itemsView;
				_itemsView.AddLogicalChild(view);
			}
		}

		public override void UnBinding(NView native)
		{
			if (_nativeMauiTable.TryGetValue(native, out View? view))
			{
				view.MeasureInvalidated -= OnItemMeasureInvalidated;
				ResetBindedView(native, view);
			}
		}

		public override TSize MeasureItem(double widthConstraint, double heightConstraint)
		{
			return MeasureItem(0, widthConstraint, heightConstraint);
		}

		public override TSize MeasureItem(int index, double widthConstraint, double heightConstraint)
		{
			if (index < 0 || index >= Count || this[index] == null)
				return new TSize(0, 0);

			widthConstraint = widthConstraint.ToScaledDP();
			heightConstraint = heightConstraint.ToScaledDP();

			if (widthConstraint > heightConstraint)
				widthConstraint = double.PositiveInfinity;
			else
				heightConstraint = double.PositiveInfinity;

			object? data;
			if (_groupItemSource.IsGroupHeader(index) && this[index] is TizenGroupItemSource.GroupHeaderItem headerItem)
			{
				data = headerItem.Data;
			}
			else if (_groupItemSource.IsGroupFooter(index) && this[index] is TizenGroupItemSource.GroupFooterItem footerItem)
			{
				data = footerItem.Data;
			}
			else
			{
				data = this[index];
			}

			if (_realizedRows.GetView(index) is { } createdView)
			{
				return (createdView as IView).Measure(widthConstraint, heightConstraint).ToPixel();
			}

			// Create a temporary view for measurement
			View view;
			if (_groupItemSource.IsGroupHeader(index))
			{
				view = CreateGroupHeaderView(index);
			}
			else if (_groupItemSource.IsGroupFooter(index))
			{
				view = CreateGroupFooterView(index);
			}
			else
			{
				view = CreateItemView(index);
			}

			if (data != null)
				view.BindingContext = data;
			view.Parent = _itemsView;

			view.ToPlatformView(MauiContext);
			try
			{
				return ((IView)view).Measure(widthConstraint, heightConstraint).ToPixel();
			}
			finally
			{
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
				view.Parent = null;
			}
		}

		View CreateItemView(int index)
		{
			if (ItemTemplate is DataTemplateSelector selector)
			{
				var template = selector.SelectTemplate(this[index], _itemsView)
					?? throw new InvalidOperationException("The grouped item template selector returned null.");
				return template.CreateContent() as View
					?? throw new InvalidOperationException("The grouped item template must create a View.");
			}
			else if (ItemTemplate != null)
			{
				return ItemTemplate.CreateContent() as View
					?? throw new InvalidOperationException("The grouped item template must create a View.");
			}
			else
			{
				return new XLabel { Text = this[index]?.ToString() ?? string.Empty };
			}
		}

		View CreateGroupHeaderView(int index)
		{
			if (ResolveGroupTemplate(GroupHeaderTemplate, GroupData(index)) is { } template)
			{
				return template.CreateContent() as View
					?? throw new InvalidOperationException("The group header template must create a View.");
			}
			else
			{
				// Default header rendering
				var item = this[index] as TizenGroupItemSource.GroupHeaderItem;
				return new XLabel { Text = item?.Data?.ToString() ?? string.Empty };
			}
		}

		View CreateGroupFooterView(int index)
		{
			if (ResolveGroupTemplate(GroupFooterTemplate, GroupData(index)) is { } template)
			{
				return template.CreateContent() as View
					?? throw new InvalidOperationException("The group footer template must create a View.");
			}
			else
			{
				return new XLabel { Text = string.Empty };
			}
		}

		object? GroupData(int index) =>
			this[index] switch
			{
				TizenGroupItemSource.GroupHeaderItem header => header.Data,
				TizenGroupItemSource.GroupFooterItem footer => footer.Data,
				_ => null,
			};

		DataTemplate? ResolveGroupTemplate(DataTemplate? template, object? group)
		{
			if (template is DataTemplateSelector selector)
			{
				return selector.SelectTemplate(group, _itemsView)
					?? throw new InvalidOperationException("The group template selector returned null.");
			}

			return template;
		}

		void ResetBindedView(NView native, View view)
		{
			if (_realizedRows.TryGetIndex(view, out _))
			{
				_realizedRows.Unbind(native);
				_itemsView.RemoveLogicalChild(view);
				view.BindingContext = null;
			}
		}

		void OnItemMeasureInvalidated(object? sender, EventArgs e)
		{
			if (sender is View view && _realizedRows.TryGetIndex(view, out var index))
			{
				CollectionView?.ItemMeasureInvalidated(index);
				ItemMeasureInvalidated?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var native in _nativeMauiTable.Keys.ToList())
					RemoveNativeView(native);
				_groupItemSource.CollectionChanged -= OnGroupItemsChanged;
				_headerFooter.Dispose();
				_groupItemSource.Dispose();
				_realizedRows.Clear();
			}

			base.Dispose(disposing);
		}

		void OnGroupItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
			ItemsChanged?.Invoke(this, EventArgs.Empty);
	}
}
