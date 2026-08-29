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
	internal class TizenShellSearchItemAdaptor : TizenItemTemplateAdaptor
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

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (GetRegisteredView(view) is { } formsView)
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
			UnBinding(native);
			// Unregister rather than just look up: leaving the entry behind keeps the view alive and
			// lets a recycled native view resolve to a MAUI view whose handler is already disposed.
			if (UnregisterNativeView(native) is { } view)
			{
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
			}
		}

	}
}
