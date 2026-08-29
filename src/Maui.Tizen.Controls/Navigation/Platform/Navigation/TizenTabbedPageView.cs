using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;

using GColor = Microsoft.Maui.Graphics.Color;
using GColors = Microsoft.Maui.Graphics.Colors;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NItemSizingStrategy = Tizen.UIExtensions.NUI.ItemSizingStrategy;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for TabbedPage on Tizen.
	/// </summary>
	/// <remarks>
	/// Consists of a tab bar (CollectionView) at the top and a content area for the current page.
	/// </remarks>
	public class TizenTabbedPageView : NView
	{
		TabbedPage _tabbedPage;
		NCollectionView _tabbedView;
		TizenTabbedPageAdaptor _adaptor;
		ViewGroup _content;
		readonly SelectionProposalCoordinator<Page> _selection = new();
		IMauiContext MauiContext => _tabbedPage.Handler?.MauiContext ?? throw new InvalidOperationException("MauiContext cannot be null here");
		bool _isDisconnected;
		bool _isDisposed;

		/// <summary>
		/// Initializes a new instance of <see cref="TizenTabbedPageView"/>.
		/// </summary>
		/// <param name="tabbedPage">The TabbedPage this view represents.</param>
		public TizenTabbedPageView(TabbedPage tabbedPage)
		{
			_tabbedPage = tabbedPage ?? throw new ArgumentNullException(nameof(tabbedPage));

			HeightSpecification = NLayoutParamPolicies.MatchParent;
			WidthSpecification = NLayoutParamPolicies.MatchParent;
			Layout = new LinearLayout
			{
				LinearOrientation = LinearLayout.Orientation.Vertical
			};

			_tabbedView = new NCollectionView
			{
				SizeHeight = 40d.ToScaledPixel(),
				WidthSpecification = NLayoutParamPolicies.MatchParent,
				LayoutManager = new GridLayoutManager(true, 1, NItemSizingStrategy.MeasureAllItems),
				SelectionMode = CollectionViewSelectionMode.SingleAlways,
			};
			// Use public Children instead of internal InternalChildren
			_tabbedView.Adaptor = _adaptor = new TizenTabbedPageAdaptor(tabbedPage);

			_content = new ViewGroup
			{
				WidthSpecification = NLayoutParamPolicies.MatchParent,
				HeightSpecification = NLayoutParamPolicies.MatchParent
			};

			Add(_tabbedView);
			Add(_content);
			_adaptor.SelectionChanged += OnTabItemSelected;

		}

		/// <summary>
		/// Gets the content container for the current page.
		/// </summary>
		public ViewGroup ContentContainer => _content;

		public void Rebind(TabbedPage tabbedPage)
		{
			ArgumentNullException.ThrowIfNull(tabbedPage);
			if (ReferenceEquals(_tabbedPage, tabbedPage))
				return;

			ReleasePageHandlers(_tabbedPage);
			_tabbedView.Adaptor = null;
			_adaptor.SelectionChanged -= OnTabItemSelected;
			_adaptor.Dispose();

			_tabbedPage = tabbedPage;
			_adaptor = new TizenTabbedPageAdaptor(tabbedPage);
			_adaptor.SelectionChanged += OnTabItemSelected;
			_tabbedView.Adaptor = _adaptor;
			UpdateCurrentPage();
		}

		/// <summary>
		/// Updates the current page display.
		/// </summary>
		public void UpdateCurrentPage()
		{
			if (_tabbedPage.CurrentPage == null)
			{
				_selection.Synchronize(
					null,
					_ => -1,
					() =>
					{
						foreach (var selected in _tabbedView.SelectedItems.ToArray())
							_tabbedView.RequestItemUnselect(selected);
					},
					_tabbedView.RequestItemSelect);

				if (_content.Children.FirstOrDefault() is { } old)
					_content.Remove(old);
				return;
			}

			// Sync the native tab selection to match the current page
			// Use public Children instead of internal InternalChildren
			var currentPageIndex = _tabbedPage.Children.IndexOf(_tabbedPage.CurrentPage);
			if (currentPageIndex != -1)
			{
				_selection.Synchronize(
					_tabbedPage.CurrentPage,
					page => _tabbedPage.Children.IndexOf(page),
					() =>
					{
						foreach (var selected in _tabbedView.SelectedItems.ToArray())
							_tabbedView.RequestItemUnselect(selected);
					},
					_tabbedView.RequestItemSelect);
			}

			try
			{
				var currentHandler = _tabbedPage.CurrentPage.ToHandler(MauiContext);
				if (currentHandler != null && currentHandler.PlatformView is NView current)
				{
					current.WidthSpecification = NLayoutParamPolicies.MatchParent;
					current.HeightSpecification = NLayoutParamPolicies.MatchParent;
					var old = _content.Children.FirstOrDefault();
					if (current != old)
					{
						if (old != null)
						{
							_content.Remove(old);
						}
						_content.Add(current);
					}
				}
			}
			catch (InvalidOperationException)
			{
				// MauiContext not available yet, will be updated when connected
			}
		}

		void OnTabItemSelected(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			var nativeIndex = e.SelectedIndexes.Count > 0 ? e.SelectedIndexes[0] : -1;
			if (_selection.ConsumeManagedEcho(nativeIndex)
				|| e.SelectedItems is null
				|| e.SelectedItems.Count == 0)
				return;

			Page? current = e.SelectedItems[0] as Page;
			if (current is not null && _tabbedPage.CurrentPage != current)
			{
				_selection.Propose(
					current,
					page =>
					{
						_tabbedPage.CurrentPage = page;
						return true;
					},
					UpdateCurrentPage);
			}
		}

		/// <summary>
		/// Disconnects and disposes platform resources.
		/// </summary>
		public void DisconnectHandler()
		{
			if (_isDisconnected)
				return;

			_isDisconnected = true;

			_adaptor.SelectionChanged -= OnTabItemSelected;
			_tabbedView.Adaptor = null;
			_adaptor.Dispose();

			ReleasePageHandlers(_tabbedPage);

			_tabbedView.Dispose();
			_content.Dispose();
		}

		void ReleasePageHandlers(TabbedPage tabbedPage)
		{
			foreach (var child in tabbedPage.Children)
			{
				var handler = child.Handler;
				if (handler is null)
					continue;

				if (handler.PlatformView is NView native && ReferenceEquals(native.GetParent(), _content))
					_content.Remove(native);

				if (handler is IDisposable disposable)
					disposable.Dispose();
				else
					handler.DisconnectHandler();

				if (ReferenceEquals(child.Handler, handler))
					child.Handler = null;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (!_isDisposed)
			{
				if (disposing)
				{
					DisconnectHandler();
				}
				_isDisposed = true;
			}
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Item adaptor for TabbedPage tabs.
	/// </summary>
	internal class TizenTabbedPageAdaptor : TizenItemTemplateAdaptor
	{
		/// <summary>
		/// Initializes a new instance of <see cref="TizenTabbedPageAdaptor"/>.
		/// </summary>
		/// <param name="page">The TabbedPage to adapt.</param>
		public TizenTabbedPageAdaptor(TabbedPage page)
			// Use public Children instead of internal InternalChildren
			: base(page, page.Children, GetTemplate(page))
		{
		}

		/// <inheritdoc/>
		protected override bool IsSelectable => true;

		static DataTemplate GetTemplate(TabbedPage page)
		{
			return new DataTemplate(() =>
			{
				return new TizenTabbedItem(page);
			});
		}
	}

#pragma warning disable CS0618 // Type or member is obsolete - Frame is obsolete but needed for layout
	/// <summary>
	/// Visual representation of a tab item in TabbedPage.
	/// </summary>
	internal class TizenTabbedItem : Frame
#pragma warning restore CS0618
	{
		// Use plain bool values instead of internal BooleanBoxes
		static readonly BindableProperty SelectedStateProperty = BindableProperty.Create(
			nameof(IsSelected),
			typeof(bool),
			typeof(TizenTabbedItem),
			false,
			propertyChanged: (b, o, n) => ((TizenTabbedItem)b).UpdateSelectedState());

		static readonly BindableProperty SelectedTabColorProperty = BindableProperty.Create(
			nameof(SelectedTabColor),
			typeof(GColor),
			typeof(TizenTabbedItem),
			default(GColor),
			propertyChanged: (b, o, n) => ((TizenTabbedItem)b).UpdateSelectedState());

		static readonly BindableProperty UnselectedTabColorProperty = BindableProperty.Create(
			nameof(UnselectedTabColor),
			typeof(GColor),
			typeof(TizenTabbedItem),
			default(GColor),
			propertyChanged: (b, o, n) => ((TizenTabbedItem)b).UpdateSelectedState());

		readonly TabbedPage _page;
		BoxView _bar;

		/// <summary>
		/// Gets or sets whether this tab is selected.
		/// </summary>
		public bool IsSelected
		{
			// Use plain bool values instead of internal BooleanBoxes
			get => (bool)GetValue(SelectedStateProperty);
			set => SetValue(SelectedStateProperty, value);
		}

		/// <summary>
		/// Gets or sets the selected tab color.
		/// </summary>
		public GColor SelectedTabColor
		{
			get => (GColor)GetValue(SelectedTabColorProperty);
			set => SetValue(SelectedTabColorProperty, value);
		}

		/// <summary>
		/// Gets or sets the unselected tab color.
		/// </summary>
		public GColor UnselectedTabColor
		{
			get => (GColor)GetValue(UnselectedTabColorProperty);
			set => SetValue(UnselectedTabColorProperty, value);
		}

#pragma warning disable CS8618 // _bar initialized in InitializeComponent
		/// <summary>
		/// Initializes a new instance of <see cref="TizenTabbedItem"/>.
		/// </summary>
		/// <param name="page">The parent TabbedPage.</param>
		public TizenTabbedItem(TabbedPage page)
#pragma warning restore CS8618
		{
			_page = page;
			InitializeComponent();
		}

		void InitializeComponent()
		{
			Padding = new Thickness(0);
			HasShadow = false;
			BorderColor = GColors.DarkGray;
			this.SetBinding(BackgroundProperty, static (TabbedPage page) => page.BarBackground, source: _page);
			this.SetBinding(BackgroundColorProperty, static (TabbedPage page) => page.BarBackgroundColor, source: _page);
			this.SetBinding(SelectedTabColorProperty, static (TabbedPage page) => page.SelectedTabColor, source: _page);
			this.SetBinding(UnselectedTabColorProperty, static (TabbedPage page) => page.UnselectedTabColor, source: _page);

			var label = new XLabel
			{
				Margin = new Thickness(20, 0),
				FontSize = 16,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
			};
			// Bind to the child Page's Title via BindingContext (not the parent TabbedPage)
			label.SetBinding(XLabel.TextProperty, static (Page page) => page.Title);
			label.SetBinding(XLabel.TextColorProperty, static (TabbedPage page) => page.BarTextColor, source: _page);

			_bar = new BoxView
			{
				Color = GColors.Transparent,
			};

			var grid = new Grid
			{
				RowDefinitions =
				{
					new RowDefinition
					{
						Height = GridLength.Star,
					},
					new RowDefinition
					{
						Height = 5,
					}
				}
			};
			grid.Add(label, 0, 0);
			grid.Add(_bar, 0, 1);
			Content = grid;

			var groups = new VisualStateGroupList();

			VisualStateGroup group = new VisualStateGroup()
			{
				Name = "CommonStates",
			};

			VisualState selected = new VisualState()
			{
				Name = VisualStateManager.CommonStates.Selected,
				TargetType = typeof(TizenTabbedItem),
				Setters =
				{
					new Setter
					{
						Property = SelectedStateProperty,
						Value = true,
					},
				},
			};

			VisualState normal = new VisualState()
			{
				Name = VisualStateManager.CommonStates.Normal,
				TargetType = typeof(TizenTabbedItem),
				Setters =
				{
					new Setter
					{
						Property = SelectedStateProperty,
						Value = false,
					},
				}
			};
			group.States.Add(normal);
			group.States.Add(selected);
			groups.Add(group);
			VisualStateManager.SetVisualStateGroups(this, groups);
		}

		void UpdateSelectedState()
		{
			if (IsSelected)
			{
				_bar.Color = _page.SelectedTabColor.IsNotDefault() ? _page.SelectedTabColor : GColors.DarkGray;
			}
			else
			{
				_bar.Color = _page.UnselectedTabColor.IsNotDefault() ? _page.UnselectedTabColor : GColors.Transparent;
			}
		}
	}
}
