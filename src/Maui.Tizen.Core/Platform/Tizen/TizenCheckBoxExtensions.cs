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

		/// <summary>
		/// Applies the check colour, restoring the themed default when unset or unsupported.
		/// </summary>
		/// <remarks>
		/// The check glyph is drawn by a Skia drawable that takes a single colour, so only a solid
		/// paint can be honoured. Previously a null or non-solid foreground was simply skipped,
		/// which left whatever colour was last applied - so clearing a foreground, or switching
		/// from a solid to a gradient, kept a stale colour. The captured default is restored
		/// instead, which is both correct and visibly distinguishable from "unchanged".
		/// </remarks>
		public static void UpdateForeground(this TizenCheckBoxView platformCheckBox, ICheckBox check)
		{
			platformCheckBox.Color = check.Foreground is SolidPaint solid
				? solid.Color.ToTizenCommonColor()
				: platformCheckBox.DefaultColor;
		}
	}
}
