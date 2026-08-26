using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;
using NColor = Tizen.NUI.Color;
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
		MauiToolbar? _toolbar;
		TizenShellSearchView? _searchView;
		WrapperView? _backdropView;
		bool _isDisposed;
		bool _isOpen;
		ShellAppearance? _appearance;

		static readonly XColor DefaultBackgroundColor = new XColor(1f, 1f, 1f, 1f);
		static readonly XColor DefaultBackdropColor = new XColor(0.2f, 0.2f, 0.2f, 0.2f);

		/// <summary>
		/// Creates a new shell view. Call <see cref="SetElement"/> to initialize.
		/// </summary>
		public TizenShellView()
		{
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
			Shell = shell ?? throw new ArgumentNullException(nameof(shell));
			MauiContext = context ?? throw new ArgumentNullException(nameof(context));

			_navigationDrawer = CreateNavigationDrawer();

			_flyoutView = CreateNavigationView();
			_navigationDrawer.Drawer = _flyoutView.TargetView;

			_mainContentView = CreateNavigationContentView();
			_navigationDrawer.Content = _mainContentView.TargetView;

			_navigationDrawer.Toggled += OnDrawerToggled;

			Add((NView)_navigationDrawer);

			// Subscribe to appearance changes - IAppearanceObserver is public
			((IShellController)Shell).AddAppearanceObserver(this, Shell);
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

		/// <summary>
		/// Called when appearance changes.
		/// </summary>
		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			_appearance = appearance;
			UpdateAppearance(appearance);
		}

		IView? _customFlyoutView;

		/// <summary>
		/// Updates the custom flyout view.
		/// </summary>
		public void UpdateFlyout(IView? flyout)
		{
			_customFlyoutView = flyout;

			if (_customFlyoutView != null && _flyoutView != null && MauiContext != null)
			{
				_flyoutView.Content = _customFlyoutView.ToPlatform(MauiContext);
			}
		}

		/// <summary>
		/// Refreshes the flyout items.
		/// </summary>
		public void UpdateItems()
		{
			if (_customFlyoutView != null)
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
				_flyoutAdaptor.SelectionChanged -= OnFlyoutItemSelected;

			_flyoutAdaptor = new TizenShellFlyoutItemAdaptor(shell, items);
			_flyoutAdaptor.SelectionChanged += OnFlyoutItemSelected;

			_flyoutCollectionView.Adaptor = _flyoutAdaptor;
			_flyoutView.Content = _flyoutCollectionView;
		}

		void OnFlyoutItemSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			if (Shell == null || ShellController == null)
				return;

			if (e.SelectedItems == null || e.SelectedItems.Count == 0)
				return;

			var selected = e.SelectedItems[0];
			if (selected is Element element)
			{
				ShellController.OnFlyoutItemSelected(element);
			}

			// Close the drawer after selection for flyout behavior
			if (Shell.FlyoutBehavior == FlyoutBehavior.Flyout)
			{
				_ = _navigationDrawer?.CloseAsync(true);
			}
		}

		/// <summary>
		/// Updates the flyout behavior.
		/// </summary>
		public void UpdateFlyoutBehavior(FlyoutBehavior behavior)
		{
			if (_navigationDrawer == null || Shell == null)
				return;

			_navigationDrawer.DrawerBehavior = behavior.ToPlatform();
			UpdateDrawerToggleVisible();

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
				_backdropView = new WrapperView()
				{
					WidthSpecification = LayoutParamPolicies.MatchParent,
					HeightSpecification = LayoutParamPolicies.MatchParent,
					BackgroundColor = DefaultBackdropColor.ToNUIColor()
				};
				_navigationDrawer.Backdrop = _backdropView;
			}

			if (backdrop != null && !backdrop.IsEmpty)
				_backdropView.UpdateBackground(backdrop);
		}

		/// <summary>
		/// Updates the background color.
		/// </summary>
		public void UpdateBackgroundColor(XColor? color)
		{
			if (_flyoutView == null)
				return;

			if (color != null)
			{
				_flyoutView.BackgroundColor = color.ToNUIColor();
			}
		}

		/// <summary>
		/// Updates the current shell item.
		/// </summary>
		public void UpdateCurrentItem(ShellItem? item)
		{
			// Handler handles this through ShellItem handler dispatch
		}

		/// <summary>
		/// Updates the flyout header.
		/// </summary>
		public void UpdateFlyoutHeader(Shell shell)
		{
			if (_flyoutView == null || _flyoutAdaptor == null)
				return;

			_flyoutView.Header = _flyoutAdaptor.GetHeaderView();
		}

		/// <summary>
		/// Updates the flyout footer.
		/// </summary>
		public void UpdateFlyoutFooter(Shell shell)
		{
			if (_flyoutView == null || _flyoutAdaptor == null)
				return;

			_flyoutView.Footer = _flyoutAdaptor.GetFooterView();
		}

		/// <summary>
		/// Updates the flyout content.
		/// </summary>
		public void UpdateFlyoutContent(object? content)
		{
			// Custom flyout content replaces the default items view
			if (content == null || _flyoutView == null || MauiContext == null)
				return;

			if (content is Microsoft.Maui.Controls.View view)
			{
				_flyoutView.Content = view.ToPlatform(MauiContext);
			}
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
				_mainContentView.TitleView = null;
				_toolbar = null;
				return;
			}

			if (toolbar.Handler is not IPlatformViewHandler handler)
			{
				_toolbar = (MauiToolbar?)toolbar.ToPlatform(MauiContext);
			}
			else
			{
				_toolbar = handler.PlatformView as MauiToolbar;
			}

			_mainContentView.TitleView = _toolbar;

			// Wire up icon press to toggle flyout
			if (_toolbar != null)
			{
				_toolbar.IconPressed -= OnToolbarIconPressed;
				_toolbar.IconPressed += OnToolbarIconPressed;
			}
		}

		void OnToolbarIconPressed(object? sender, EventArgs e)
		{
			if (Shell?.FlyoutBehavior == FlyoutBehavior.Flyout)
			{
				Shell.FlyoutIsPresented = !Shell.FlyoutIsPresented;
			}
		}

		/// <summary>
		/// Updates the toolbar colors.
		/// </summary>
		public void UpdateToolbarColors(XColor? foreground, XColor? background, XColor? title)
		{
			// Toolbar colors are handled via appearance - no direct implementation needed
			// since appearance observer notifies updates
		}

		void UpdateDrawerToggleVisible()
		{
			if (Shell == null)
				return;

			var toolbar = ShellElementTree.GetToolbar(Shell);
			if (toolbar != null)
			{
				var visible = Shell.FlyoutBehavior == FlyoutBehavior.Flyout;
				ToolbarDrawerToggle.SetDrawerToggleVisible(toolbar, visible);
			}
		}

		void UpdateAppearance(ShellAppearance? appearance)
		{
			if (_flyoutView == null || Shell == null)
				return;

			// Update flyout background from shell appearance
			var flyoutBg = ShellElementTree.GetEffectiveValue<XColor>(Shell, Shell.FlyoutBackgroundColorProperty, null);
			if (flyoutBg != null)
			{
				_flyoutView.BackgroundColor = flyoutBg.ToNUIColor();
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Disposes resources.
		/// </summary>
		protected virtual void Dispose(bool disposing)
		{
			if (_isDisposed)
				return;

			if (disposing)
			{
				if (Shell != null)
				{
					((IShellController)Shell).RemoveAppearanceObserver(this);
				}

				if (_navigationDrawer != null)
				{
					_navigationDrawer.Toggled -= OnDrawerToggled;
				}

				if (_toolbar != null)
				{
					_toolbar.IconPressed -= OnToolbarIconPressed;
				}

				if (_flyoutCollectionView != null)
				{
					_flyoutCollectionView.Dispose();
					_flyoutCollectionView = null;
				}

				if (_flyoutAdaptor != null)
				{
					_flyoutAdaptor.SelectionChanged -= OnFlyoutItemSelected;
					_flyoutAdaptor.Dispose();
					_flyoutAdaptor = null;
				}

				_searchView?.Dispose();
				_currentShellItemView?.Dispose();
			}

			_isDisposed = true;
		}
	}
}
