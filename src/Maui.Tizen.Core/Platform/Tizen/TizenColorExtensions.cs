// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Graphics;
using NColor = Tizen.NUI.Color;
using TColor = Tizen.UIExtensions.Common.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Colour conversions for the Wave A controls.
	/// </summary>
	/// <remarks>
	/// Tizen has two colour types and they are not interchangeable: <c>Tizen.NUI.Color</c> is what
	/// NUI view properties take, while <c>Tizen.UIExtensions.Common.Color</c> is what the
	/// Skia-drawn <c>GraphicsView</c> controls (check box, switch, slider, progress bar, activity
	/// indicator) take. <see cref="TizenPlatformExtensions.ToTizen"/> covers the first; this covers
	/// the second, and adds the null handling MAUI's optional colour properties need.
	/// </remarks>
	public static class TizenColorExtensions
	{
		/// <summary>
		/// Converts to the colour type the Skia-drawn Tizen controls use.
		/// </summary>
		/// <remarks>
		/// A null MAUI colour means "unset", which maps to <c>Color.Default</c> - the drawable
		/// then picks its own themed colour instead of rendering transparent.
		/// </remarks>
		public static TColor ToTizenCommonColor(this Color? color) =>
			color is null ? TColor.Default : new TColor(color.Red, color.Green, color.Blue, color.Alpha);

		/// <summary>
		/// Converts to the colour type NUI view properties use, preserving "unset" as null.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="TizenPlatformExtensions.ToTizen"/>, which requires a colour.
		/// Several NUI properties treat null as "keep the theme default", so the distinction has
		/// to survive the conversion.
		/// </remarks>
		public static NColor? ToTizenNativeColor(this Color? color) =>
			color is null ? null : new NColor(color.Red, color.Green, color.Blue, color.Alpha);
	}
}
