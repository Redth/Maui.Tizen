using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;
using XImage = Microsoft.Maui.Controls.Image;
using GColor = Microsoft.Maui.Graphics.Color;
using GColors = Microsoft.Maui.Graphics.Colors;

#pragma warning disable CS0618 // Frame is obsolete but still the upstream layout

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// View for a single item in the Shell flyout menu.
	/// This is a MAUI View with VisualStateGroups for selection state.
	/// </summary>
	internal class TizenShellFlyoutItemView : Frame
	{
		static readonly BindableProperty SelectedStateProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(TizenShellFlyoutItemView), false, propertyChanged: (b, o, n) => ((TizenShellFlyoutItemView)b).UpdateSelectedState());
		static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(nameof(SelectedColor), typeof(GColor), typeof(TizenShellFlyoutItemView), null, propertyChanged: (b, o, n) => ((TizenShellFlyoutItemView)b).UpdateSelectedState());
		static readonly BindableProperty UnselectedColorProperty = BindableProperty.Create(nameof(UnselectedColor), typeof(GColor), typeof(TizenShellFlyoutItemView), null, propertyChanged: (b, o, n) => ((TizenShellFlyoutItemView)b).UpdateSelectedState());
		static readonly BindableProperty SelectedTextColorProperty = BindableProperty.Create(nameof(SelectedTextColor), typeof(GColor), typeof(TizenShellFlyoutItemView), null, propertyChanged: (b, o, n) => ((TizenShellFlyoutItemView)b).UpdateSelectedState());
		static readonly BindableProperty UnselectedTextColorProperty = BindableProperty.Create(nameof(UnselectedTextColor), typeof(GColor), typeof(TizenShellFlyoutItemView), null, propertyChanged: (b, o, n) => ((TizenShellFlyoutItemView)b).UpdateSelectedState());

		static readonly GColor s_defaultBackgroundColor = GColor.FromRgb(33, 150, 243);
		static readonly GColor s_selectedBackgroundColor = GColor.FromRgb(21, 101, 192);

		Grid _grid;
		XLabel _label;

		public bool IsSelected
		{
			get => (bool)GetValue(SelectedStateProperty);
			set => SetValue(SelectedStateProperty, value);
		}

		public GColor? SelectedColor
		{
			get => (GColor?)GetValue(SelectedColorProperty);
			set => SetValue(SelectedColorProperty, value);
		}

		public GColor? UnselectedColor
		{
			get => (GColor?)GetValue(UnselectedColorProperty);
			set => SetValue(UnselectedColorProperty, value);
		}

		public GColor? SelectedTextColor
		{
			get => (GColor?)GetValue(SelectedTextColorProperty);
			set => SetValue(SelectedTextColorProperty, value);
		}

		public GColor? UnselectedTextColor
		{
			get => (GColor?)GetValue(UnselectedTextColorProperty);
			set => SetValue(UnselectedTextColorProperty, value);
		}

#pragma warning disable CS8618
		public TizenShellFlyoutItemView()
#pragma warning restore CS8618
		{
			InitializeComponent();
		}

		void InitializeComponent()
		{
			Padding = new Thickness(0);
			HasShadow = false;
			BackgroundColor = s_defaultBackgroundColor;
			CornerRadius = 10;
			HeightRequest = 50;
			Margin = new Thickness(10);

			var icon = new XImage
			{
				Margin = new Thickness(10, 0),
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
			};

			_label = new XLabel
			{
				Margin = new Thickness(15, 15),
				FontSize = 16,
				VerticalTextAlignment = TextAlignment.Center,
			};

			_grid = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition { Width = 50 },
					new ColumnDefinition { Width = GridLength.Star },
				},
				HeightRequest = 50,
			};
			_grid.Add(icon, 0, 0);
			_grid.Add(_label, 1, 0);

			Content = _grid;

			var groups = new VisualStateGroupList();

			VisualStateGroup group = new VisualStateGroup()
			{
				Name = "CommonStates",
			};

			VisualState selected = new VisualState()
			{
				Name = VisualStateManager.CommonStates.Selected,
				TargetType = typeof(TizenShellFlyoutItemView),
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
				TargetType = typeof(TizenShellFlyoutItemView),
				Setters =
				{
					new Setter
					{
						Property = SelectedStateProperty,
						Value = false,
					},
				},
			};

			group.States.Add(selected);
			group.States.Add(normal);
			groups.Add(group);

			VisualStateManager.SetVisualStateGroups(this, groups);
		}

		void UpdateSelectedState()
		{
			BackgroundColor = IsSelected
				? SelectedColor ?? s_selectedBackgroundColor
				: UnselectedColor ?? s_defaultBackgroundColor;
			_label.TextColor = IsSelected
				? SelectedTextColor ?? GColors.White
				: UnselectedTextColor ?? GColors.White;
		}

		/// <summary>
		/// Sets up data bindings from a flyout item.
		/// </summary>
		public void BindToData(object data)
		{
			if (_grid.Children.Count >= 2)
			{
				var icon = _grid.Children[0] as XImage;
				var label = _grid.Children[1] as XLabel;

				if (data is BindableObject bo)
				{
					if (icon != null)
					{
						if (data is IMenuItemController)
						{
							icon.SetBinding(XImage.SourceProperty, nameof(MenuItem.IconImageSource));
						}
						else
						{
							icon.SetBinding(XImage.SourceProperty, nameof(BaseShellItem.Icon));
						}
					}

					if (label != null)
					{
						if (data is IMenuItemController menuItem)
						{
							label.SetBinding(XLabel.TextProperty, nameof(MenuItem.Text));
						}
						else
						{
							label.SetBinding(XLabel.TextProperty, nameof(BaseShellItem.Title));
						}
					}

					SetBinding(
						IsEnabledProperty,
						new Binding(data is IMenuItemController
							? nameof(MenuItem.IsEnabled)
							: nameof(BaseShellItem.IsEnabled)));
				}
			}
		}

		public static View GetFlyoutItemView(object data, IMauiContext context, TizenItemAppearance? appearance = null)
		{
			var view = new TizenShellFlyoutItemView();
			view.BindToData(data);
			if (appearance != null)
			{
				view.SetBinding(SelectedColorProperty, new Binding(nameof(TizenItemAppearance.ForegroundColor), source: appearance));
				view.SetBinding(UnselectedColorProperty, new Binding(nameof(TizenItemAppearance.BackgroundColor), source: appearance));
				view.SetBinding(SelectedTextColorProperty, new Binding(nameof(TizenItemAppearance.TitleColor), source: appearance));
				view.SetBinding(UnselectedTextColorProperty, new Binding(nameof(TizenItemAppearance.UnselectedColor), source: appearance));
			}
			return view;
		}

		internal static object GetTitle(object data)
		{
			if (data is BindableObject bo)
			{
				if (data is IMenuItemController)
				{
					return (string?)bo.GetValue(MenuItem.TextProperty) ?? string.Empty;
				}
				return (string?)bo.GetValue(BaseShellItem.TitleProperty) ?? string.Empty;
			}
			return string.Empty;
		}

		internal static object GetIcon(object data)
		{
			if (data is BindableObject bo)
			{
				if (data is IMenuItemController)
				{
					// MenuItem uses ImageSource directly, not Icon
					return (ImageSource?)bo.GetValue(MenuItem.IconImageSourceProperty) ?? ImageSource.FromFile("");
				}
				return (ImageSource?)bo.GetValue(BaseShellItem.IconProperty) ?? ImageSource.FromFile("");
			}
			return ImageSource.FromFile("");
		}
	}

	/// <summary>
	/// Binding helper extension.
	/// </summary>
	internal static class ShellViewBindingExtensions
	{
		internal static View BindTo(this View view, object data, System.Func<object, object> getter, BindableProperty srcProp, string path, BindableProperty dstProp)
		{
			if (data is BindableObject source)
			{
				view.SetBinding(dstProp, new Binding(path, source: source));
			}
			else
			{
				view.SetValue(dstProp, getter(data));
			}
			return view;
		}
	}
}

#pragma warning restore CS0618
