// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IEditor</c> property mappings.
	/// </summary>
	public static class TizenEditorExtensions
	{
		/// <remarks>See <see cref="TizenEntryExtensions.UpdateText"/> for why this is guarded.</remarks>
		public static void UpdateText(this Editor platformEditor, IText editor)
		{
			var text = editor.Text ?? string.Empty;

			if (platformEditor.Text != text)
				platformEditor.Text = text;
		}

		public static void UpdateTextColor(this Editor platformEditor, ITextStyle editor) =>
			platformEditor.TextColor = editor.TextColor.ToTizenCommonColor();

		public static void UpdateHorizontalTextAlignment(this Editor platformEditor, ITextAlignment editor) =>
			platformEditor.HorizontalTextAlignment = editor.HorizontalTextAlignment.ToTizenTextAlignment();

		/// <remarks>
		/// A multi-line editor has no vertical text alignment property in NUI, so this maps onto
		/// the control's own vertical alignment instead. Note that upstream reads
		/// <c>HorizontalTextAlignment</c> here; that is a bug, and this reads
		/// <see cref="ITextAlignment.VerticalTextAlignment"/> as the property name implies.
		/// </remarks>
		public static void UpdateVerticalTextAlignment(this Editor platformEditor, ITextAlignment editor) =>
			platformEditor.VerticalAlignment = editor.VerticalTextAlignment.ToTizenVerticalAlignment();

		public static void UpdatePlaceholder(this Editor platformEditor, ITextInput editor) =>
			platformEditor.Placeholder = editor.Placeholder ?? string.Empty;

		public static void UpdatePlaceholderColor(this Editor platformEditor, ITextInput editor) =>
			platformEditor.PlaceholderColor = editor.PlaceholderColor.ToTizenCommonColor();

		public static void UpdateIsReadOnly(this Editor platformEditor, ITextInput editor) =>
			platformEditor.IsReadOnly = editor.IsReadOnly;

		public static void UpdateIsTextPredictionEnabled(this Editor platformEditor, ITextInput editor) =>
			platformEditor.IsTextPredictionEnabled = editor.IsTextPredictionEnabled;

		/// <remarks>See <see cref="TizenEntryExtensions.UpdateMaxLength"/>.</remarks>
		public static void UpdateMaxLength(this Editor platformEditor, ITextInput editor)
		{
			var maxLength = editor.MaxLength < 0 ? int.MaxValue : editor.MaxLength;
			platformEditor.MaxLength = maxLength;

			if (maxLength != int.MaxValue && platformEditor.Text is { Length: > 0 } text && text.Length > maxLength)
				platformEditor.Text = text[..maxLength];
		}

		public static void UpdateKeyboard(this Editor platformEditor, ITextInput editor) =>
			platformEditor.Keyboard = editor.Keyboard.ToTizenKeyboard();

		public static void UpdateCharacterSpacing(this Editor platformEditor, ITextStyle editor) =>
			platformEditor.CharacterSpacing = editor.CharacterSpacing.ToScaledPixel();

		/// <remarks>See <see cref="TizenEntryExtensions.UpdateCursorPosition"/>.</remarks>
		public static void UpdateCursorPosition(this Editor platformEditor, ITextInput editor) =>
			platformEditor.PrimaryCursorPosition = ClampToText(platformEditor, editor.CursorPosition);

		/// <remarks>See <see cref="TizenEntryExtensions.UpdateSelectionLength"/>.</remarks>
		public static void UpdateSelectionLength(this Editor platformEditor, ITextInput editor)
		{
			if (editor.SelectionLength == 0)
			{
				platformEditor.SelectNone();
				return;
			}

			var start = ClampToText(platformEditor, editor.CursorPosition);
			var end = ClampToText(platformEditor, editor.CursorPosition + editor.SelectionLength);

			if (start == end)
				platformEditor.SelectNone();
			else
				platformEditor.SelectText(start, end);
		}

		/// <summary>Not supported on global::Tizen.</summary>
		/// <remarks>See <see cref="TizenEntryExtensions.UpdateIsSpellCheckEnabled"/>.</remarks>
		public static void UpdateIsSpellCheckEnabled(this Editor platformEditor, ITextInput editor)
		{
		}

		static int ClampToText(Editor platformEditor, int position)
		{
			var length = platformEditor.Text?.Length ?? 0;
			return Math.Clamp(position, 0, length);
		}
	}
}
