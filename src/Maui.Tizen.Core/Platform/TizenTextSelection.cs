// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Translates a native text selection into MAUI's cursor-and-length model.
	/// </summary>
	/// <remarks>
	/// Split out of the handlers because the translation is pure arithmetic with no NUI
	/// dependency, which is what lets the host-side tests execute it. The handlers keep only the
	/// event subscriptions, which genuinely cannot run off-device.
	/// </remarks>
	public static class TizenTextSelection
	{
		/// <summary>
		/// Normalises a native selection range.
		/// </summary>
		/// <remarks>
		/// NUI reports a selection as an ordered pair of offsets, and the pair runs backwards when
		/// the user drags right to left. MAUI models the same selection as a start plus a
		/// non-negative length. Passing NUI's raw pair through would give MAUI a negative length
		/// for a perfectly ordinary backwards drag.
		/// </remarks>
		/// <param name="start">The native selection start offset.</param>
		/// <param name="end">The native selection end offset.</param>
		/// <returns>The cursor position and selection length to publish.</returns>
		public static (int CursorPosition, int SelectionLength) Normalize(int start, int end) =>
			(Math.Min(start, end), Math.Abs(end - start));

		/// <summary>
		/// Applies a native selection to the cross-platform text input.
		/// </summary>
		/// <remarks>
		/// Order matters. Setting the length first can be clamped against the old cursor position,
		/// so the cursor is moved first and the length applied against the new position.
		/// </remarks>
		/// <param name="input">The cross-platform text input.</param>
		/// <param name="start">The native selection start offset.</param>
		/// <param name="end">The native selection end offset.</param>
		public static void ApplySelection(this ITextInput input, int start, int end)
		{
			ArgumentNullException.ThrowIfNull(input);

			var (cursor, length) = Normalize(start, end);

			input.CursorPosition = cursor;
			input.SelectionLength = length;
		}

		/// <summary>
		/// Collapses the selection to a caret at <paramref name="cursorPosition"/>.
		/// </summary>
		/// <param name="input">The cross-platform text input.</param>
		/// <param name="cursorPosition">The native primary cursor position.</param>
		public static void ApplyCaret(this ITextInput input, int cursorPosition)
		{
			ArgumentNullException.ThrowIfNull(input);

			input.SelectionLength = 0;
			input.CursorPosition = cursorPosition;
		}
	}
}
