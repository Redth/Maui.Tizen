using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;
using NColor = Tizen.NUI.Color;
using XView = Microsoft.Maui.Controls.View;
using XColor = Microsoft.Maui.Graphics.Color;
using XShadow = Microsoft.Maui.Controls.Shadow;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for the entire Shell layout: drawer, flyout, content, and toolbar.
	/// </summary>
	public class TizenShellView : NView, IAppearanceObserver, IDisposable
	{
		INavigationDrawer? _navigationDrawer;
		TizenNavigationView? _flyoutView;
		TizenNavigationContentView? _mainContentView;
		TCollectionView? _flyoutCollectionView;
		TizenShellFlyoutItemAdaptor? _flyoutAdaptor;
		TizenShellItemView? _currentShellItemView;
		readonly ShellSectionViewCache<ShellItem, TizenShellItemView> _shellItemCache = new();
		readonly Dictionary<ShellItem, IDisposable> _shellItemHandlers = new();
		TizenShellSearchView? _searchView;
		SearchHandler? _currentSearchHandler;
		Page? _currentPage;
		readonly List<Element> _searchOwners = new();
		XView? _fixedHeader;
		XView? _fixedFooter;
		XView? _customFlyoutContent;
		readonly SelectionProposalCoordinator<Element> _flyoutSelection = new();
		// Ownership tracker rather than a cached field: Core's ITizenToolbarContainer.SetToolbar
		// DISPOSES the toolbar it replaces, so a raw cached reference can be observed after disposal.
		readonly ToolbarOwnership<TizenToolbarView> _toolbarOwnership;
		TizenWrapperView? _backdropView;
		bool _isDisposed;
		bool _isOpen;
		ShellAppearance? _appearance;
		readonly TizenItemAppearance _itemAppearance = new();

		static readonly XColor DefaultBackgroundColor = new XColor(1f, 1f, 1f, 1f);
		static readonly XColor DefaultBackdropColor = new XColor(0.2f, 0.2f, 0.2f, 0.2f);

		/// <summary>
		/// Creates a new shell view. Call <see cref="SetElement"/> to initialize.
		/// </summary>
		public TizenShellView()
		{
			_toolbarOwnership = new ToolbarOwnership<TizenToolbarView>(
				toolbar => toolbar.IconPressed += OnToolbarIconPressed,
				toolbar => toolbar.IconPressed -= OnToolbarIconPressed);

			WidthSpecification = LayoutParamPolicies.MatchParent;
			HeightSpecification = LayoutParamPolicies.MatchParent;
			Layout = new LinearLayout();
		}

		/// <summary>
		/// Raised when the flyout drawer is toggled open or closed.
		/// </summary>
		public event EventHandler? Toggled;

		/// <summary>
		/// Gets the shell element.
		/// </summary>
		public Shell? Shell { get; private set; }

		/// <summary>
		/// Gets the MAUI context.
		/// </summary>
		public IMauiContext? MauiContext { get; private set; }

		/// <summary>
		/// Gets the shell controller.
		/// </summary>
		protected IShellController? ShellController => Shell as IShellController;

		bool HeaderOnMenu => Shell?.FlyoutHeaderBehavior is
			FlyoutHeaderBehavior.Scroll or FlyoutHeaderBehavior.CollapseOnScroll;

		/// <summary>
		/// Gets or sets whether the flyout is open.
		/// </summary>
		public bool IsOpened
		{
			get => _isOpen;
			set
			{
				if (_isOpen != value)
					_isOpen = value;

				if (_navigationDrawer == null)
					return;

				if (value)
					_ = _navigationDrawer.OpenAsync(true);
				else
					_ = _navigationDrawer.CloseAsync(true);
			}
		}

		/// <summary>
		/// Gets or sets the current shell item view.
		/// </summary>
		public TizenShellItemView? CurrentShellItemView
		{
			get => _currentShellItemView;
			set
			{
				if (_currentShellItemView != value)
				{
					if (_currentShellItemView != null && _mainContentView != null)
					{
						_mainContentView.Content = null;
					}

					_currentShellItemView = value;

					if (_currentShellItemView != null && _mainContentView != null)
					{
						_mainContentView.Content = _currentShellItemView;
					}
				}
			}
		}

		/// <summary>
		/// Initializes the shell view with the shell element and context.
		/// </summary>
		public void SetElement(Shell shell, IMauiContext context)
		{
			if (ReferenceEquals(Shell, shell))
			{
				MauiContext = context;
				return;
			}

			if (Shell is not null && !ReferenceEquals(Shell, shell))
				ResetElementState();

			Shell = shell ?? throw new ArgumentNullException(nameof(shell));
			MauiContext = context ?? throw new ArgumentNullException(nameof(context));

			if (_navigationDrawer is null)
			{
				_navigationDrawer = CreateNavigationDrawer();

				_flyoutView = CreateNavigationView();
				_navigationDrawer.Drawer = _flyoutView.TargetView;

				_mainContentView = CreateNavigationContentView();
				_navigationDrawer.Content = _mainContentView.TargetView;

				_navigationDrawer.Toggled += OnDrawerToggled;
				Add((NView)_navigationDrawer);
			}

			// Subscribe to appearance changes - IAppearanceObserver is public
			((IShellController)Shell).AddAppearanceObserver(this, Shell);
			Shell.PropertyChanged += OnShellPropertyChanged;
			if (Shell.Items is INotifyCollectionChanged items)
				items.CollectionChanged += OnShellItemsChanged;
		}

		void ResetElementState()
		{
			if (Shell is not null)
			{
				((IShellController)Shell).RemoveAppearanceObserver(this);
				Shell.PropertyChanged -= OnShellPropertyChanged;
				if (Shell.Items is INotifyCollectionChanged items)
					items.CollectionChanged -= OnShellItemsChanged;
			}

			if (_currentPage is not null)
				_currentPage.PropertyChanged -= OnCurrentPagePropertyChanged;
			_currentPage = null;
			RefreshSearchOwnerSubscriptions(null);
			DetachToolbar();

			if (_flyoutView is not null)
			{
				SetFixedFlyoutView(ref _fixedHeader, null, value => _flyoutView.Header = value);
				SetFixedFlyoutView(ref _fixedFooter, null, value => _flyoutView.Footer = value);
				_flyoutView.Content = null;
			}

			if (_flyoutCollectionView is not null)
				_flyoutCollectionView.Adaptor = null;
			if (_flyoutAdaptor is not null)
			{
				_flyoutAdaptor.SelectionChanged -= OnFlyoutItemSelected;
				_flyoutAdaptor.Dispose();
				_flyoutAdaptor = null;
			}

			if (_customFlyoutContent is not null)
			{
				(_customFlyoutContent.Handler as IDisposable)?.Dispose();
				_customFlyoutContent.Handler = null;
				_customFlyoutContent.Parent = null;
				_customFlyoutContent = null;
			}

			CurrentShellItemView = null;
			_shellItemCache.Clear();
			foreach (var handler in _shellItemHandlers.Values)
				handler.Dispose();
			foreach (var item in _shellItemHandlers.Keys)
				item.Handler = null;
			_shellItemHandlers.Clear();
			_appearance = null;
			_itemAppearance.BackgroundColor = null;
			_itemAppearance.ForegroundColor = null;
			_itemAppearance.TitleColor = null;
			_itemAppearance.UnselectedColor = null;
		}

		/// <summary>
		/// Creates the navigation drawer.
		/// </summary>
		protected virtual INavigationDrawer CreateNavigationDrawer()
		{
			return new NavigationDrawer();
		}

		/// <summary>
		/// Creates the navigation view for the flyout.
		/// </summary>
		protected virtual TizenNavigationView CreateNavigationView()
		{
			return new TizenNavigationView();
		}

		/// <summary>
		/// Creates the navigation content view.
		/// </summary>
		protected virtual TizenNavigationContentView CreateNavigationContentView()
		{
			return new TizenNavigationContentView();
		}

		void OnDrawerToggled(object? sender, EventArgs e)
		{
			if (_navigationDrawer != null)
				_isOpen = _navigationDrawer.IsOpened;
			Toggled?.Invoke(this, EventArgs.Empty);
		}

		void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == Shell.CurrentStateProperty.PropertyName
				|| e.PropertyName == Shell.SearchHandlerProperty.PropertyName)
				UpdateSearchHandler();
		}

		void OnShellItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (Shell is null)
				return;

			var liveItems = Shell.Items.ToHashSet();
			foreach (var removed in _shellItemHandlers.Keys.Where(item => !liveItems.Contains(item)).ToList())
			{
				if (ReferenceEquals(_shellItemCache.CurrentSection, removed))
					CurrentShellItemView = null;

				_shellItemCache.Remove(removed);
				if (_shellItemHandlers.Remove(removed, out var handler))
					handler.Dispose();
				removed.Handler = null;
			}

			UpdateItems();
			SynchronizeFlyoutSelection();
		}

		/// <summary>
		/// Called when appearance changes.
		/// </summary>
		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			_appearance = appearance;
			UpdateAppearance(appearance);
		}

		/// <summary>
		/// Updates the custom flyout view.
		/// </summary>
		public void UpdateFlyout(IView? flyout) => UpdateFlyoutContent();

		/// <summary>
		/// Refreshes the flyout items.
		/// </summary>
		public void UpdateItems()
		{
			if (_customFlyoutContent != null)
				return;

			if (Shell != null)
				UpdateFlyoutItems(Shell);
		}

		/// <summary>
		/// Updates the flyout items.
		/// </summary>
		public void UpdateFlyoutItems(Shell shell)
		{
			if (ShellController == null || MauiContext == null || _flyoutView == null)
				return;

			if (_customFlyoutContent is not null)
				return;

			UpdateFlyoutItemsCore(shell, HeaderOnMenu ? ShellController.FlyoutHeader : null);
		}

		void UpdateFlyoutItemsCore(Shell shell, XView? scrollingHeader)
		{
			if (ShellController == null || _flyoutView == null)
				return;

			var groups = ShellController.GenerateFlyoutGrouping();
			var items = new List<Element>();
			foreach (var group in groups)
			{
				items.AddRange(group);
			}

			if (_flyoutCollectionView == null)
			{
				_flyoutCollectionView = new TCollectionView
				{
					WidthSpecification = LayoutParamPolicies.MatchParent,
					HeightSpecification = LayoutParamPolicies.MatchParent,
					LayoutManager = new LinearLayoutManager(false),
					SelectionMode = CollectionViewSelectionMode.SingleAlways,
				};
				_flyoutCollectionView.ScrollView.HideScrollbar = true;
			}

			if (_flyoutAdaptor != null)
			{
				_flyoutCollectionView.Adaptor = null;
				_flyoutAdaptor.SelectionChanged -= OnFlyoutItemSelected;
				_flyoutAdaptor.Dispose();
			}

			_flyoutAdaptor = new TizenShellFlyoutItemAdaptor(shell, items, scrollingHeader)
			{
				ItemAppearance = _itemAppearance,
			};
			_flyoutAdaptor.SelectionChanged += OnFlyoutItemSelected;

			_flyoutCollectionView.Adaptor = _flyoutAdaptor;
			_flyoutView.Content = _flyoutCollectionView;
			SynchronizeFlyoutSelection();
		}

		void OnFlyoutItemSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (Shell == null || ShellController == null)
				return;

			var nativeIndex = e.SelectedIndexes.Count > 0 ? e.SelectedIndexes[0] : -1;
			if (_flyoutSelection.ConsumeManagedEcho(nativeIndex)
				|| e.SelectedItems == null
				|| e.SelectedItems.Count == 0)
				return;

			var selected = e.SelectedItems[0];
			if (selected is Element element)
			{
				_flyoutSelection.Propose(
					element,
					selected =>
					{
						ShellController.OnFlyoutItemSelected(selected);
						return true;
					},
					SynchronizeFlyoutSelection);
			}

			// Close the drawer after selection for flyout behavior
			if (Shell.FlyoutBehavior == FlyoutBehavior.Flyout)
			{
				_ = _navigationDrawer?.CloseAsync(true);
			}
		}

		void SynchronizeFlyoutSelection()
		{
			if (_flyoutCollectionView?.Adaptor is not { } adaptor || Shell is null)
				return;

			_flyoutSelection.Synchronize(
				Shell.CurrentItem,
				adaptor.GetItemIndex,
				() =>
				{
					foreach (var selected in _flyoutCollectionView.SelectedItems.ToArray())
						_flyoutCollectionView.RequestItemUnselect(selected);
				},
				_flyoutCollectionView.RequestItemSelect);
		}

		/// <summary>
		/// Updates the flyout behavior.
		/// </summary>
		public void UpdateFlyoutBehavior(FlyoutBehavior behavior)
		{
			if (_navigationDrawer == null || Shell == null)
				return;

			_navigationDrawer.DrawerBehavior = behavior.ToTizenDrawerBehavior();
			RefreshToolbarLeadingIcon();

			if (_navigationDrawer.DrawerBehavior == DrawerBehavior.Drawer)
				_ = _navigationDrawer.CloseAsync(false);
		}

		/// <summary>
		/// Updates the drawer width.
		/// </summary>
		public void UpdateDrawerWidth(double width)
		{
			if (_navigationDrawer == null)
				return;

			if (width >= 0)
			{
				_navigationDrawer.DrawerWidth = width.ToScaledPixel();
			}
		}

		/// <summary>
		/// Updates the flyout backdrop brush.
		/// </summary>
		public void UpdateFlyoutBackDrop(Brush? backdrop)
		{
			if (_navigationDrawer == null)
				return;

			if (_backdropView == null)
			{
				_backdropView = new TizenWrapperView()
				{
					WidthSpecification = LayoutParamPolicies.MatchParent,
					HeightSpecification = LayoutParamPolicies.MatchParent,
					BackgroundColor = DefaultBackdropColor.ToTizen().ToNative()
				};
				_navigationDrawer.Backdrop = _backdropView;
			}

			if (backdrop != null && !backdrop.IsEmpty)
				_backdropView.UpdateBackground(backdrop);
			else
				_backdropView.BackgroundColor = DefaultBackdropColor.ToTizen().ToNative();
		}

		/// <summary>
		/// Updates the background color.
		/// </summary>
		public void UpdateBackgroundColor(XColor? color)
		{
			if (_flyoutView == null)
				return;

			_flyoutView.BackgroundColor = (color ?? DefaultBackgroundColor).ToTizen().ToNative();
		}

		/// <summary>
		/// Updates the current shell item.
		/// </summary>
		public void UpdateCurrentItem(ShellItem? item)
		{
			if (MauiContext is null)
				return;

			CurrentShellItemView = _shellItemCache.SetCurrent(
				item,
				current =>
				{
					var handler = current.ToHandler(MauiContext);
					if (handler is not Handlers.TizenShellItemHandler shellItemHandler)
						throw new InvalidOperationException(
							$"The handler for {current.GetType().FullName} is not {nameof(Handlers.TizenShellItemHandler)}.");

					_shellItemHandlers[current] = shellItemHandler;
					return shellItemHandler.PlatformView;
				},
				_ => CurrentShellItemView = null);

			UpdateSearchHandler();
			SynchronizeFlyoutSelection();
		}

		/// <summary>
		/// Updates the search handler based on current content.
		/// </summary>
		public void UpdateSearchHandler()
		{
			if (Shell is null || MauiContext is null)
				return;

			var page = Shell.GetCurrentShellPage();
			if (!ReferenceEquals(_currentPage, page))
			{
				if (_currentPage is not null)
					_currentPage.PropertyChanged -= OnCurrentPagePropertyChanged;

				_currentPage = page;
				if (_currentPage is not null)
					_currentPage.PropertyChanged += OnCurrentPagePropertyChanged;

				RefreshSearchOwnerSubscriptions(_currentPage);
			}

			var searchHandler = Shell.GetEffectiveValue<SearchHandler?>(
				Shell.SearchHandlerProperty,
				defaultValue: null);

			if (!ReferenceEquals(_currentSearchHandler, searchHandler))
			{
				if (_currentSearchHandler is not null)
					_currentSearchHandler.PropertyChanged -= OnCurrentSearchHandlerPropertyChanged;
				_currentSearchHandler = searchHandler;
				if (_currentSearchHandler is not null)
					_currentSearchHandler.PropertyChanged += OnCurrentSearchHandlerPropertyChanged;
			}

			if (_currentSearchHandler is null)
			{
				if (_toolbarOwnership.Current is not null)
					_toolbarOwnership.Current.SearchBar = null;
				_searchView?.Dispose();
				_searchView = null;
				return;
			}

			_searchView ??= new TizenShellSearchView();
			_searchView.Bind(_currentSearchHandler, Shell, MauiContext);

			if (_toolbarOwnership.Current is not null)
			{
				_toolbarOwnership.Current.SearchBar =
					_currentSearchHandler.SearchBoxVisibility == SearchBoxVisibility.Hidden
						? null
						: _searchView;
			}
		}

		void OnCurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == Shell.SearchHandlerProperty.PropertyName)
				UpdateSearchHandler();
		}

		void OnCurrentSearchHandlerPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SearchHandler.SearchBoxVisibility))
				UpdateSearchHandler();
		}

		void OnSearchOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == Shell.SearchHandlerProperty.PropertyName)
				UpdateSearchHandler();
		}

		void RefreshSearchOwnerSubscriptions(Page? page)
		{
			foreach (var owner in _searchOwners)
				owner.PropertyChanged -= OnSearchOwnerPropertyChanged;
			_searchOwners.Clear();

			for (Element? owner = page?.Parent; owner is not null && !ReferenceEquals(owner, Shell); owner = owner.Parent)
			{
				owner.PropertyChanged += OnSearchOwnerPropertyChanged;
				_searchOwners.Add(owner);
			}
		}

		/// <summary>
		/// Updates the flyout header.
		/// </summary>
		public void UpdateFlyoutHeader(Shell shell)
		{
			if (_flyoutView == null || ShellController == null || MauiContext == null)
				return;

			var header = ShellController.FlyoutHeader;
			if (HeaderOnMenu)
			{
				SetFixedFlyoutView(ref _fixedHeader, null, value => _flyoutView.Header = value);
				UpdateFlyoutItemsCore(shell, header);
			}
			else
			{
				UpdateFlyoutItemsCore(shell, scrollingHeader: null);
				SetFixedFlyoutView(ref _fixedHeader, header, value => _flyoutView.Header = value);
			}
		}

		/// <summary>
		/// Updates the flyout footer.
		/// </summary>
		public void UpdateFlyoutFooter(Shell shell)
		{
			if (_flyoutView == null || ShellController == null || MauiContext == null)
				return;

			SetFixedFlyoutView(ref _fixedFooter, ShellController.FlyoutFooter, value => _flyoutView.Footer = value);
		}

		/// <summary>
		/// Updates the flyout content.
		/// </summary>
		public void UpdateFlyoutContent()
		{
			if (_flyoutView == null || ShellController == null || MauiContext == null || Shell is null)
				return;

			var content = ShellController.FlyoutContent;
			if (ReferenceEquals(_customFlyoutContent, content))
				return;

			if (_customFlyoutContent is not null)
			{
				_flyoutView.Content = null;
				(_customFlyoutContent.Handler as IDisposable)?.Dispose();
				_customFlyoutContent.Handler = null;
				_customFlyoutContent.Parent = null;
			}

			_customFlyoutContent = content;
			if (_customFlyoutContent is not null)
			{
				_customFlyoutContent.Parent = Shell;
				_flyoutView.Content = _customFlyoutContent.ToPlatformView(MauiContext);
				return;
			}

			UpdateFlyoutItems(Shell);
		}

		/// <summary>
		/// Updates the toolbar.
		/// </summary>
		public void UpdateToolbar()
		{
			if (Shell == null || MauiContext == null || _mainContentView == null)
				return;

			var toolbar = ShellElementTree.GetToolbar(Shell);

			if (toolbar == null)
			{
				DetachToolbar();
				return;
			}

			TizenToolbarView? platformToolbar = toolbar.Handler is ITizenPlatformViewHandler handler
				? handler.PlatformView as TizenToolbarView
				: (TizenToolbarView?)toolbar.ToPlatformView(MauiContext);

			if (platformToolbar is null)
			{
				throw new InvalidOperationException(
					$"The handler for {toolbar.GetType().FullName} did not produce a {nameof(TizenToolbarView)}.");
			}

			if (ReferenceEquals(_toolbarOwnership.Current, platformToolbar))
			{
				_mainContentView.SetToolbar(platformToolbar);
				UpdateSearchHandler();
				return;
			}

			// Unsubscribe before the container disposes the outgoing toolbar, transfer ownership,
			// then subscribe to the incoming instance.
			if (_toolbarOwnership.Current is not null)
				_toolbarOwnership.Current.SearchBar = null;
			_toolbarOwnership.Release();
			_mainContentView.SetToolbar(platformToolbar);
			_toolbarOwnership.Transfer(platformToolbar);
			UpdateSearchHandler();
			RefreshToolbarLeadingIcon();
		}

		public void DetachToolbar()
		{
			if (_toolbarOwnership.Current is not null)
				_toolbarOwnership.Current.SearchBar = null;

			_searchView?.Dispose();
			_searchView = null;
			if (_currentSearchHandler is not null)
				_currentSearchHandler.PropertyChanged -= OnCurrentSearchHandlerPropertyChanged;
			_currentSearchHandler = null;
			_toolbarOwnership.Release();
			_mainContentView?.ClearToolbar();
		}

		void SetFixedFlyoutView(ref XView? current, XView? next, Action<NView?> setNative)
		{
			if (ReferenceEquals(current, next))
				return;

			setNative(null);
			if (current is not null)
			{
				(current.Handler as IDisposable)?.Dispose();
				current.Handler = null;
				current.Parent = null;
			}

			current = next;
			if (current is not null && MauiContext is not null && Shell is not null)
			{
				current.Parent = Shell;
				setNative(current.ToPlatformView(MauiContext));
			}
		}

		/// <summary>
		/// Toggles the flyout when the press belongs to the drawer toggle.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="TizenToolbarHandler"/> subscribes to the same <c>IconPressed</c> event and pops
		/// the navigation stack when the back button owns the slot. Both subscriptions run on every
		/// press, so gating this one on <c>FlyoutBehavior == Flyout</c> alone made a back press
		/// toggle the drawer open <em>and</em> navigate back - the drawer is available in flyout mode
		/// whether or not it currently owns the slot.
		/// </para>
		/// <para>
		/// The two subscriptions are therefore split by slot ownership, which is the same
		/// back-precedence rule the icon itself is drawn from, so the press cannot be claimed twice.
		/// </para>
		/// </remarks>
		void OnToolbarIconPressed(object? sender, EventArgs e)
		{
			if (Shell is null || !ToolbarDrawerToggle.ShouldToggleDrawer(ShellElementTree.GetToolbar(Shell), Shell))
			{
				return;
			}

			Shell.FlyoutIsPresented = !Shell.FlyoutIsPresented;
		}

		/// <summary>
		/// Updates the toolbar colors.
		/// </summary>
		public void UpdateToolbarColors(XColor? foreground, XColor? background, XColor? title)
		{
			_toolbarOwnership.Current?.UpdateBarIconColor(foreground);
			_toolbarOwnership.Current?.UpdateBarBackgroundColor(background);
			_toolbarOwnership.Current?.UpdateBarTextColor(title);
		}

		/// <summary>
		/// Re-renders the toolbar's leading icon after a change that can affect the drawer toggle.
		/// </summary>
		/// <remarks>
		/// This used to WRITE a latched drawer-toggle flag onto the toolbar. The capability is
		/// read-only upstream (dotnet/maui#37863), so nothing is stored: the value is computed on
		/// read and this only asks the toolbar to redraw. That also removes the staleness the latch
		/// had, where a flyout-behaviour change that did not route through here left a stale icon.
		/// </remarks>
		void RefreshToolbarLeadingIcon()
		{
			if (Shell == null || MauiContext == null)
			{
				return;
			}

			if (ShellElementTree.GetToolbar(Shell) is Toolbar toolbar
				&& _toolbarOwnership.Current is { } platformToolbar)
			{
				if (toolbar.Handler is TizenToolbarHandler toolbarHandler)
					toolbarHandler.UpdateNavigationIcon(toolbar, Shell);
				else
					platformToolbar.UpdateBackButton(toolbar, ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, Shell));
			}
		}

		void UpdateAppearance(ShellAppearance? appearance)
		{
			if (_flyoutView == null || Shell == null)
				return;

			if (appearance is not null)
			{
				_itemAppearance.BackgroundColor = appearance.BackgroundColor;
				_itemAppearance.ForegroundColor = appearance.ForegroundColor;
				_itemAppearance.TitleColor = appearance.TitleColor;
				_itemAppearance.UnselectedColor = appearance.UnselectedColor;
				UpdateToolbarColors(
					appearance.ForegroundColor,
					appearance.BackgroundColor,
					appearance.TitleColor);
			}

			// Update flyout background from shell appearance
			var flyoutBg = ShellElementTree.GetEffectiveValue<XColor>(Shell, Shell.FlyoutBackgroundColorProperty, null);
			UpdateBackgroundColor(flyoutBg);
		}

		/// <inheritdoc/>
		// NUI's BaseHandle already exposes Dispose(); this participates in that chain rather
		// than shadowing it, which CS0108 would otherwise flag.
		public new void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Disposes resources.
		/// </summary>
		protected override void Dispose(DisposeTypes type)
		{
			if (_isDisposed)
				return;

			// DisposeTypes.Explicit is a real Dispose() call; Implicit is finalization, where
			// touching other managed objects is not safe.
			if (type == DisposeTypes.Explicit)
			{
				if (Shell != null)
				{
					((IShellController)Shell).RemoveAppearanceObserver(this);
					Shell.PropertyChanged -= OnShellPropertyChanged;
					if (Shell.Items is INotifyCollectionChanged items)
						items.CollectionChanged -= OnShellItemsChanged;
				}

				if (_currentPage is not null)
					_currentPage.PropertyChanged -= OnCurrentPagePropertyChanged;
				_currentPage = null;
				RefreshSearchOwnerSubscriptions(null);

				if (_navigationDrawer != null)
				{
					_navigationDrawer.Toggled -= OnDrawerToggled;
				}

				DetachToolbar();

				if (_flyoutView is not null)
				{
					SetFixedFlyoutView(ref _fixedHeader, null, value => _flyoutView.Header = value);
					SetFixedFlyoutView(ref _fixedFooter, null, value => _flyoutView.Footer = value);
				}

				if (_customFlyoutContent is not null)
				{
					(_customFlyoutContent.Handler as IDisposable)?.Dispose();
					_customFlyoutContent.Handler = null;
					_customFlyoutContent.Parent = null;
					_customFlyoutContent = null;
				}

				if (_flyoutAdaptor != null)
				{
					if (_flyoutCollectionView != null)
						_flyoutCollectionView.Adaptor = null;
					_flyoutAdaptor.SelectionChanged -= OnFlyoutItemSelected;
					_flyoutAdaptor.Dispose();
					_flyoutAdaptor = null;
				}

				if (_flyoutCollectionView != null)
				{
					_flyoutCollectionView.Dispose();
					_flyoutCollectionView = null;
				}

				_shellItemCache.Clear();
				foreach (var handler in _shellItemHandlers.Values)
					handler.Dispose();
				foreach (var item in _shellItemHandlers.Keys)
					item.Handler = null;
				_shellItemHandlers.Clear();
				_currentShellItemView = null;
			}

			_isDisposed = true;

			// The NUI base owns the native handle; skipping this leaks it regardless of what the
			// managed teardown above released.
			base.Dispose(type);
		}
	}
}
