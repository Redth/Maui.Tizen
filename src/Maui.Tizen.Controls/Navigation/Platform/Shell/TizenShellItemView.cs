using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
using XColor = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for a ShellItem (represents the bottom tab bar and its sections).
	/// </summary>
	public class TizenShellItemView : NView, IDisposable
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
		TizenMoreItem? _moreItem;
		bool _hasMoreTab;
		TizenItemAppearance _appearance;
		bool _isDisposed;
		bool _isTabBarVisible = true;
		// The lazy-creation and current-section rules live in a NUI-free helper so they can be
		// executed in a host test; this view is an NView and cannot be instantiated off-device.
		readonly ShellSectionViewCache<ShellSection, NView> _shellSectionStackCache = new();
		readonly SelectionProposalCoordinator<ShellSection> _selection = new();
		IList<ShellSection>? _cachedGroups;
		readonly List<ShellSection> _trackedSections = new();

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
			ShellItemController.ItemsCollectionChanged += OnShellSectionsChanged;
			RefreshSectionSubscriptions();

		}

		public ShellItem ShellItem { get; private set; }

		protected IShellItemController ShellItemController => (ShellItem as IShellItemController)!;

		public IMauiContext MauiContext { get; }

		public void Rebind(ShellItem shellItem)
		{
			ArgumentNullException.ThrowIfNull(shellItem);
			if (ReferenceEquals(ShellItem, shellItem))
				return;

			ShellItemController.ItemsCollectionChanged -= OnShellSectionsChanged;
			foreach (var section in _trackedSections)
			{
				section.PropertyChanged -= OnSectionPropertyChanged;
				if (section.Handler is IDisposable handler)
				{
					handler.Dispose();
					section.Handler = null;
				}
			}
			_trackedSections.Clear();
			_shellSectionStackCache.Clear();
			if (_currentSectionStack is not null)
				Remove(_currentSectionStack);
			_currentSectionStack = null;

			if (_tabBarAdaptor is not null)
			{
				if (_bottomTabBar is not null)
					_bottomTabBar.Adaptor = null;
				_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
				_tabBarAdaptor.Dispose();
				_tabBarAdaptor = null;
			}

			if (_moreAdaptor is not null)
			{
				if (_morePopup?.Content is TCollectionView moreCollection)
					moreCollection.Adaptor = null;
				_moreAdaptor.SelectionChanged -= OnMoreItemSelected;
				_moreAdaptor.Dispose();
				_moreAdaptor = null;
			}
			_morePopup?.Dispose();
			_morePopup = null;

			ShellItem = shellItem;
			_cachedGroups = null;
			ShellItemController.ItemsCollectionChanged += OnShellSectionsChanged;
			RefreshSectionSubscriptions();
			UpdateTabBar(_isTabBarVisible);
		}

		void OnShellSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			var previousSections = _trackedSections.ToList();
			RefreshSectionSubscriptions();
			var liveSections = ShellItem.Items.ToHashSet();
			foreach (var removed in previousSections
				.Where(section => !liveSections.Contains(section))
				.ToList())
			{
				if (ReferenceEquals(_shellSectionStackCache.CurrentSection, removed)
					&& _currentSectionStack is not null)
				{
					Remove(_currentSectionStack);
					_currentSectionStack = null;
				}

				if (removed.Handler is IDisposable handler)
				{
					handler.Dispose();
					removed.Handler = null;
					_shellSectionStackCache.Remove(removed);
				}
				else
				{
					_shellSectionStackCache.Remove(removed, static stack => stack.Dispose());
				}
			}

			UpdateTabBar(_isTabBarVisible);
		}

		void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(BaseShellItem.IsVisible))
				UpdateTabBar(_isTabBarVisible);
		}

		void RefreshSectionSubscriptions()
		{
			foreach (var section in _trackedSections)
				section.PropertyChanged -= OnSectionPropertyChanged;

			_trackedSections.Clear();
			_trackedSections.AddRange(ShellItem.Items);

			foreach (var section in _trackedSections)
				section.PropertyChanged += OnSectionPropertyChanged;
		}

		/// <summary>
		/// Updates the tab bar visibility.
		/// </summary>
		public void UpdateTabBar(bool isVisible)
		{
			if (isVisible && ShellItemController.ShowTabs)
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
			var items = ShellItemController.GetItems()
				.Where(static section => section.IsVisible)
				.ToList();

			if (items.Count == 0)
			{
				HideTabBar();
				_cachedGroups = items;
				return;
			}

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
				_moreItem = new TizenMoreItem();
				tabItems.Add(_moreItem);
			}
			else
				_moreItem = null;

			if (_tabBarAdaptor != null)
			{
				if (_bottomTabBar != null)
					_bottomTabBar.Adaptor = null;
				_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
				_tabBarAdaptor.Dispose();
				_tabBarAdaptor = null;
			}

			if (_bottomTabBar == null)
			{
				_bottomTabBar = new TCollectionView
				{
					HeightSpecification = (int)80d.ToScaledPixel(),
					WidthSpecification = LayoutParamPolicies.MatchParent,
					SelectionMode = CollectionViewSelectionMode.SingleAlways,
				};
				_bottomTabBar.ScrollView.HideScrollbar = true;
				_bottomTabBar.ScrollView.ScrollEnabled = false;
				Add(_bottomTabBar);
			}

			_bottomTabBar.LayoutManager = new GridLayoutManager(
				false,
				items.Count > MaxBottomTabs ? MaxBottomTabs : items.Count);

			_tabBarAdaptor = new TizenShellSectionItemAdaptor(ShellItem, tabItems);
			_tabBarAdaptor.ItemAppearance = _appearance;
			_bottomTabBar.Adaptor = _tabBarAdaptor;
			_tabBarAdaptor.SelectionChanged += OnTabBarSelectionChanged;

			SynchronizeNativeSelection();
			_cachedGroups = items;
		}

		void HideTabBar()
		{
			if (_bottomTabBar != null)
				Remove(_bottomTabBar);
		}

		void OnTabBarSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			var nativeIndex = e.SelectedIndexes.Count > 0 ? e.SelectedIndexes[0] : -1;
			if (_selection.ConsumeManagedEcho(nativeIndex) || e.SelectedItems?.Count is not > 0)
				return;

			var selected = e.SelectedItems[0];

			if (selected is TizenMoreItem)
			{
				ShowMorePopup();
				SynchronizeNativeSelection();
			}
			else if (selected is ShellSection section)
			{
				_selection.Propose(
					section,
					candidate => ShellItemController.ProposeSection(candidate),
					SynchronizeNativeSelection);
			}
		}

		void ShowMorePopup()
		{
			if (_moreSections == null || _moreSections.Count == 0)
				return;

			if (_moreAdaptor != null)
			{
				if (_morePopup?.Content is TCollectionView oldCollection)
					oldCollection.Adaptor = null;
				_moreAdaptor.SelectionChanged -= OnMoreItemSelected;
				_moreAdaptor.Dispose();
				_moreAdaptor = null;
			}

			if (_morePopup != null)
			{
				_morePopup.Close();
				_morePopup.Dispose();
				_morePopup = null;
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
				_selection.Propose(
					section,
					candidate => ShellItemController.ProposeSection(candidate),
					SynchronizeNativeSelection);
				_morePopup?.Close();
			}
		}

		public void UpdateBottomTabBarColors(XColor? backgroundColor, XColor? titleColor, XColor? unselectedColor)
		{
			_appearance.BackgroundColor = backgroundColor;
			_appearance.TitleColor = titleColor;
			_appearance.UnselectedColor = unselectedColor;

			if (_bottomTabBar is not null)
			{
				_bottomTabBar.BackgroundColor =
					(backgroundColor ?? Microsoft.Maui.Graphics.Colors.Transparent).ToTizen().ToNative();
			}
		}

		/// <summary>
		/// Updates the current item by mounting the section view and syncing tab selection.
		/// </summary>
		public void UpdateCurrentItem(ShellSection? section)
		{
			if (section is null)
			{
				if (_currentSectionStack is not null)
					Remove(_currentSectionStack);
				_currentSectionStack = null;
				SynchronizeNativeSelection();
				return;
			}

			_currentSectionStack = _shellSectionStackCache.SetCurrent(
				section,
				create: s => s.ToPlatformView(MauiContext),
				unmount: Remove);

			if (_currentSectionStack is null)
			{
				return;
			}

			SynchronizeNativeSelection();

			Add(_currentSectionStack);
			(_currentSectionStack.Layout as global::Tizen.NUI.LayoutGroup)?.ChangeLayoutSiblingOrder(0);
		}

		void SynchronizeNativeSelection()
		{
			if (_bottomTabBar?.Adaptor is not { } adaptor)
				return;

			var current = ShellItem.CurrentItem;

			_selection.Synchronize(
				current,
				section => _moreSections?.Contains(section) == true && _moreItem is not null
					? adaptor.GetItemIndex(_moreItem)
					: adaptor.GetItemIndex(section),
				() =>
				{
					foreach (var selected in _bottomTabBar.SelectedItems.ToArray())
						_bottomTabBar.RequestItemUnselect(selected);
				},
				_bottomTabBar.RequestItemSelect);
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
				ShellItemController.ItemsCollectionChanged -= OnShellSectionsChanged;
				foreach (var section in _trackedSections)
					section.PropertyChanged -= OnSectionPropertyChanged;
				_trackedSections.Clear();

				if (_tabBarAdaptor != null)
				{
					if (_bottomTabBar != null)
						_bottomTabBar.Adaptor = null;
					_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
					_tabBarAdaptor.Dispose();
					_tabBarAdaptor = null;
				}

				if (_bottomTabBar != null)
				{
					_bottomTabBar.Dispose();
					_bottomTabBar = null;
				}

				if (_moreAdaptor != null)
				{
					if (_morePopup?.Content is TCollectionView moreCollection)
						moreCollection.Adaptor = null;
					_moreAdaptor.SelectionChanged -= OnMoreItemSelected;
					_moreAdaptor.Dispose();
					_moreAdaptor = null;
				}

				_morePopup?.Dispose();
				_morePopup = null;

				// ShellSection handlers own their platform stacks and are disposed by the parent
				// handler. Clear only this view's cache to avoid disposing the same stack twice.
				_shellSectionStackCache.Clear();
			}

			_isDisposed = true;

			// The NUI base owns the native handle; skipping this leaks it regardless of what the
			// managed teardown above released.
			base.Dispose(type);
		}
	}
}
