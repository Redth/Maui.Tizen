using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell search results to the Tizen CollectionView adaptor model.
	/// </summary>
	public class TizenShellSearchItemAdaptor : TizenItemTemplateAdaptor
	{
		readonly Dictionary<NView, View> _shellNativeMauiTable = new();

		/// <summary>
		/// Initializes a new instance of the adaptor for Shell search results.
		/// </summary>
		/// <param name="parentElement">An Element to use as the parent context for template binding (typically the Shell).</param>
		/// <param name="searchHandler">The SearchHandler owning these results.</param>
		/// <param name="items">The items source.</param>
		/// <param name="template">The data template for item display.</param>
		public TizenShellSearchItemAdaptor(Element parentElement, SearchHandler searchHandler, IEnumerable items, DataTemplate template) :
			base(parentElement, items, template)
		{
			SearchHandler = searchHandler;
		}

		/// <summary>
		/// Gets the SearchHandler this adaptor is displaying results for.
		/// </summary>
		public SearchHandler SearchHandler { get; }

		protected override bool IsSelectable => true;

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			View view;

			if (ItemTemplate is DataTemplateSelector selector)
			{
				view = (View)selector.SelectTemplate(item, Element).CreateContent();
			}
			else
			{
				view = (View)ItemTemplate.CreateContent();
			}

			// Set the Shell as the parent context for the templated view.
			// SearchHandler is not an Element, so we use the parent Element passed to the constructor.
			if (Element != null)
			{
				view.Parent = Element;
			}
			view.BindingContext = item;
			var native = view.ToPlatform(MauiContext);

			// Register native-to-MAUI mapping for selection state tracking
			_shellNativeMauiTable[native] = view;
			ItemSelectionState.TrackEnabledState(view);

			return native;
		}

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (_shellNativeMauiTable.TryGetValue(view, out View? formsView))
			{
				switch (state)
				{
					case ViewHolderState.Focused:
						ItemSelectionState.SetItemFocused(formsView, true);
						break;
					case ViewHolderState.Normal:
						ItemSelectionState.Reset(formsView);
						break;
					case ViewHolderState.Selected:
						ItemSelectionState.SetItemSelectedAndUnfocused(formsView, true);
						break;
				}
			}
		}

		public override void RemoveNativeView(NView native)
		{
			if (_shellNativeMauiTable.TryGetValue(native, out View? view))
			{
				ItemSelectionState.UntrackEnabledState(view);
				_shellNativeMauiTable.Remove(native);

				if (view.Handler is ITizenPlatformViewHandler handler)
				{
					handler.Dispose();
					view.Handler = null;
				}
			}
			base.RemoveNativeView(native);
		}
	}
}
