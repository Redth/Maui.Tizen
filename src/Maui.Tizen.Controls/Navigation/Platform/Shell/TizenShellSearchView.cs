using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>Search editor and result list hosted by the Shell toolbar.</summary>
	internal sealed class TizenShellSearchView : TizenSearchBarView
	{
		readonly NView _resultsHost;
		NCollectionView? _collectionView;
		TizenShellSearchItemAdaptor? _adaptor;
		SearchHandler? _searchHandler;
		Element? _parentElement;
		IMauiContext? _mauiContext;
		INotifyCollectionChanged? _observableItems;
		bool _updatingQuery;
		bool _disposed;

		public TizenShellSearchView()
		{
			_resultsHost = new NView
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.WrapContent,
			};

			Add(_resultsHost);
			Entry.TextChanged += OnTextChanged;
			SearchButtonPressed += OnSearchButtonPressed;
		}

		public void Bind(SearchHandler? searchHandler, Element parentElement, IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(parentElement);
			ArgumentNullException.ThrowIfNull(mauiContext);

			if (ReferenceEquals(_searchHandler, searchHandler)
				&& ReferenceEquals(_parentElement, parentElement)
				&& ReferenceEquals(_mauiContext, mauiContext))
			{
				Refresh();
				return;
			}

			DetachSearchHandler();
			_searchHandler = searchHandler;
			_parentElement = parentElement;
			_mauiContext = mauiContext;

			if (_searchHandler is null)
			{
				Refresh();
				return;
			}

			_searchHandler.PropertyChanged += OnSearchHandlerPropertyChanged;
			((ISearchHandlerController)_searchHandler).ListProxyChanged += OnListProxyChanged;
			SubscribeItems(_searchHandler.ItemsSource);
			Refresh();
		}

		void Refresh()
		{
			if (_searchHandler is null)
			{
				IsEnabled = false;
				Entry.Text = string.Empty;
				Entry.PlaceholderText = string.Empty;
				ReplaceResults(null);
				return;
			}

			IsEnabled = _searchHandler.IsSearchEnabled;
			if (_searchHandler.SearchBoxVisibility == SearchBoxVisibility.Hidden)
				Hide();
			else
				Show();
			Entry.PlaceholderText = _searchHandler.Placeholder ?? string.Empty;

			if (!string.Equals(Entry.Text, _searchHandler.Query, StringComparison.Ordinal))
			{
				_updatingQuery = true;
				try
				{
					Entry.Text = _searchHandler.Query ?? string.Empty;
				}
				finally
				{
					_updatingQuery = false;
				}
			}

			ReplaceResults(((ISearchHandlerController)_searchHandler).ListProxy);
		}

		void ReplaceResults(IEnumerable? items)
		{
			if (_collectionView is not null)
			{
				_collectionView.Adaptor = null;
				_resultsHost.Remove(_collectionView);
			}

			if (_adaptor is not null)
			{
				_adaptor.SelectionChanged -= OnSelectionChanged;
				_adaptor.Dispose();
				_adaptor = null;
			}

			_collectionView?.Dispose();
			_collectionView = null;

			if (items is null || _searchHandler is null || _parentElement is null || _mauiContext is null)
				return;

			var template = _searchHandler.ItemTemplate ?? new DataTemplate(() =>
			{
				var label = new Microsoft.Maui.Controls.Label { VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, ".");
				return label;
			});

			_adaptor = new TizenShellSearchItemAdaptor(_parentElement, _searchHandler, items, template);
			_adaptor.SelectionChanged += OnSelectionChanged;

			_collectionView = new NCollectionView
			{
				LayoutManager = new LinearLayoutManager(false),
				SelectionMode = CollectionViewSelectionMode.Single,
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.WrapContent,
				Adaptor = _adaptor,
			};
			_resultsHost.Add(_collectionView);
		}

		void OnTextChanged(object? sender, EventArgs e)
		{
			if (!_updatingQuery && _searchHandler is not null)
				_searchHandler.Query = Entry.Text;
		}

		void OnSearchButtonPressed(object? sender, EventArgs e)
		{
			if (_searchHandler is not null)
				((ISearchHandlerController)_searchHandler).QueryConfirmed();
		}

		void OnSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (_searchHandler is not null && e.SelectedItems?.Count > 0)
				((ISearchHandlerController)_searchHandler).ItemSelected(e.SelectedItems[0]);
		}

		void OnSearchHandlerPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (_searchHandler is null)
				return;

			if (e.PropertyName == nameof(SearchHandler.ItemsSource))
				SubscribeItems(_searchHandler.ItemsSource);

			if (e.PropertyName is nameof(SearchHandler.Query)
				or nameof(SearchHandler.ItemsSource)
				or nameof(SearchHandler.ItemTemplate)
				or nameof(SearchHandler.Placeholder)
				or nameof(SearchHandler.IsSearchEnabled)
				or nameof(SearchHandler.SearchBoxVisibility)
				or nameof(SearchHandler.Command)
				or nameof(SearchHandler.CommandParameter))
			{
				Refresh();
			}
		}

		void OnItemsSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
			ReplaceResults(_searchHandler is null
				? null
				: ((ISearchHandlerController)_searchHandler).ListProxy);

		void OnListProxyChanged(object? sender, ListProxyChangedEventArgs e) =>
			ReplaceResults(_searchHandler is null
				? null
				: ((ISearchHandlerController)_searchHandler).ListProxy);

		void SubscribeItems(IEnumerable? items)
		{
			if (_observableItems is not null)
				_observableItems.CollectionChanged -= OnItemsSourceChanged;

			_observableItems = items as INotifyCollectionChanged;
			if (_observableItems is not null)
				_observableItems.CollectionChanged += OnItemsSourceChanged;
		}

		void DetachSearchHandler()
		{
			SubscribeItems(null);

			if (_searchHandler is not null)
			{
				_searchHandler.PropertyChanged -= OnSearchHandlerPropertyChanged;
				((ISearchHandlerController)_searchHandler).ListProxyChanged -= OnListProxyChanged;
			}

			_searchHandler = null;
			_parentElement = null;
			_mauiContext = null;
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				Entry.TextChanged -= OnTextChanged;
				SearchButtonPressed -= OnSearchButtonPressed;
				DetachSearchHandler();
				ReplaceResults(null);
			}

			_disposed = true;
			base.Dispose(disposing);
		}
	}
}
