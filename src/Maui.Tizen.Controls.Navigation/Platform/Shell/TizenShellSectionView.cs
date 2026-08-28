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
		readonly Dictionary<Page, IViewHandler?> _handlerMap = new();

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

			// Subscribe to items changes
			if (ShellSection.Items is INotifyCollectionChanged ncc)
			{
				ncc.CollectionChanged += OnShellContentsChanged;
			}
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
			BuildTopTabBar();
		}

		void BuildTopTabBar()
		{
			var items = ShellSection.Items.ToList();

			// Only show tab bar if more than one content
			if (items.Count <= 1)
			{
				if (_topTabBar != null)
				{
					Remove(_topTabBar);
					_topTabBar.Dispose();
					_topTabBar = null;
				}
				if (_tabBarAdaptor != null)
				{
					_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
					_tabBarAdaptor.Dispose();
					_tabBarAdaptor = null;
				}
				return;
			}

			if (_tabBarAdaptor != null)
			{
				_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
				_tabBarAdaptor.Dispose();
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
		}

		void OnTopTabSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (e.SelectedItems?.Count > 0 && e.SelectedItems[0] is ShellContent content)
			{
				ShellSection.CurrentItem = content;
			}
		}

		/// <summary>
		/// Updates the current item by mounting the ShellContent into the content area and syncing tab selection.
		/// </summary>
		public void UpdateCurrentItem(ShellContent content)
		{
			BuildTopTabBar();

			// Sync tab selection
			var selectedIdx = _topTabBar?.Adaptor?.GetItemIndex(content) ?? 0;
			if (selectedIdx >= 0)
				_topTabBar?.RequestItemSelect(selectedIdx);

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
					var native = page.ToPlatform(MauiContext);
					_handlerMap[page] = page.Handler;
					return native;
				});

			CurrentContent = platformView;
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

			if (_topTabBar != null && backgroundColor != null)
			{
				_topTabBar.BackgroundColor = backgroundColor.ToNUIColor();
			}
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
				if (ShellSection.Items is INotifyCollectionChanged ncc)
				{
					ncc.CollectionChanged -= OnShellContentsChanged;
				}

				if (_topTabBar != null)
				{
					_topTabBar.Dispose();
				}

				if (_tabBarAdaptor != null)
				{
					_tabBarAdaptor.SelectionChanged -= OnTopTabSelected;
					_tabBarAdaptor.Dispose();
				}

				// Dispose all cached page handlers
				foreach (var kvp in _handlerMap)
				{
					if (kvp.Value is ITizenPlatformViewHandler handler)
					{
						handler.Dispose();
					}
					kvp.Key.Handler = null;
				}
				_handlerMap.Clear();
				_contentCache.Clear();
			}

			_isDisposed = true;

			// The NUI base owns the native handle; skipping this leaks it regardless of what the
			// managed teardown above released.
			base.Dispose(type);
		}
	}
}
