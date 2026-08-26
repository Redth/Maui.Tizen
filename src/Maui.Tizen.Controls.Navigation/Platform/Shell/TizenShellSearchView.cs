using System;
using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for Shell's search handler results display.
	/// </summary>
	public class TizenShellSearchView : NView
	{
		TCollectionView? _collectionView;
		TizenShellSearchItemAdaptor? _adaptor;
		SearchHandler? _searchHandler;
		Element? _parentElement;

		public TizenShellSearchView()
		{
			Layout = new LinearLayout();
			WidthSpecification = LayoutParamPolicies.MatchParent;
			HeightSpecification = LayoutParamPolicies.WrapContent;
		}

		public IMauiContext? MauiContext { get; set; }

		/// <summary>
		/// Gets or sets the parent Element (typically the Shell) used as context for template bindings.
		/// </summary>
		public Element? ParentElement
		{
			get => _parentElement;
			set => _parentElement = value;
		}

		public SearchHandler? SearchHandler
		{
			get => _searchHandler;
			set
			{
				if (_searchHandler != null)
				{
					if (_searchHandler.ItemsSource is INotifyCollectionChanged oldCollection)
					{
						oldCollection.CollectionChanged -= OnItemsSourceChanged;
					}
				}

				_searchHandler = value;

				if (_searchHandler != null)
				{
					if (_searchHandler.ItemsSource is INotifyCollectionChanged newCollection)
					{
						newCollection.CollectionChanged += OnItemsSourceChanged;
					}
					UpdateContent();
				}
			}
		}

		void OnItemsSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			UpdateContent();
		}

		void UpdateContent()
		{
			if (_searchHandler == null || MauiContext == null)
				return;

			if (_collectionView != null)
			{
				Remove(_collectionView);
				_collectionView.Dispose();
				_collectionView = null;
			}

			if (_adaptor != null)
			{
				_adaptor.SelectionChanged -= OnSelectionChanged;
				_adaptor.Dispose();
				_adaptor = null;
			}

			var items = _searchHandler.ItemsSource;
			if (items == null)
				return;

			var template = _searchHandler.ItemTemplate ?? new DataTemplate(() =>
			{
				var label = new Microsoft.Maui.Controls.Label { VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, ".");
				return label;
			});

			// Use the parent Element (Shell) for template binding context.
			// If no parent is set, we use the SearchHandler's BindingContext or a dummy element.
			var parentElement = _parentElement ?? new ContentView { BindingContext = _searchHandler.BindingContext };
			_adaptor = new TizenShellSearchItemAdaptor(parentElement, _searchHandler, items, template);
			_adaptor.SelectionChanged += OnSelectionChanged;

			_collectionView = new TCollectionView
			{
				LayoutManager = new LinearLayoutManager(false),
				SelectionMode = CollectionViewSelectionMode.Single,
			};

			_collectionView.Adaptor = _adaptor;

			Add(_collectionView);
		}

		void OnSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (_searchHandler == null)
				return;

			var controller = _searchHandler as ISearchHandlerController;
			if (e.SelectedItems?.Count > 0)
			{
				controller?.ItemSelected(e.SelectedItems[0]);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_searchHandler != null)
				{
					if (_searchHandler.ItemsSource is INotifyCollectionChanged collection)
					{
						collection.CollectionChanged -= OnItemsSourceChanged;
					}
				}

				if (_collectionView != null)
				{
					_collectionView.Dispose();
					_collectionView = null;
				}

				if (_adaptor != null)
				{
					_adaptor.SelectionChanged -= OnSelectionChanged;
					_adaptor.Dispose();
					_adaptor = null;
				}
			}
			base.Dispose(disposing);
		}
	}
}
