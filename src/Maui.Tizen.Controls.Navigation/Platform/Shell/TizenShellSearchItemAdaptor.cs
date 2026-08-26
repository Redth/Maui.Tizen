using System;
using System.Collections;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell search results to the Tizen CollectionView adaptor model.
	/// </summary>
	public class TizenShellSearchItemAdaptor : TizenItemTemplateAdaptor
	{
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
			return view.ToPlatform(MauiContext);
		}
	}
}
