using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;
using TSize = Tizen.UIExtensions.Common.Size;

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
		bool _resultsVisible;
		bool _disposed;

		public TizenShellSearchView()
		{
			_resultsHost = new NView
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.WrapContent,
			};

			Add(_resultsHost);
			_resultsHost.Hide();
			Entry.TextChanged += OnTextChanged;
			SearchButtonPressed += OnSearchButtonPressed;
			LayoutUpdated += OnShellLayoutUpdated;
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
			UpdateResultsVisibility();
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
			{
				UpdateResultsVisibility();
				return;
			}

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
			UpdateResultsVisibility();
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
			{
				var handler = _searchHandler;
				((ISearchHandlerController)handler).ItemSelected(e.SelectedItems[0]);
				if (ReferenceEquals(_searchHandler, handler))
				{
					_resultsVisible = false;
					_resultsHost.Hide();
					void ClearQuery()
					{
						if (!ReferenceEquals(_searchHandler, handler))
							return;

						handler.Query = string.Empty;
						_updatingQuery = true;
						try
						{
							Entry.Text = string.Empty;
						}
						finally
						{
							_updatingQuery = false;
						}
						UpdateResultsVisibility();
					}

					if (handler.Dispatcher is { } dispatcher)
						dispatcher.Dispatch(ClearQuery);
					else
						ClearQuery();
				}
			}
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

		public override TSize Measure(double availableWidth, double availableHeight)
		{
			var search = base.Measure(availableWidth, availableHeight);
			return new TSize(search.Width, search.Height + MeasureResults(availableWidth, availableHeight));
		}

		protected override void LayoutContent(float width, float height)
		{
			var search = base.Measure(width, height);
			var searchHeight = (float)Math.Min(height, search.Height);
			base.LayoutContent(width, searchHeight);

			_resultsHost.Position = new Position(0, searchHeight);
			_resultsHost.SizeWidth = width;
			_resultsHost.SizeHeight = (float)MeasureResults(width, double.PositiveInfinity);
			if (_collectionView is not null)
			{
				_collectionView.SizeWidth = _resultsHost.SizeWidth;
				_collectionView.SizeHeight = _resultsHost.SizeHeight;
			}
		}

		double MeasureResults(double width, double height)
		{
			if (!_resultsVisible || _adaptor is null)
				return 0;

			double measured = 0;
			for (var index = 0; index < _adaptor.Count; index++)
				measured += _adaptor.MeasureItem(index, width, height).Height;

			return SearchResultsLayout.ConstrainHeight(
				measured,
				Devices.DeviceDisplay.MainDisplayInfo.Height);
		}

		void OnShellLayoutUpdated(object? sender, global::Tizen.UIExtensions.Common.LayoutEventArgs e) =>
			LayoutContent(SizeWidth, SizeHeight);

		void UpdateResultsVisibility()
		{
			var visible = SearchResultsLayout.IsVisible(
				_searchHandler?.Query,
				_adaptor?.Count ?? 0,
				_searchHandler?.SearchBoxVisibility == SearchBoxVisibility.Hidden);
			if (_resultsVisible == visible)
				return;

			_resultsVisible = visible;
			if (visible)
				_resultsHost.Show();
			else
				_resultsHost.Hide();
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			_disposed = true;
			if (disposing)
			{
				ExceptionSafeCleanup.Run(
					() => LayoutUpdated -= OnShellLayoutUpdated,
					() => Entry.TextChanged -= OnTextChanged,
					() => SearchButtonPressed -= OnSearchButtonPressed,
					DetachSearchHandler,
					() => ReplaceResults(null),
					DisconnectEvents,
					() => base.Dispose(disposing));
				return;
			}

			base.Dispose(disposing);
		}
	}
}
