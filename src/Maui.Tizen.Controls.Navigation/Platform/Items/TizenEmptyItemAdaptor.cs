using System;
using System.Collections.Generic;
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
	/// Adaptor for displaying an empty view when <see cref="ItemsView.ItemsSource"/> is empty.
	/// </summary>
	public class TizenEmptyItemAdaptor : ItemAdaptor
	{
		static readonly object[] s_emptyItems = new object[] { new object() };
		readonly Dictionary<NView, View> _nativeTable = new();
		readonly ItemsView _itemsView;

		public TizenEmptyItemAdaptor(ItemsView itemsView)
			: base(s_emptyItems)
		{
			_itemsView = itemsView;
		}

		protected IMauiContext MauiContext => _itemsView.Handler!.MauiContext!;

		public override NView CreateNativeView(int index)
		{
			View emptyView = CreateEmptyView();
			var native = emptyView.ToPlatform(MauiContext);
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
			if (_nativeTable.TryGetValue(native, out View? view))
			{
				if (view.Handler is IPlatformViewHandler handler)
				{
					_nativeTable.Remove(handler.PlatformView!);
					handler.Dispose();
					view.Handler = null;
				}
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
			// The empty view should fill the available space
			return new TSize((float)widthConstraint, (float)heightConstraint);
		}

		/// <summary>
		/// Gets the header view. Not used for empty adaptor.
		/// </summary>
		public override NView? GetHeaderView() => null;

		/// <summary>
		/// Gets the footer view. Not used for empty adaptor.
		/// </summary>
		public override NView? GetFooterView() => null;

		/// <summary>
		/// Measures the header. Returns zero size since there is no header.
		/// </summary>
		public override TSize MeasureHeader(double widthConstraint, double heightConstraint) => new TSize(0, 0);

		/// <summary>
		/// Measures the footer. Returns zero size since there is no footer.
		/// </summary>
		public override TSize MeasureFooter(double widthConstraint, double heightConstraint) => new TSize(0, 0);

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
	}
}
