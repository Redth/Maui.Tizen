using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;
using XImage = Microsoft.Maui.Controls.Image;
using XColor = Microsoft.Maui.Graphics.Color;

#pragma warning disable CS0618 // Frame is obsolete but still the upstream layout

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// View for a single item in the Shell flyout menu.
	/// </summary>
	public class TizenShellFlyoutItemView : NView
	{
		static readonly XColor s_defaultBackgroundColor = XColor.FromRgb(33, 150, 243);

		public TizenShellFlyoutItemView()
		{
			Layout = new global::Tizen.NUI.LinearLayout();
		}

		public static View GetFlyoutItemView(object data, IMauiContext context)
		{
			var frame = new Frame
			{
				BackgroundColor = s_defaultBackgroundColor,
				CornerRadius = 10,
				HasShadow = false,
				HeightRequest = 50,
				Margin = new Thickness(10),
				Padding = new Thickness(5),
			};

			var contentLayout = new StackLayout
			{
				Orientation = StackOrientation.Horizontal,
				Padding = new Thickness(5),
				Spacing = 5,
				VerticalOptions = LayoutOptions.Center,
			};

			contentLayout.Children.Add(new XImage
			{
				Aspect = Aspect.AspectFit,
				HorizontalOptions = LayoutOptions.Start,
				HeightRequest = 20,
				VerticalOptions = LayoutOptions.Center,
				WidthRequest = 20,
			}.BindTo(data, TizenShellFlyoutItemView.GetIcon, BindableProperty.Create("Icon", typeof(ImageSource), typeof(XImage)), "Icon", XImage.SourceProperty));

			contentLayout.Children.Add(new XLabel
			{
				FontSize = 14,
				HorizontalOptions = LayoutOptions.StartAndExpand,
				VerticalOptions = LayoutOptions.Center,
			}.BindTo(data, TizenShellFlyoutItemView.GetTitle, BindableProperty.Create("Title", typeof(string), typeof(XLabel)), "Title", XLabel.TextProperty));

			frame.Content = contentLayout;

			var view = frame;
			view.SetBinding(View.BackgroundColorProperty, static (TizenItemAppearance app) => app.BackgroundColor);
			return view;
		}

		internal static object GetTitle(object data)
		{
			if (data is BindableObject bo)
			{
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
