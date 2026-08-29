using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;
using GColor = Microsoft.Maui.Graphics.Color;
using GColors = Microsoft.Maui.Graphics.Colors;

#pragma warning disable CS0618 // Frame is obsolete but still the upstream layout

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// View for a single item in the Shell's top tab bar (ShellContent tab).
	/// This is a MAUI View with VisualStateGroups for selection state.
	/// </summary>
	internal class TizenShellContentItemView : Frame
	{
		static readonly BindableProperty SelectedStateProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(TizenShellContentItemView), false, propertyChanged: (b, o, n) => ((TizenShellContentItemView)b).UpdateViewColors());
		internal static readonly BindableProperty SelectedTextColorProperty = BindableProperty.Create(nameof(SelectedTextColor), typeof(GColor), typeof(TizenShellContentItemView), null, propertyChanged: (b, o, n) => ((TizenShellContentItemView)b).UpdateViewColors());
		internal static readonly BindableProperty SelectedBarColorProperty = BindableProperty.Create(nameof(SelectedBarColor), typeof(GColor), typeof(TizenShellContentItemView), null, propertyChanged: (b, o, n) => ((TizenShellContentItemView)b).UpdateViewColors());
		internal static readonly BindableProperty UnselectedColorProperty = BindableProperty.Create(nameof(UnselectedColor), typeof(GColor), typeof(TizenShellContentItemView), null, propertyChanged: (b, o, n) => ((TizenShellContentItemView)b).UpdateViewColors());

		XLabel _label;
		BoxView _bar;

		public bool IsSelected
		{
			get => (bool)GetValue(SelectedStateProperty);
			set => SetValue(SelectedStateProperty, value);
		}

		public GColor? SelectedTextColor
		{
			get => (GColor?)GetValue(SelectedTextColorProperty);
			set => SetValue(SelectedTextColorProperty, value);
		}

		public GColor? SelectedBarColor
		{
			get => (GColor?)GetValue(SelectedBarColorProperty);
			set => SetValue(SelectedBarColorProperty, value);
		}

		public GColor? UnselectedColor
		{
			get => (GColor?)GetValue(UnselectedColorProperty);
			set => SetValue(UnselectedColorProperty, value);
		}

#pragma warning disable CS8618
		public TizenShellContentItemView()
#pragma warning restore CS8618
		{
			InitializeComponent();
		}

		void InitializeComponent()
		{
			Padding = new Thickness(0);
			HasShadow = false;
			BorderColor = GColors.Transparent;
			BackgroundColor = GColors.Transparent;

			_label = new XLabel
			{
				Margin = new Thickness(20, 0),
				FontSize = 16,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
			};

			_bar = new BoxView
			{
				Color = GColors.Transparent,
			};

			var grid = new Grid
			{
				RowDefinitions =
				{
					new RowDefinition { Height = GridLength.Star },
					new RowDefinition { Height = 5 },
				},
			};
			grid.Add(_label, 0, 0);
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
				TargetType = typeof(TizenShellContentItemView),
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
				TargetType = typeof(TizenShellContentItemView),
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

		void UpdateViewColors()
		{
			var textColor = IsSelected ? (SelectedTextColor ?? GColors.Black) : (UnselectedColor ?? GColors.Gray);
			_label.TextColor = textColor;

			var barColor = IsSelected ? (SelectedBarColor ?? GColors.Blue) : GColors.Transparent;
			_bar.Color = barColor;
		}

		/// <summary>
		/// Sets up data bindings from a ShellContent.
		/// </summary>
		public void BindToData(object data)
		{
			if (data is ShellContent content)
			{
				_label.SetBinding(XLabel.TextProperty, new Binding(nameof(BaseShellItem.Title)));
				SetBinding(IsEnabledProperty, new Binding(nameof(BaseShellItem.IsEnabled)));
			}
		}

		/// <summary>
		/// Factory method to create a content item view for the adaptor.
		/// </summary>
		public static View GetContentItemView(object data, IMauiContext context, TizenItemAppearance? appearance = null)
		{
			var view = new TizenShellContentItemView();
			view.BindToData(data);
			if (appearance is not null)
			{
				view.SetBinding(SelectedTextColorProperty, new Binding(nameof(TizenItemAppearance.TitleColor), source: appearance));
				view.SetBinding(SelectedBarColorProperty, new Binding(nameof(TizenItemAppearance.ForegroundColor), source: appearance));
				view.SetBinding(UnselectedColorProperty, new Binding(nameof(TizenItemAppearance.UnselectedColor), source: appearance));
			}
			return view;
		}
	}
}

#pragma warning restore CS0618
