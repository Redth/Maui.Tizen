using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
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
	/// View for a single item in the Shell's bottom tab bar (ShellItem tab).
	/// This is a MAUI View with VisualStateGroups for selection state.
	/// </summary>
	public class TizenShellSectionItemView : Frame
	{
		static readonly BindableProperty SelectedStateProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(TizenShellSectionItemView), false, propertyChanged: (b, o, n) => ((TizenShellSectionItemView)b).UpdateViewColors());
		internal static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(nameof(SelectedColor), typeof(GColor), typeof(TizenShellSectionItemView), null, propertyChanged: (b, o, n) => ((TizenShellSectionItemView)b).UpdateViewColors());
		internal static readonly BindableProperty UnselectedColorProperty = BindableProperty.Create(nameof(UnselectedColor), typeof(GColor), typeof(TizenShellSectionItemView), null, propertyChanged: (b, o, n) => ((TizenShellSectionItemView)b).UpdateViewColors());

		XLabel _label;
		View _icon;
		bool _isMoreItem;

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

#pragma warning disable CS8618
		public TizenShellSectionItemView(bool isMoreItem = false)
#pragma warning restore CS8618
		{
			_isMoreItem = isMoreItem;
			InitializeComponent();
		}

		void InitializeComponent()
		{
			Padding = new Thickness(0);
			HasShadow = false;
			BorderColor = GColors.Transparent;
			BackgroundColor = GColors.Transparent;

			var grid = new Grid
			{
				RowSpacing = 0,
				RowDefinitions =
				{
					new RowDefinition { Height = 55 },
					new RowDefinition { Height = 20 },
				},
			};

			grid.Add(CreateIconView(), 0, 0);
			grid.Add(CreateTextView(), 0, 1);

			var groups = new VisualStateGroupList();

			VisualStateGroup group = new VisualStateGroup()
			{
				Name = "CommonStates",
			};

			VisualState selected = new VisualState()
			{
				Name = VisualStateManager.CommonStates.Selected,
				TargetType = typeof(TizenShellSectionItemView),
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
				TargetType = typeof(TizenShellSectionItemView),
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

			Content = grid;
		}

		View CreateIconView()
		{
			if (_isMoreItem)
			{
				_icon = new Ellipse
				{
					Fill = new SolidColorBrush(GColors.Gray),
					WidthRequest = 6,
					HeightRequest = 6,
					HorizontalOptions = LayoutOptions.Center,
					VerticalOptions = LayoutOptions.End,
					Margin = new Thickness(0, 0, 0, 3),
				};
			}
			else
			{
				_icon = new XImage
				{
					Aspect = Aspect.AspectFit,
					HeightRequest = 30,
					HorizontalOptions = LayoutOptions.Center,
					VerticalOptions = LayoutOptions.Center,
					WidthRequest = 30,
				};
			}
			return _icon;
		}

		View CreateTextView()
		{
			_label = new XLabel
			{
				FontSize = 12,
				HorizontalOptions = LayoutOptions.Center,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalOptions = LayoutOptions.Center,
			};
			return _label;
		}

		void UpdateViewColors()
		{
			var color = IsSelected ? (SelectedColor ?? GColors.Black) : (UnselectedColor ?? GColors.Gray);
			_label.TextColor = color;

			if (_icon is XImage image)
			{
				// For images, we would tint, but MAUI doesn't have built-in tinting
				// so we leave as is for now
			}
			else if (_icon is Ellipse ellipse)
			{
				ellipse.Fill = new SolidColorBrush(color);
			}
		}

		/// <summary>
		/// Sets up data bindings from a ShellSection or MoreItem.
		/// </summary>
		public void BindToData(object data)
		{
			if (data is ShellSection section)
			{
				if (_icon is XImage image)
				{
					image.SetBinding(XImage.SourceProperty, new Binding(nameof(BaseShellItem.Icon), source: section));
				}
				_label.SetBinding(XLabel.TextProperty, new Binding(nameof(BaseShellItem.Title), source: section));
			}
			else if (data is TizenMoreItem)
			{
				_label.Text = "More";
			}
		}

		/// <summary>
		/// Factory method to create a section item view for the adaptor.
		/// </summary>
		public static View GetSectionItemView(object data, IMauiContext context)
		{
			bool isMoreItem = data is TizenMoreItem;
			var view = new TizenShellSectionItemView(isMoreItem);
			view.BindToData(data);
			return view;
		}
	}
}

#pragma warning restore CS0618
