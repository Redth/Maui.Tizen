using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;
using NColor = Tizen.NUI.Color;
using XColor = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for a ShellSection (represents the top tab bar and its contents).
	/// </summary>
	public class TizenShellSectionView : NView, IDisposable
	{
		TCollectionView? _topTabBar;
		TizenShellContentItemAdaptor? _tabBarAdaptor;
		NView _contentArea;
		NView? _currentContent;
		TizenItemAppearance _appearance;
		bool _isDisposed;
		readonly ShellSectionViewCache<ShellContent, NView> _contentCache = new();
		readonly SelectionProposalCoordinator<ShellContent> _selection = new();
		readonly Dictionary<Page, IViewHandler?> _handlerMap = new();
		readonly Dictionary<ShellContent, Page> _pageMap = new();

		public TizenShellSectionView(ShellSection shellSection, IMauiContext context)
		{
			ShellSection = shellSection;
			MauiContext = context;
			_appearance = new TizenItemAppearance();

			WidthSpecification = LayoutParamPolicies.MatchParent;
			HeightSpecification = LayoutParamPolicies.MatchParent;

			Layout = new LinearLayout
			{
				LinearOrientation = LinearLayout.Orientation.Vertical
			};

			_contentArea = new NView
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.MatchParent,
				Weight = 1,
			};

			BuildTopTabBar();
			Add(_contentArea);

			((IShellSectionController)ShellSection).ItemsCollectionChanged += OnShellContentsChanged;
		}

		public ShellSection ShellSection { get; }

		public IMauiContext MauiContext { get; }

		public NView? CurrentContent
		{
			get => _currentContent;
			set
			{
				if (_currentContent != value)
				{
					if (_currentContent != null)
					{
						_contentArea.Remove(_currentContent);
					}

					_currentContent = value;

					if (_currentContent != null)
					{
						_currentContent.WidthSpecification = LayoutParamPolicies.MatchParent;
						_currentContent.HeightSpecification = LayoutParamPolicies.MatchParent;
						_contentArea.Add(_currentContent);
					}
				}
			}
		}

		void OnShellContentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			var liveContents = ((IShellSectionController)ShellSection).GetItems().ToHashSet();
			foreach (var removed in _pageMap.Keys.Where(content => !liveContents.Contains(content)).ToList())
			{
				if (ReferenceEquals(_contentCache.CurrentSection, removed))
					CurrentContent = null;

				_contentCache.Remove(removed);
				ReleasePage(removed);
			}

			BuildTopTabBar();
		}

		void BuildTopTabBar()
		{
			var items = ((IShellSectionController)ShellSection).GetItems().ToList();

			// Only show tab bar if more than one content
			if (items.Count <= 1)
			{
				if (_tabBarAdaptor != null)
				{
					if (_topTabBar != null)
						_topTabBar.Adaptor = null;
					_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
					_tabBarAdaptor.Dispose();
					_tabBarAdaptor = null;
				}
				if (_topTabBar != null)
				{
					Remove(_topTabBar);
					_topTabBar.Dispose();
					_topTabBar = null;
				}
				return;
			}

			if (_tabBarAdaptor != null)
			{
				if (_topTabBar != null)
					_topTabBar.Adaptor = null;
				_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
				_tabBarAdaptor.Dispose();
				_tabBarAdaptor = null;
			}

			if (_topTabBar != null)
			{
				Remove(_topTabBar);
				_topTabBar.Dispose();
			}

			_tabBarAdaptor = new TizenShellContentItemAdaptor(ShellSection, items);
			_tabBarAdaptor.ItemAppearance = _appearance;
			_tabBarAdaptor.SelectionChanged += OnTopTabSelected;

			_topTabBar = new TCollectionView
			{
				HeightSpecification = (int)40d.ToScaledPixel(),
				WidthSpecification = LayoutParamPolicies.MatchParent,
				LayoutManager = new LinearLayoutManager(true, global::Tizen.UIExtensions.NUI.ItemSizingStrategy.MeasureFirstItem),
				SelectionMode = CollectionViewSelectionMode.SingleAlways,
			};

			_topTabBar.Adaptor = _tabBarAdaptor;

			// Insert at top (before content area)
			Add(_topTabBar);
			(_topTabBar.Layout as global::Tizen.NUI.LayoutGroup)?.ChangeLayoutSiblingOrder(0);
			_topTabBar.RaiseToTop();
			SynchronizeNativeSelection();
		}

		void OnTopTabSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			var nativeIndex = e.SelectedIndexes.Count > 0 ? e.SelectedIndexes[0] : -1;
			if (_selection.ConsumeManagedEcho(nativeIndex)
				|| e.SelectedItems?.Count is not > 0
				|| e.SelectedItems[0] is not ShellContent content)
				return;

			_selection.Propose(
				content,
				candidate =>
				{
					ShellSection.CurrentItem = candidate;
					return true;
				},
				SynchronizeNativeSelection);
		}

		/// <summary>
		/// Updates the current item by mounting the ShellContent into the content area and syncing tab selection.
		/// </summary>
		public void UpdateCurrentItem(ShellContent content)
		{
			BuildTopTabBar();

			// Sync tab selection
			SynchronizeNativeSelection();

			UpdateContent(content);
		}

		void UpdateContent(ShellContent? content)
		{
			// Use the cache to track and create/reuse the platform view
			// The cache handles null content and returns null for the platform view
			var platformView = _contentCache.SetCurrent(
				content,
				c =>
				{
					// Get the page from the content via the public IShellContentController interface
					var page = ((IShellContentController)c).GetOrCreateContent();
					if (page == null)
						throw new InvalidOperationException($"ShellContent {c.Title} returned null page");
					var native = page.ToPlatformView(MauiContext);
					_pageMap[c] = page;
					_handlerMap[page] = page.Handler;
					return native;
				});

			CurrentContent = platformView;
		}

		void SynchronizeNativeSelection()
		{
			if (_topTabBar?.Adaptor is not { } adaptor)
				return;

			_selection.Synchronize(
				ShellSection.CurrentItem,
				adaptor.GetItemIndex,
				() =>
				{
					foreach (var selected in _topTabBar.SelectedItems.ToArray())
						_topTabBar.RequestItemUnselect(selected);
				},
				_topTabBar.RequestItemSelect);
		}

		public void UpdateAppearance(ShellAppearance appearance)
		{
			if (appearance == null)
				return;

			_appearance.BackgroundColor = appearance.BackgroundColor;
			_appearance.ForegroundColor = appearance.ForegroundColor;
			_appearance.TitleColor = appearance.TitleColor;
			_appearance.UnselectedColor = appearance.UnselectedColor;
		}

		/// <summary>
		/// Updates the top tab bar colors.
		/// </summary>
		public void UpdateTopTabBarColors(XColor foregroundColor, XColor backgroundColor, XColor titleColor, XColor unselectedColor)
		{
			_appearance.ForegroundColor = foregroundColor;
			_appearance.BackgroundColor = backgroundColor;
			_appearance.TitleColor = titleColor;
			_appearance.UnselectedColor = unselectedColor;

			if (_topTabBar != null)
				_topTabBar.BackgroundColor = (backgroundColor ?? Microsoft.Maui.Graphics.Colors.Transparent).ToTizen().ToNative();
		}

		// NUI's BaseHandle already exposes Dispose(); this participates in that chain rather
		// than shadowing it, which CS0108 would otherwise flag.
		public new void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected override void Dispose(DisposeTypes type)
		{
			if (_isDisposed)
				return;

			if (type == DisposeTypes.Explicit)
			{
				((IShellSectionController)ShellSection).ItemsCollectionChanged -= OnShellContentsChanged;

				if (_tabBarAdaptor != null)
				{
					if (_topTabBar != null)
						_topTabBar.Adaptor = null;
					_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
					_tabBarAdaptor.Dispose();
					_tabBarAdaptor = null;
				}

				if (_topTabBar != null)
				{
					_topTabBar.Dispose();
					_topTabBar = null;
				}

				CurrentContent = null;

				// Dispose all cached page handlers
				foreach (var kvp in _handlerMap)
				{
					(kvp.Value as IDisposable)?.Dispose();
					kvp.Key.Handler = null;
				}
				_handlerMap.Clear();
				_pageMap.Clear();
				_contentCache.Clear();
			}

			_isDisposed = true;

			// The NUI base owns the native handle; skipping this leaks it regardless of what the
			// managed teardown above released.
			base.Dispose(type);
		}

		void ReleasePage(ShellContent content)
		{
			if (!_pageMap.Remove(content, out var page))
				return;

			if (_handlerMap.Remove(page, out var handler))
				(handler as IDisposable)?.Dispose();

			page.Handler = null;
		}
	}
}
