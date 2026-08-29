using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
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
	/// Interface for item adaptors that support selection change notifications.
	/// </summary>
	internal interface ITizenItemTemplateAdaptor
	{
		/// <summary>
		/// Raised when the user changes selection from the UI.
		/// </summary>
		event EventHandler<TizenCollectionViewSelectionChangedEventArgs>? SelectionChanged;

		event EventHandler? ItemsChanged;

		/// <summary>
		/// Gets the templated view for the specified native view.
		/// </summary>
		View? GetTemplatedView(NView view);

		/// <summary>
		/// Gets the templated view for the specified index.
		/// </summary>
		View? GetTemplatedView(int index);

		/// <summary>
		/// Gets the index of the specified item in the items source.
		/// </summary>
		int GetItemIndex(object item);

		/// <summary>
		/// Gets the number of items the adaptor is presenting.
		/// </summary>
		/// <remarks>
		/// Needed to bound-check indexes before they reach the native selection surface. Satisfied
		/// implicitly by <c>ItemAdaptor.Count</c> on the base class.
		/// </remarks>
		int Count { get; }
	}

	/// <summary>
	/// Adapts MAUI item templates to Tizen's CollectionView adaptor model.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The in-tree backend used <c>Microsoft.Maui.Controls.Internals.BooleanBoxes</c> for allocation
	/// avoidance and <c>View.IsItemSelected</c> for selection state. Both are internal.
	/// </para>
	/// <para>
	/// This out-of-tree version uses plain bool values (the allocation difference is negligible) and
	/// drives selection state through <see cref="ItemSelectionState"/> which uses the public
	/// <see cref="VisualStateManager"/>.
	/// </para>
	/// </remarks>
	internal class TizenItemTemplateAdaptor : ItemAdaptor, ITizenItemTemplateAdaptor
	{
		readonly Dictionary<NView, View> _nativeMauiTable = new();
		readonly Dictionary<object, View?> _dataBindedViewTable = new();
		protected View? _headerCache;
		protected View? _footerCache;

		public TizenItemTemplateAdaptor(ItemsView itemsView)
			: this(itemsView, itemsView.ItemsSource, itemsView.ItemTemplate ?? new DefaultItemTemplate())
		{
		}

		protected TizenItemTemplateAdaptor(Element itemsView, IEnumerable items, DataTemplate template)
			: base(items)
		{
			ItemTemplate = template;
			Element = itemsView;
			IsSelectable = itemsView is SelectableItemsView;
		}

		/// <summary>
		/// Raised when the user changes selection from the UI.
		/// </summary>
		public event EventHandler<TizenCollectionViewSelectionChangedEventArgs>? SelectionChanged;

		event EventHandler? ITizenItemTemplateAdaptor.ItemsChanged
		{
			add { }
			remove { }
		}

		protected DataTemplate ItemTemplate { get; set; }

		protected Element Element { get; set; }

		protected virtual bool IsSelectable { get; }

		protected IMauiContext MauiContext => Element.Handler!.MauiContext!;

		public object GetData(int index)
		{
			if (this[index] == null)
				throw new InvalidOperationException("No data");
			return this[index]!;
		}

		public override void SendItemSelected(IEnumerable<int> selected)
		{
			var indexes = selected.Where(index => index >= 0 && index < Count).ToList();
			var items = new List<object>();
			foreach (var idx in indexes)
			{
				if (idx < 0 || Count <= idx)
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

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (_nativeMauiTable.TryGetValue(view, out View? formsView))
			{
				switch (state)
				{
					case ViewHolderState.Focused:
						// Use the public adapter instead of internal IsFocusedPropertyKey dance
						ItemSelectionState.SetItemFocused(formsView, true);
						break;
					case ViewHolderState.Normal:
						// Use the public adapter instead of internal View.IsItemSelected
						// Reset clears selection, pointer-over and focus together and recomputes once,
						// so a recycled row cannot come back still carrying a previous item's state.
						ItemSelectionState.Reset(formsView);
						break;
					case ViewHolderState.Selected:
						if (IsSelectable)
						{
							// Use the public adapter instead of internal View.IsItemSelected
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

		/// <summary>
		/// Registers the MAUI view created for <paramref name="native"/>, and starts tracking it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Derived adaptors - the Shell flyout, section, content and search item adaptors - create
		/// their own item views and must register them <b>here</b> rather than in a private table of
		/// their own. Everything that makes a row work is keyed off this registration: rebinding a
		/// recycled row, resolving the MAUI view in <see cref="UpdateViewState"/>, activating the
		/// current item, and tearing the row down. A parallel table looks equivalent and silently
		/// opts the row out of all of it.
		/// </para>
		/// <para>
		/// Enabled-state tracking is attached here too, so no caller has to remember it.
		/// </para>
		/// </remarks>
		protected void RegisterNativeView(NView native, View view)
		{
			ArgumentNullException.ThrowIfNull(native);
			ArgumentNullException.ThrowIfNull(view);

			_nativeMauiTable[native] = view;
			ItemSelectionState.TrackEnabledState(view);
		}

		/// <summary>
		/// Gets the MAUI view registered for <paramref name="native"/>, if any.
		/// </summary>
		protected View? GetRegisteredView(NView native)
			=> native is not null && _nativeMauiTable.TryGetValue(native, out View? view) ? view : null;

		/// <summary>
		/// Removes the registration for <paramref name="native"/> and stops tracking it.
		/// </summary>
		/// <returns>The view that was registered, so the caller can dispose its handler.</returns>
		protected View? UnregisterNativeView(NView native)
		{
			if (native is null || !_nativeMauiTable.TryGetValue(native, out View? view))
			{
				return null;
			}

			ItemSelectionState.UntrackEnabledState(view);
			_nativeMauiTable.Remove(native);

			return view;
		}

		public View? GetTemplatedView(NView view)
		{
			return _nativeMauiTable.TryGetValue(view, out var formsView) ? formsView : null;
		}

		public View? GetTemplatedView(int index)
		{
			var item = this[index];
			if (item != null && Count > index && _dataBindedViewTable.TryGetValue(item, out View? view))
			{
				return view;
			}
			return null;
		}

		public override object GetViewCategory(int index)
		{
			if (ItemTemplate is DataTemplateSelector selector)
			{
				return selector.SelectTemplate(this[index], Element);
			}
			return base.GetViewCategory(index);
		}

		public override NView CreateNativeView(int index)
		{
			View view;
			if (ItemTemplate is DataTemplateSelector selector)
			{
				view = (View)selector.SelectTemplate(GetData(index), Element).CreateContent();
			}
			else
			{
				view = (View)ItemTemplate.CreateContent();
			}
			var native = view.ToPlatformView(MauiContext);
			RegisterNativeView(native, view);

			return native;
		}

		public override NView CreateNativeView()
		{
			return CreateNativeView(0);
		}

#pragma warning disable CS8764
		public override NView? GetHeaderView()
#pragma warning restore CS8764
		{
			ReleaseCachedView(ref _headerCache);
			_headerCache = CreateHeaderView();
			if (_headerCache != null)
			{
				_headerCache.Parent = Element;

				(_headerCache.Handler as IDisposable)?.Dispose();
				_headerCache.Handler = null;
				_headerCache.MeasureInvalidated += OnHeaderFooterMeasureInvalidated;
				return _headerCache.ToPlatformView(MauiContext);
			}
			return null;
		}

#pragma warning disable CS8764
		public override NView? GetFooterView()
#pragma warning restore CS8764
		{
			ReleaseCachedView(ref _footerCache);
			_footerCache = CreateFooterView();
			if (_footerCache != null)
			{
				_footerCache.Parent = Element;
				(_footerCache.Handler as IDisposable)?.Dispose();
				_footerCache.Handler = null;
				_footerCache.MeasureInvalidated += OnHeaderFooterMeasureInvalidated;
				return _footerCache.ToPlatformView(MauiContext);
			}
			return null;
		}

		public override void RemoveNativeView(NView native)
		{
			UnBinding(native);
			if (UnregisterNativeView(native) is { } view)
			{
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
			}
		}

		public override void SetBinding(NView native, int index)
		{
			if (_nativeMauiTable.TryGetValue(native, out View? view))
			{
				ResetBindedView(view);
				view.BindingContext = this[index];
				_dataBindedViewTable[this[index]!] = view;
				view.MeasureInvalidated += OnItemMeasureInvalidated;
				view.Parent = Element;

				AddLogicalChild(view);
			}
		}

		public override void UnBinding(NView native)
		{
			if (_nativeMauiTable.TryGetValue(native, out View? view))
			{
				view.MeasureInvalidated -= OnItemMeasureInvalidated;
				ResetBindedView(view);
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

			// TODO. It is hack code, it should be updated by Tizen.UIExtensions
			if (widthConstraint > heightConstraint)
				widthConstraint = double.PositiveInfinity;
			else
				heightConstraint = double.PositiveInfinity;

			if (_dataBindedViewTable.TryGetValue(GetData(index), out View? createdView) && createdView != null)
			{
				return (createdView as IView).Measure(widthConstraint, heightConstraint).ToPixel();
			}

			View view;
			if (ItemTemplate is DataTemplateSelector selector)
			{
				var template = selector.SelectTemplate(GetData(index), Element)
					?? throw new InvalidOperationException("The item template selector returned null.");
				view = template.CreateContent() as View
					?? throw new InvalidOperationException("The item template must create a View.");
			}
			else
			{
				view = ItemTemplate.CreateContent() as View
					?? throw new InvalidOperationException("The item template must create a View.");
			}

			if (Count > index)
				view.BindingContext = this[index];
			view.Parent = Element;

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

		public override TSize MeasureHeader(double widthConstraint, double heightConstraint)
		{
			// TODO. It is workaround code, if update Tizen.UIExtensions.NUI, this code will be removed
			if (CollectionView is NCollectionView cv)
			{
				if (cv.LayoutManager != null)
				{
					if (cv.LayoutManager.IsHorizontal)
						widthConstraint = double.PositiveInfinity;
					else
						heightConstraint = double.PositiveInfinity;
				}
			}

			return (_headerCache as IView)?.Measure(widthConstraint.ToScaledDP(), heightConstraint.ToScaledDP()).ToPixel() ?? new TSize(0, 0);
		}

		public override TSize MeasureFooter(double widthConstraint, double heightConstraint)
		{
			return (_footerCache as IView)?.Measure(widthConstraint.ToScaledDP(), heightConstraint.ToScaledDP()).ToPixel() ?? new TSize(0, 0);
		}

		protected virtual View? CreateHeaderView()
		{
			if (Element is StructuredItemsView structuredItemsView)
			{
				if (structuredItemsView.Header != null)
				{
					View? header = null;
					if (structuredItemsView.Header is View view)
					{
						header = view;
					}
					else if (structuredItemsView.HeaderTemplate != null)
					{
						header = (View)structuredItemsView.HeaderTemplate.CreateContent();
						header.BindingContext = structuredItemsView.Header;
					}
					else if (structuredItemsView.Header is string str)
					{
						header = new XLabel { Text = str, };
					}
					return header;
				}
			}
			return null;
		}

		protected virtual View? CreateFooterView()
		{
			if (Element is StructuredItemsView structuredItemsView)
			{
				if (structuredItemsView.Footer != null)
				{
					View? footer = null;
					if (structuredItemsView.Footer is View view)
					{
						footer = view;
					}
					else if (structuredItemsView.FooterTemplate != null)
					{
						footer = (View)structuredItemsView.FooterTemplate.CreateContent();
						footer.BindingContext = structuredItemsView.Footer;
					}
					else if (structuredItemsView.Footer is string str)
					{
						footer = new XLabel { Text = str, };
					}
					return footer;
				}
			}
			return null;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ReleaseCachedView(ref _headerCache);
				ReleaseCachedView(ref _footerCache);
			}
			base.Dispose(disposing);
		}

		void ReleaseCachedView(ref View? view)
		{
			if (view is null)
				return;

			view.MeasureInvalidated -= OnHeaderFooterMeasureInvalidated;
			(view.Handler as IDisposable)?.Dispose();
			view.Handler = null;
			view.Parent = null;
			view = null;
		}

		void ResetBindedView(View view)
		{
			if (view.BindingContext != null && _dataBindedViewTable.ContainsKey(view.BindingContext))
			{
				_dataBindedViewTable[view.BindingContext] = null;
				RemoveLogicalChild(view);
				view.BindingContext = null;
			}
		}

		void OnItemMeasureInvalidated(object? sender, EventArgs e)
		{
			var data = (sender as View)?.BindingContext ?? null;
			int index = data != null ? GetItemIndex(data) : -1;

			if (index != -1)
			{
				CollectionView?.ItemMeasureInvalidated(index);
			}
		}

		void OnHeaderFooterMeasureInvalidated(object? sender, EventArgs e)
		{
			CollectionView?.ItemMeasureInvalidated(-1);
		}

		void AddLogicalChild(Element element)
		{
			// AddLogicalChild and RemoveLogicalChild are public on ItemsView
			if (Element is ItemsView iv)
			{
				iv.AddLogicalChild(element);
			}
			else
			{
				element.Parent = Element;
			}
		}

		void RemoveLogicalChild(Element element)
		{
			if (Element is ItemsView iv)
			{
				iv.RemoveLogicalChild(element);
			}
			else
			{
				element.Parent = null;
			}
		}
	}

	/// <summary>
	/// Specialized adaptor for CarouselView that sizes items to fill the viewport.
	/// </summary>
	internal class TizenCarouselViewItemTemplateAdaptor : TizenItemTemplateAdaptor
	{
		public TizenCarouselViewItemTemplateAdaptor(ItemsView itemsView) : base(itemsView) { }

		public override TSize MeasureItem(double widthConstraint, double heightConstraint)
		{
			return MeasureItem(0, widthConstraint, heightConstraint);
		}

		public override TSize MeasureItem(int index, double widthConstraint, double heightConstraint)
		{
			return (CollectionView as NView)!.Size.ToCommon();
		}
	}

	/// <summary>
	/// Default item template that displays the item's ToString() value.
	/// </summary>
	internal class DefaultItemTemplate : DataTemplate
	{
		public DefaultItemTemplate() : base(CreateView) { }

		class ToTextConverter : IValueConverter
		{
			public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
			{
				return value?.ToString() ?? string.Empty;
			}

			public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
		}

		static View CreateView()
		{
			var label = new XLabel
			{
				TextColor = Colors.Black,
			};
			label.SetBinding(XLabel.TextProperty, static (object source) => source, converter: new ToTextConverter());

			return new Microsoft.Maui.Controls.StackLayout
			{
				BackgroundColor = Colors.White,
				Padding = 30,
				Children =
				{
					label
				}
			};
		}
	}
}
