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
		TizenShellSectionView? _currentShellSectionView;
		NView _contentArea;
		TPopup? _morePopup;
		TizenShellSectionItemAdaptor? _moreAdaptor;
		List<ShellSection>? _visibleSections;
		List<ShellSection>? _moreSections;
		bool _hasMoreTab;
		TizenItemAppearance _appearance;
		bool _isDisposed;

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

			BuildBottomTabBar();

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

		public IMauiContext MauiContext { get; }

		public TizenShellSectionView? CurrentShellSectionView
		{
			get => _currentShellSectionView;
			set
			{
				if (_currentShellSectionView != value)
				{
					if (_currentShellSectionView != null)
					{
						_contentArea.Remove(_currentShellSectionView);
					}

					_currentShellSectionView = value;

					if (_currentShellSectionView != null)
					{
						_currentShellSectionView.WidthSpecification = LayoutParamPolicies.MatchParent;
						_currentShellSectionView.HeightSpecification = LayoutParamPolicies.MatchParent;
						_contentArea.Add(_currentShellSectionView);
					}
				}
			}
		}

		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			UpdateAppearance(appearance);
		}

		void OnShellSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			BuildBottomTabBar();
		}

		void BuildBottomTabBar()
		{
			var items = ShellItem.Items.ToList();
			var tabBarVisible = Shell.GetTabBarIsVisible(ShellItem);

			if (!tabBarVisible || items.Count <= 1)
			{
				if (_bottomTabBar != null)
				{
					Remove(_bottomTabBar);
					_bottomTabBar.Dispose();
					_bottomTabBar = null;
				}
				if (_tabBarAdaptor != null)
				{
					_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
					_tabBarAdaptor.Dispose();
					_tabBarAdaptor = null;
				}
				return;
			}

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
			{
				_tabBarAdaptor.SelectionChanged -= OnTabBarSelectionChanged;
				_tabBarAdaptor.Dispose();
			}

			if (_bottomTabBar != null)
			{
				Remove(_bottomTabBar);
				_bottomTabBar.Dispose();
			}

			_tabBarAdaptor = new TizenShellSectionItemAdaptor(ShellItem, tabItems);
			_tabBarAdaptor.ItemAppearance = _appearance;
			_tabBarAdaptor.SelectionChanged += OnTabBarSelectionChanged;

			_bottomTabBar = new TCollectionView
			{
				HeightSpecification = (int)80d.ToScaledPixel(),
				WidthSpecification = LayoutParamPolicies.MatchParent,
				LayoutManager = new LinearLayoutManager(true, global::Tizen.UIExtensions.NUI.ItemSizingStrategy.MeasureFirstItem),
				SelectionMode = CollectionViewSelectionMode.Single,
			};

			_bottomTabBar.Adaptor = _tabBarAdaptor;

			Add(_bottomTabBar);
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

		public void UpdateCurrentItem()
		{
			// Current item is updated through section view
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

			if (disposing)
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
				_currentShellSectionView?.Dispose();
			}

			_isDisposed = true;
		}
	}
}
