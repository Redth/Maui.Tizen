// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IPicker</c> property mappings.
	/// </summary>
	/// <remarks>
	/// Tizen has no drop-down control, so a picker is presented as a read-only entry that opens
	/// an action sheet. The entry's placeholder therefore doubles as the picker's title.
	/// </remarks>
	public static class TizenPickerExtensions
	{
		public static void UpdateTitle(this Entry platformPicker, IPicker picker) =>
			platformPicker.UpdatePlaceholder(picker.Title);

		public static void UpdateTitleColor(this Entry platformPicker, IPicker picker) =>
			platformPicker.UpdatePlaceholderColor(picker.TitleColor);

		public static void UpdateSelectedIndex(this Entry platformPicker, IPicker picker) =>
			platformPicker.UpdatePicker(picker);

		/// <summary>
		/// Rewrites the displayed text from the picker's current selection.
		/// </summary>
		/// <remarks>
		/// The index is bounds-checked against the live item count: MAUI can report a selected
		/// index that the item list no longer contains when items are replaced, and reading past
		/// the end would throw from inside a property mapper.
		/// </remarks>
		public static void UpdatePicker(this Entry platformPicker, IPicker picker)
		{
			var index = picker.SelectedIndex;

			platformPicker.Text = index < 0 || index >= picker.GetCount()
				? string.Empty
				: picker.GetItem(index) ?? string.Empty;
		}
	}
}
