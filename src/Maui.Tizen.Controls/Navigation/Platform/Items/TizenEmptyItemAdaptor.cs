using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;

using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;
using TSize = Tizen.UIExtensions.Common.Size;
using XLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adaptor for displaying an empty view when <see cref="ItemsView.ItemsSource"/> is empty.
	/// </summary>
	internal class TizenEmptyItemAdaptor : ItemAdaptor
	{
		static readonly object[] s_emptyItems = new object[] { new object() };
		readonly Dictionary<NView, View> _nativeTable = new();
		readonly ItemsView _itemsView;
		readonly TizenHeaderFooterPresenter? _headerFooter;

		public TizenEmptyItemAdaptor(ItemsView itemsView)
			: base(HasEmptyContent(itemsView) ? s_emptyItems : Array.Empty<object>())
		{
			_itemsView = itemsView;
			if (itemsView is StructuredItemsView structured)
			{
				_headerFooter = new TizenHeaderFooterPresenter(
					structured,
					() => MauiContext,
					() => CollectionView?.ItemMeasureInvalidated(-1));
			}
		}

		protected IMauiContext MauiContext => _itemsView.Handler!.MauiContext!;

		static bool HasEmptyContent(ItemsView itemsView) =>
			ViewportConstraint.NeedsEmptyPlaceholder(
				itemsView.EmptyView is not null,
				itemsView.EmptyViewTemplate is not null,
				itemsView is StructuredItemsView { Header: not null },
				itemsView is StructuredItemsView { Footer: not null });

		public override NView CreateNativeView(int index)
		{
			View emptyView = CreateEmptyView();
			var native = emptyView.ToPlatformView(MauiContext);
			_nativeTable[native] = emptyView;
			return native;
		}

		public override NView CreateNativeView()
		{
			return CreateNativeView(0);
		}

		public override void RemoveNativeView(NView native)
		{
			UnBinding(native);
			if (_nativeTable.Remove(native, out View? view))
			{
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
			}
		}

		public override void SetBinding(NView native, int index)
		{
			if (_nativeTable.TryGetValue(native, out View? view))
			{
				// Empty view binding context is the EmptyView property value itself (when it's data)
				// or the view's existing context
				if (_itemsView.EmptyView != null && _itemsView.EmptyView is not View)
				{
					view.BindingContext = _itemsView.EmptyView;
				}
				view.Parent = _itemsView;
				_itemsView.AddLogicalChild(view);
			}
		}

		public override void UnBinding(NView native)
		{
			if (_nativeTable.TryGetValue(native, out View? view))
			{
				_itemsView.RemoveLogicalChild(view);
				view.Parent = null;
			}
		}

		public override TSize MeasureItem(double widthConstraint, double heightConstraint)
		{
			return MeasureItem(0, widthConstraint, heightConstraint);
		}

		public override TSize MeasureItem(int index, double widthConstraint, double heightConstraint)
		{
			var allocated = (CollectionView as NView)?.Size.ToCommon() ?? TSize.Zero;
			var header = _headerFooter?.MeasureHeader(allocated.Width, allocated.Height) ?? TSize.Zero;
			var footer = _headerFooter?.MeasureFooter(allocated.Width, allocated.Height) ?? TSize.Zero;
			var layoutManager = (CollectionView as NCollectionView)?.LayoutManager;
			var horizontal = layoutManager?.IsHorizontal == true;
			var grid = layoutManager as GridLayoutManager;
			var remainingWidth = horizontal
				? ViewportConstraint.Remaining(allocated.Width, header.Width, footer.Width)
				: allocated.Width;
			var remainingHeight = horizontal
				? allocated.Height
				: ViewportConstraint.Remaining(allocated.Height, header.Height, footer.Height);
			return new TSize(
				(float)ViewportConstraint.ResolveEmptyCell(
					widthConstraint,
					remainingWidth,
					!horizontal && grid?.Span > 1),
				(float)ViewportConstraint.ResolveEmptyCell(
					heightConstraint,
					remainingHeight,
					horizontal && grid?.Span > 1));
		}

		/// <summary>
		/// Gets the header view. Not used for empty adaptor.
		/// </summary>
		public override NView? GetHeaderView() => _headerFooter?.GetHeaderView();

		/// <summary>
		/// Gets the footer view. Not used for empty adaptor.
		/// </summary>
		public override NView? GetFooterView() => _headerFooter?.GetFooterView();

		/// <summary>
		/// Measures the header. Returns zero size since there is no header.
		/// </summary>
		public override TSize MeasureHeader(double widthConstraint, double heightConstraint) =>
			_headerFooter?.MeasureHeader(widthConstraint, heightConstraint) ?? new TSize(0, 0);

		/// <summary>
		/// Measures the footer. Returns zero size since there is no footer.
		/// </summary>
		public override TSize MeasureFooter(double widthConstraint, double heightConstraint) =>
			_headerFooter?.MeasureFooter(widthConstraint, heightConstraint) ?? new TSize(0, 0);

		View CreateEmptyView()
		{
			if (_itemsView.EmptyView is View emptyView)
			{
				return emptyView;
			}
			else if (_itemsView.EmptyViewTemplate != null)
			{
				var view = (View)_itemsView.EmptyViewTemplate.CreateContent();
				if (_itemsView.EmptyView != null)
				{
					view.BindingContext = _itemsView.EmptyView;
				}
				return view;
			}
			else if (_itemsView.EmptyView != null)
			{
				// EmptyView is data, create a default view
				return new XLabel { Text = _itemsView.EmptyView.ToString() };
			}
			else
			{
				return new ContentView();
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_headerFooter?.Dispose();

			base.Dispose(disposing);
		}
	}
}
