using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using XLabel = Microsoft.Maui.Controls.Label;

#pragma warning disable CS0618 // Frame is obsolete but still the upstream layout

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// View for a single item in the Shell's top tab bar (ShellContent tab).
	/// </summary>
	public class TizenShellContentItemView : NView
	{
		static readonly Color s_defaultBackgroundColor = Colors.Transparent;

		public TizenShellContentItemView()
		{
			Layout = new global::Tizen.NUI.LinearLayout();
		}

		public static View GetContentItemView(object data, IMauiContext context)
		{
			var frame = new Frame
			{
				BackgroundColor = s_defaultBackgroundColor,
				CornerRadius = 0,
				HasShadow = false,
				HeightRequest = 40,
				Margin = new Thickness(0),
				Padding = new Thickness(0),
			};

			var label = new XLabel
			{
				FontSize = 14,
				HorizontalOptions = LayoutOptions.Center,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalOptions = LayoutOptions.Center,
			}.BindTo(data, TizenShellFlyoutItemView.GetTitle, BindableProperty.Create("Title", typeof(string), typeof(XLabel)), "Title", XLabel.TextProperty);

			frame.Content = label;

			var view = frame;
			return view;
		}
	}
}

#pragma warning restore CS0618
