using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;
using XImage = Microsoft.Maui.Controls.Image;

#pragma warning disable CS0618 // Frame is obsolete but still the upstream layout

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// View for a single item in the Shell's bottom tab bar (ShellItem tab).
	/// </summary>
	public class TizenShellSectionItemView : NView
	{
		static readonly Color s_defaultBackgroundColor = Colors.Transparent;

		public TizenShellSectionItemView()
		{
			Layout = new global::Tizen.NUI.LinearLayout();
		}

		public static View GetSectionItemView(object data, IMauiContext context)
		{
			var frame = new Frame
			{
				BackgroundColor = s_defaultBackgroundColor,
				CornerRadius = 0,
				HasShadow = false,
				HeightRequest = 80,
				Margin = new Thickness(0),
				Padding = new Thickness(0),
			};

			var contentLayout = new StackLayout
			{
				HorizontalOptions = LayoutOptions.Center,
				Orientation = StackOrientation.Vertical,
				Padding = new Thickness(5),
				Spacing = 2,
				VerticalOptions = LayoutOptions.Center,
			};

			contentLayout.Children.Add(new XImage
			{
				Aspect = Aspect.AspectFit,
				HeightRequest = 30,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
				WidthRequest = 30,
			}.BindTo(data, TizenShellFlyoutItemView.GetIcon, BindableProperty.Create("Icon", typeof(ImageSource), typeof(XImage)), "Icon", XImage.SourceProperty));

			contentLayout.Children.Add(new XLabel
			{
				FontSize = 12,
				HorizontalOptions = LayoutOptions.Center,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalOptions = LayoutOptions.Center,
			}.BindTo(data, TizenShellFlyoutItemView.GetTitle, BindableProperty.Create("Title", typeof(string), typeof(XLabel)), "Title", XLabel.TextProperty));

			frame.Content = contentLayout;

			var view = frame;
			return view;
		}
	}
}

#pragma warning restore CS0618
