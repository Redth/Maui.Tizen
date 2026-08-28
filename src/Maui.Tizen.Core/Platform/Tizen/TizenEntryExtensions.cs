// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IEntry</c> and <c>ITextInput</c> property mappings.
	/// </summary>
	public static class TizenEntryExtensions
	{
		/// <remarks>
		/// Guarded against a redundant write: assigning <c>Text</c> resets the cursor and clears
		/// the selection, so echoing the value the control already has would fight the user's
		/// caret while typing.
		/// </remarks>
		public static void UpdateText(this Entry platformEntry, IText entry)
		{
			var text = entry.Text ?? string.Empty;

			if (platformEntry.Text != text)
				platformEntry.Text = text;
		}

		public static void UpdateTextColor(this Entry platformEntry, ITextStyle entry) =>
			platformEntry.TextColor = entry.TextColor.ToTizenCommonColor();

		public static void UpdateHorizontalTextAlignment(this Entry platformEntry, ITextAlignment entry) =>
			platformEntry.HorizontalTextAlignment = entry.HorizontalTextAlignment.ToTizenTextAlignment();

		public static void UpdateVerticalTextAlignment(this Entry platformEntry, ITextAlignment entry) =>
			platformEntry.VerticalTextAlignment = entry.VerticalTextAlignment.ToTizenTextAlignment();

		/// <remarks>
		/// Toggling <c>IsPassword</c> does not re-render the existing text in NUI, so the value
		/// is re-assigned to force the control to redraw with (or without) the mask.
		/// </remarks>
		public static void UpdateIsPassword(this Entry platformEntry, IEntry entry)
		{
			platformEntry.IsPassword = entry.IsPassword;
			platformEntry.Text = platformEntry.Text;
		}

		public static void UpdateReturnType(this Entry platformEntry, IEntry entry) =>
			platformEntry.UpdateReturnType(entry.ReturnType);

		/// <summary>
		/// Applies a return key type.
		/// </summary>
		/// <remarks>
		/// Takes the enum rather than an <see cref="IEntry"/> so <c>ISearchBar</c>, which
		/// carries a <c>ReturnType</c> without being an entry, can use the same mapping.
		/// </remarks>
		public static void UpdateReturnType(this Entry platformEntry, ReturnType returnType) =>
			platformEntry.ReturnType = returnType.ToTizenReturnType();

		public static void UpdatePlaceholder(this Entry platformEntry, ITextInput entry) =>
			platformEntry.Placeholder = entry.Placeholder ?? string.Empty;

		public static void UpdatePlaceholder(this Entry platformEntry, string? placeholder) =>
			platformEntry.Placeholder = placeholder ?? string.Empty;

		public static void UpdatePlaceholderColor(this Entry platformEntry, ITextInput entry) =>
			platformEntry.PlaceholderColor = entry.PlaceholderColor.ToTizenCommonColor();

		public static void UpdatePlaceholderColor(this Entry platformEntry, Graphics.Color? color) =>
			platformEntry.PlaceholderColor = color is null
				? global::Tizen.UIExtensions.Common.Color.Default
				: color.ToTizenCommonColor();

		public static void UpdateIsReadOnly(this Entry platformEntry, ITextInput entry) =>
			platformEntry.IsReadOnly = entry.IsReadOnly;

		public static void UpdateIsTextPredictionEnabled(this Entry platformEntry, ITextInput entry) =>
			platformEntry.IsTextPredictionEnabled = entry.IsTextPredictionEnabled;

		/// <remarks>
		/// MAUI uses <see cref="int.MaxValue"/> for "unlimited"; NUI treats <c>MaxLength</c>
		/// literally, and a negative value would reject all input, so the value is clamped to a
		/// non-negative range.
		/// </remarks>
		public static void UpdateMaxLength(this Entry platformEntry, ITextInput entry)
		{
			var maxLength = entry.MaxLength < 0 ? int.MaxValue : entry.MaxLength;
			platformEntry.MaxLength = maxLength;

			if (maxLength != int.MaxValue && platformEntry.Text is { Length: > 0 } text && text.Length > maxLength)
				platformEntry.Text = text[..maxLength];
		}

		public static void UpdateKeyboard(this Entry platformEntry, ITextInput entry) =>
			platformEntry.Keyboard = entry.Keyboard.ToTizenKeyboard();

		/// <remarks>
		/// The requested position is clamped to the current text length. MAUI can push a cursor
		/// position that was valid for the previous text before the new text has been applied.
		/// </remarks>
		public static void UpdateCursorPosition(this Entry platformEntry, ITextInput entry) =>
			platformEntry.PrimaryCursorPosition = ClampToText(platformEntry, entry.CursorPosition);

		/// <remarks>
		/// A zero-length selection is a caret, not an empty highlight, so it is expressed with
		/// <c>SelectNone</c>. Both ends are clamped so a stale selection cannot address text
		/// that no longer exists.
		/// </remarks>
		public static void UpdateSelectionLength(this Entry platformEntry, ITextInput entry)
		{
			if (entry.SelectionLength == 0)
			{
				platformEntry.SelectNone();
				return;
			}

			var start = ClampToText(platformEntry, entry.CursorPosition);
			var end = ClampToText(platformEntry, entry.CursorPosition + entry.SelectionLength);

			if (start == end)
				platformEntry.SelectNone();
			else
				platformEntry.SelectText(start, end);
		}

		public static void UpdateCharacterSpacing(this Entry platformEntry, ITextStyle entry) =>
			platformEntry.CharacterSpacing = entry.CharacterSpacing.ToScaledPixel();

		/// <summary>Not supported on global::Tizen.</summary>
		/// <remarks>
		/// Tizen's IME does not expose a spell-check toggle independent of text prediction.
		/// Deliberate no-op.
		/// </remarks>
		public static void UpdateIsSpellCheckEnabled(this Entry platformEntry, ITextInput entry)
		{
		}

		/// <summary>Not supported on global::Tizen.</summary>
		/// <remarks>
		/// NUI's entry has no built-in clear affordance and MAUI provides no drawing surface
		/// inside the control to add one. Deliberate no-op.
		/// </remarks>
		public static void UpdateClearButtonVisibility(this Entry platformEntry, IEntry entry)
		{
		}

		static int ClampToText(Entry platformEntry, int position)
		{
			var length = platformEntry.Text?.Length ?? 0;
			return Math.Clamp(position, 0, length);
		}
	}
}
