// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using Tizen.UIExtensions.NUI;
using TKeyboard = Tizen.UIExtensions.Common.Keyboard;
using TReturnType = Tizen.UIExtensions.Common.ReturnType;
using TTextAlignment = Tizen.UIExtensions.Common.TextAlignment;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Text conversions shared by every text-bearing Tizen handler.
	/// </summary>
	public static class TizenTextExtensions
	{
		/// <summary>The NUI default text size, in points, when a font size is unset.</summary>
		internal const double DefaultEntryFontSize = 25d;

		/// <summary>The NUI default button text size, in points, when a font size is unset.</summary>
		internal const double DefaultButtonFontSize = 14d;

		/// <summary>
		/// Maps MAUI's horizontal text alignment onto Tizen's.
		/// </summary>
		/// <remarks>
		/// <see cref="TextAlignment.Start"/> and <see cref="TextAlignment.End"/> are resolved
		/// against the platform locale by Tizen, so right-to-left languages align correctly
		/// without the backend having to invert anything.
		/// </remarks>
		public static TTextAlignment ToTizenTextAlignment(this TextAlignment alignment) => alignment switch
		{
			TextAlignment.Start => TTextAlignment.Start,
			TextAlignment.Center => TTextAlignment.Center,
			TextAlignment.End => TTextAlignment.End,
			_ => TTextAlignment.Auto,
		};

		/// <summary>
		/// Maps MAUI's vertical text alignment onto NUI's.
		/// </summary>
		public static global::Tizen.NUI.VerticalAlignment ToTizenVerticalAlignment(this TextAlignment alignment) => alignment switch
		{
			TextAlignment.Start => global::Tizen.NUI.VerticalAlignment.Top,
			TextAlignment.Center => global::Tizen.NUI.VerticalAlignment.Center,
			TextAlignment.End => global::Tizen.NUI.VerticalAlignment.Bottom,
			_ => global::Tizen.NUI.VerticalAlignment.Center,
		};

		/// <summary>
		/// Maps MAUI's return key type onto Tizen's.
		/// </summary>
		/// <exception cref="NotSupportedException">
		/// Thrown for a value Tizen has no key for. This is deliberately not silently defaulted:
		/// a return key that does the wrong thing is a functional bug, and MAUI's enum is closed,
		/// so an unmatched value means the enum grew and this mapping needs revisiting.
		/// </exception>
		public static TReturnType ToTizenReturnType(this ReturnType returnType) => returnType switch
		{
			ReturnType.Go => TReturnType.Go,
			ReturnType.Next => TReturnType.Next,
			ReturnType.Send => TReturnType.Send,
			ReturnType.Search => TReturnType.Search,
			ReturnType.Done => TReturnType.Done,
			ReturnType.Default => TReturnType.Default,
			_ => throw new NotSupportedException($"ReturnType '{returnType}' has no Tizen equivalent."),
		};

		/// <summary>
		/// Maps MAUI's keyboard kind onto Tizen's IME layout.
		/// </summary>
		/// <remarks>
		/// <see cref="Keyboard.Date"/> and <see cref="Keyboard.Time"/> both map to Tizen's single
		/// combined date/time layout; Tizen has no separate one for each. Anything unrecognised
		/// falls back to the normal layout rather than throwing - an unexpected keyboard hint
		/// should not make a text field unusable.
		/// </remarks>
		public static TKeyboard ToTizenKeyboard(this Keyboard keyboard)
		{
			if (keyboard == Keyboard.Numeric)
				return TKeyboard.Numeric;
			if (keyboard == Keyboard.Telephone)
				return TKeyboard.PhoneNumber;
			if (keyboard == Keyboard.Email)
				return TKeyboard.Email;
			if (keyboard == Keyboard.Url)
				return TKeyboard.Url;
			if (keyboard == Keyboard.Date || keyboard == Keyboard.Time)
				return TKeyboard.DateTime;
			if (keyboard == Keyboard.Password)
				return TKeyboard.Password;

			return TKeyboard.Normal;
		}

		/// <summary>
		/// Resolves the point size to apply for a text style, falling back to a platform default.
		/// </summary>
		/// <remarks>
		/// MAUI reports an unset font size as 0 (or NaN), which NUI would render as invisible
		/// text rather than as "use the default".
		/// </remarks>
		internal static double ResolveFontSize(this ITextStyle textStyle, double defaultSize) =>
			textStyle.Font.Size > 0 && !double.IsNaN(textStyle.Font.Size)
				? textStyle.Font.Size.ToScaledPoint()
				: defaultSize.ToScaledPoint();

		/// <summary>
		/// Resolves the font family for <paramref name="textStyle"/>, tolerating a font manager
		/// that does not implement the Tizen contract.
		/// </summary>
		/// <remarks>
		/// A host may register MAUI's own <c>IFontManager</c>; falling back to the raw family
		/// name keeps text rendering rather than throwing from inside a property mapper.
		/// </remarks>
		public static string GetTizenFontFamily(this IFontManager? fontManager, ITextStyle textStyle) =>
			fontManager is ITizenFontManager tizenFontManager
				? tizenFontManager.GetFontFamily(textStyle.Font.Family) ?? string.Empty
				: textStyle.Font.Family ?? string.Empty;

		/// <summary>
		/// Applies font family, size, weight and slant to a NUI entry.
		/// </summary>
		public static void UpdateTizenFont(this Entry platformEntry, ITextStyle textStyle, IFontManager? fontManager)
		{
			platformEntry.FontSize = textStyle.ResolveFontSize(DefaultEntryFontSize);
			platformEntry.FontAttributes = textStyle.Font.GetTizenFontAttributes();
			platformEntry.FontFamily = fontManager.GetTizenFontFamily(textStyle);
		}

		/// <summary>
		/// Applies font family, size, weight and slant to a NUI editor.
		/// </summary>
		public static void UpdateTizenFont(this Editor platformEditor, ITextStyle textStyle, IFontManager? fontManager)
		{
			platformEditor.FontSize = textStyle.ResolveFontSize(DefaultEntryFontSize);
			platformEditor.FontAttributes = textStyle.Font.GetTizenFontAttributes();
			platformEditor.FontFamily = fontManager.GetTizenFontFamily(textStyle);
		}

		/// <summary>
		/// Applies font family, size, weight and slant to a NUI button.
		/// </summary>
		public static void UpdateTizenFont(this Button platformButton, ITextStyle textStyle, IFontManager? fontManager)
		{
			platformButton.FontSize = textStyle.ResolveFontSize(DefaultButtonFontSize);
			platformButton.FontAttributes = textStyle.Font.GetTizenFontAttributes();
			platformButton.FontFamily = fontManager.GetTizenFontFamily(textStyle);
		}
	}
}
