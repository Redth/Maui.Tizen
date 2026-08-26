// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI.GraphicsView;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>ICheckBox</c> property mappings.
	/// </summary>
	public static class TizenCheckBoxExtensions
	{
		public static void UpdateIsChecked(this CheckBox platformCheckBox, ICheckBox check) =>
			platformCheckBox.IsChecked = check.IsChecked;

		/// <remarks>
		/// The check glyph is drawn by a Skia drawable that takes a single colour, so only a
		/// solid paint can be honoured. A gradient or image foreground is ignored rather than
		/// approximated to an arbitrary colour.
		/// </remarks>
		public static void UpdateForeground(this CheckBox platformCheckBox, ICheckBox check)
		{
			if (check.Foreground is SolidPaint solid)
				platformCheckBox.Color = solid.Color.ToTizenCommonColor();
		}
	}
}
