using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using TPopup = Tizen.UIExtensions.NUI.Popup;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for a ShellItem (represents the bottom tab bar and its sections).
	/// </summary>
	public class TizenShellItemView : NView, IAppearanceObserver, IDisposable
	{
		const int MaxBottomTabs = 5;

		TCollectionView? _bottomTabBar;
		TizenShellSectionItemAdaptor? _tabBarAdaptor;
		NView? _currentSectionStack;
		NView _contentArea;
		TPopup? _morePopup;
		TizenShellSectionItemAdaptor? _moreAdaptor;
		List<ShellSection>? _visibleSections;
		List<ShellSection>? _moreSections;
		bool _hasMoreTab;
		TizenItemAppearance _appearance;
		bool _isDisposed;
		bool _isTabBarVisible = true;
		int _lastSelected;
		// The lazy-creation and current-section rules live in a NUI-free helper so they can be
		// executed in a host test; this view is an NView and cannot be instantiated off-device.
		readonly ShellSectionViewCache<ShellSection, NView> _shellSectionStackCache = new();
		IList<ShellSection>? _cachedGroups;

		public TizenShellItemView(ShellItem shellItem, IMauiContext context)
		{
			ShellItem = shellItem;
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
			Add(_contentArea);

			UpdateTabBar(true);

			// Subscribe to items changes
			if (ShellItem.Items is INotifyCollectionChanged ncc)
			{
				ncc.CollectionChanged += OnShellSectionsChanged;
			}

			// Subscribe to appearance changes - IAppearanceObserver is public
			var shell = ShellItem.Parent as Shell;
			if (shell != null)
			{
				((IShellController)shell).AddAppearanceObserver(this, ShellItem);
			}
		}

		public ShellItem ShellItem { get; }

		protected IShellItemController ShellItemController => (ShellItem as IShellItemController)!;

		public IMauiContext MauiContext { get; }

		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			UpdateAppearance(appearance);
		}

		void OnShellSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			UpdateTabBar(_isTabBarVisible);
		}

		/// <summary>
		/// Updates the tab bar visibility.
		/// </summary>
		public void UpdateTabBar(bool isVisible)
		{
			if (isVisible)
				ShowTabBar();
			else
				HideTabBar();

			_isTabBarVisible = isVisible;
		}

		bool IsItemChanged(IList<ShellSection> groups)
		{
			if (_cachedGroups == null)
				return true;

			if (_cachedGroups.Count != groups.Count)
				return true;

			for (int i = 0; i < groups.Count; i++)
			{
				if (_cachedGroups[i] != groups[i])
				{
					return true;
				}
			}

			return false;
		}

		void ShowTabBar()
		{
			if (!ShellItemController.ShowTabs)
				return;

			var items = ShellItem.Items.ToList();

			// BLOCKER C FIX: Re-attach the tab bar BEFORE the unchanged-items fast path.
			// If the tab bar was previously hidden/detached via HideTabBar(), we need to re-add
			// it to the view hierarchy even if the items haven't changed. The fast path check
			// must happen AFTER ensuring the tab bar is attached, not before.
			if (_bottomTabBar != null && _bottomTabBar.GetParent() == null)
			{
				Add(_bottomTabBar);
			}

			// Now the unchanged-items fast path is safe - the tab bar is already visible
			if (_bottomTabBar != null && !IsItemChanged(items))
				return;

			// Determine if we need a "More" tab
			_hasMoreTab = items.Count > MaxBottomTabs;

			if (_hasMoreTab)
			{
				_visibleSections = items.Take(MaxBottomTabs - 1).ToList();
				_moreSections = items.Skip(MaxBottomTabs - 1).ToList();
			}
			else
			{
				_visibleSections = items;
				_moreSections = null;
			}

			// Build list for adaptor
			var tabItems = new List<object>(_visibleSections!);
			if (_hasMoreTab)
			{
				tabItems.Add(new TizenMoreItem());
			}

			if (_tabBarAdaptor != null)
				_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;

			if (_bottomTabBar == null)
			{
				_bottomTabBar = new TCollectionView
				{
					HeightSpecification = (int)80d.ToScaledPixel(),
					WidthSpecification = LayoutParamPolicies.MatchParent,
					LayoutManager = new GridLayoutManager(false, items.Count > MaxBottomTabs ? MaxBottomTabs : items.Count),
					SelectionMode = CollectionViewSelectionMode.SingleAlways,
				};
				_bottomTabBar.ScrollView.HideScrollbar = true;
				_bottomTabBar.ScrollView.ScrollEnabled = false;
				Add(_bottomTabBar);
			}

			_tabBarAdaptor = new TizenShellSectionItemAdaptor(ShellItem, tabItems);
			_tabBarAdaptor.ItemAppearance = _appearance;
			_bottomTabBar.Adaptor = _tabBarAdaptor;
			_tabBarAdaptor.SelectionChanged += OnTabBarSelectionChanged;

			_bottomTabBar.RequestItemSelect(_lastSelected);
			_cachedGroups = items;
		}

		void HideTabBar()
		{
			if (_bottomTabBar != null)
				Remove(_bottomTabBar);
		}

		void OnTabBarSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (e.SelectedItems?.Count > 0)
			{
				var selected = e.SelectedItems[0];

				if (selected is TizenMoreItem)
				{
					ShowMorePopup();
				}
				else if (selected is ShellSection section)
				{
					ShellItem.CurrentItem = section;
				}
			}
		}

		void ShowMorePopup()
		{
			if (_moreSections == null || _moreSections.Count == 0)
				return;

			if (_morePopup != null)
			{
				_morePopup.Close();
				_morePopup.Dispose();
			}

			if (_moreAdaptor != null)
			{
				_moreAdaptor.SelectionChanged -= OnMoreItemSelected;
				_moreAdaptor.Dispose();
			}

			_morePopup = new TPopup
			{
				Layout = new LinearLayout
				{
					VerticalAlignment = global::Tizen.NUI.VerticalAlignment.Bottom
				},
			};

			var collectionView = new TCollectionView
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				LayoutManager = new LinearLayoutManager(false),
				SelectionMode = CollectionViewSelectionMode.Single,
				SizeHeight = (float)(50d.ToScaledPixel() * _moreSections.Count),
			};

			_moreAdaptor = new TizenShellSectionItemAdaptor(ShellItem, _moreSections);
			_moreAdaptor.SelectionChanged += OnMoreItemSelected;
			collectionView.Adaptor = _moreAdaptor;

			_morePopup.Content = collectionView;
			_morePopup.Open();
		}

		void OnMoreItemSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (e.SelectedItems?.Count > 0 && e.SelectedItems[0] is ShellSection section)
			{
				ShellItem.CurrentItem = section;
				_morePopup?.Close();
			}
		}

		void UpdateAppearance(ShellAppearance? appearance)
		{
			if (appearance == null)
				return;

			_appearance.BackgroundColor = appearance.BackgroundColor;
			_appearance.ForegroundColor = appearance.ForegroundColor;
			_appearance.TitleColor = appearance.TitleColor;
			_appearance.UnselectedColor = appearance.UnselectedColor;
		}

		/// <summary>
		/// Updates the current item by mounting the section view and syncing tab selection.
		/// </summary>
		public void UpdateCurrentItem(ShellSection section)
		{
			_currentSectionStack = _shellSectionStackCache.SetCurrent(
				section,
				create: s => s.ToPlatform(MauiContext),
				unmount: Remove);

			if (_currentSectionStack is null)
			{
				return;
			}

			var selectedIdx = _bottomTabBar?.Adaptor?.GetItemIndex(section) ?? 0;
			_lastSelected = selectedIdx < 0 ? MaxBottomTabs - 1 : selectedIdx;
			_bottomTabBar?.RequestItemSelect(_lastSelected);

			Add(_currentSectionStack);
			(_currentSectionStack.Layout as global::Tizen.NUI.LayoutGroup)?.ChangeLayoutSiblingOrder(0);
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
				var shell = ShellItem.Parent as Shell;
				if (shell != null)
				{
					((IShellController)shell).RemoveAppearanceObserver(this);
				}

				if (ShellItem.Items is INotifyCollectionChanged ncc)
				{
					ncc.CollectionChanged -= OnShellSectionsChanged;
				}

				if (_bottomTabBar != null)
				{
					_bottomTabBar.Dispose();
				}

				if (_tabBarAdaptor != null)
				{
					_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
					_tabBarAdaptor.Dispose();
				}

				if (_moreAdaptor != null)
				{
					_moreAdaptor.SelectionChanged -= OnMoreItemSelected;
					_moreAdaptor.Dispose();
				}

				_morePopup?.Dispose();

				// Dispose all cached section stacks
				_shellSectionStackCache.Clear(static stack => stack.Dispose());
			}

			_isDisposed = true;

			// The NUI base owns the native handle; skipping this leaks it regardless of what the
			// managed teardown above released.
			base.Dispose(type);
		}
	}
}
