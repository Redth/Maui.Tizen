// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.NUI.Components;
using Tizen.UIExtensions.NUI;
using NButton = Tizen.UIExtensions.NUI.Button;
using NView = Tizen.NUI.BaseComponents.View;
using TColor = Tizen.UIExtensions.Common.Color;
using TFontAttributes = Tizen.UIExtensions.Common.FontAttributes;
using TTextAlignment = Tizen.UIExtensions.Common.TextAlignment;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The modal date or time picker dialog.
	/// </summary>
	/// <remarks>
	/// Tizen's <c>DatePicker</c>/<c>TimePicker</c> are inline spinners with no dialog chrome of
	/// their own, so the surrounding card, title and Cancel/OK buttons are built here.
	/// </remarks>
	public class TizenDateTimePicker : Popup<DateTime>
	{
		const double OuterMarginDp = 20;
		const double InnerMarginDp = 10;
		const double CornerRadiusDp = 8;
		const double TitleTextSizeDp = 21;
		const double ButtonTextSizeDp = 15;
		const double ButtonPaddingDp = 15;

		/// <summary>Fraction of the window width the dialog occupies in portrait.</summary>
		const float PortraitWidthRatio = 0.8f;

		/// <summary>Fraction of the window width the dialog occupies in landscape.</summary>
		const float LandscapeWidthRatio = 0.5f;

		readonly DateTime _initialValue;
		readonly bool _isTimePicker;
		readonly DateTime _minimum;
		readonly DateTime _maximum;

		/// <param name="value">The value the dialog opens on.</param>
		/// <param name="isTimePicker">
		/// <see langword="true"/> for a time dialog, <see langword="false"/> for a date dialog.
		/// </param>
		/// <param name="minimum">Earliest selectable value, for a date dialog.</param>
		/// <param name="maximum">Latest selectable value, for a date dialog.</param>
		public TizenDateTimePicker(DateTime value, bool isTimePicker, DateTime? minimum = null, DateTime? maximum = null)
		{
			_isTimePicker = isTimePicker;
			_minimum = minimum ?? DateTime.MinValue;
			_maximum = maximum ?? DateTime.MaxValue;
			_initialValue = Clamp(value);
		}

		DateTime Clamp(DateTime value)
		{
			if (_maximum < _minimum)
				return value;

			return value < _minimum ? _minimum : value > _maximum ? _maximum : value;
		}

		protected override NView CreateContent()
		{
			Layout = new LinearLayout
			{
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center,
			};

			// A translucent scrim so the dialog reads as modal.
			BackgroundColor = new global::Tizen.NUI.Color(0.1f, 0.1f, 0.1f, 0.5f);

			var outerMargin = (ushort)OuterMarginDp.ToScaledPixel();
			var innerMargin = (ushort)InnerMarginDp.ToScaledPixel();

			var content = new NView
			{
				CornerRadius = CornerRadiusDp.ToScaledPixel(),
				BoxShadow = new Shadow(20d.ToScaledPixel(), TColor.Black.ToNative()),
				Layout = new LinearLayout
				{
					VerticalAlignment = VerticalAlignment.Center,
					HorizontalAlignment = HorizontalAlignment.Center,
					LinearOrientation = LinearLayout.Orientation.Vertical
				},
				SizeWidth = DialogWidth(),
				BackgroundColor = global::Tizen.NUI.Color.White,
			};

			var title = new Label
			{
				Margin = new Extents(outerMargin, outerMargin, outerMargin, innerMargin),
				HorizontalTextAlignment = TTextAlignment.Start,
				WidthSpecification = LayoutParamPolicies.MatchParent,
				VerticalTextAlignment = TTextAlignment.Center,
				FontAttributes = TFontAttributes.Bold,
				TextColor = TColor.FromHex("#000000"),
				PixelSize = TitleTextSizeDp.ToScaledPixel(),
				Text = _isTimePicker ? "Set Time" : "Set Date",
			};
			content.Add(title);

			Control dateTimePicker = _isTimePicker
				? new global::Tizen.NUI.Components.TimePicker { Time = _initialValue }
				: new global::Tizen.NUI.Components.DatePicker { Date = _initialValue };

			dateTimePicker.Layout = new LinearLayout
			{
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			dateTimePicker.Margin = new Extents(outerMargin, outerMargin, 0, 0);
			dateTimePicker.HeightSpecification = LayoutParamPolicies.WrapContent;
			dateTimePicker.SizeWidth = Window.Default.WindowSize.Width * PortraitWidthRatio;

			content.Add(dateTimePicker);

			var buttonRow = new NView
			{
				Layout = new LinearLayout
				{
					VerticalAlignment = VerticalAlignment.Center,
					HorizontalAlignment = HorizontalAlignment.End,
					LinearOrientation = LinearLayout.Orientation.Horizontal
				},
				Margin = new Extents(outerMargin, outerMargin, innerMargin, outerMargin),
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.WrapContent
			};
			content.Add(buttonRow);

			var cancelButton = CreateDialogButton("Cancel");
			cancelButton.Clicked += (_, _) => Close();
			buttonRow.Add(cancelButton);

			var okButton = CreateDialogButton("OK");
			okButton.Margin = new Extents(innerMargin, 0, 0, 0);
			okButton.Clicked += (_, _) =>
			{
				var selected = dateTimePicker switch
				{
					global::Tizen.NUI.Components.TimePicker timePicker => timePicker.Time,
					global::Tizen.NUI.Components.DatePicker datePicker => datePicker.Date,
					_ => _initialValue,
				};

				// Tizen's spinner cannot express a valid range, so the limits are re-applied
				// to whatever the user landed on.
				SendSubmit(Clamp(selected));
			};
			buttonRow.Add(okButton);

			// The dialog is sized as a fraction of the window, so it has to be re-measured when
			// the device rotates.
			Relayout += (_, _) => content.SizeWidth = DialogWidth();

			return content;
		}

		static float DialogWidth()
		{
			var windowSize = Window.Default.WindowSize;
			var isLandscape = windowSize.Width > windowSize.Height;
			return windowSize.Width * (isLandscape ? LandscapeWidthRatio : PortraitWidthRatio);
		}

		static NButton CreateDialogButton(string text)
		{
			var button = new NButton
			{
				Text = text,
				TextColor = TColor.Black,
				BackgroundColor = TColor.Transparent.ToNative(),
			};

			button.TextLabel.PixelSize = ButtonTextSizeDp.ToScaledPixel();

			// NUI does not size a transparent button to its text, so do it explicitly.
			button.SizeWidth = button.TextLabel.NaturalSize.Width + (ButtonPaddingDp.ToScaledPixel() * 2);

			return button;
		}
	}
}
