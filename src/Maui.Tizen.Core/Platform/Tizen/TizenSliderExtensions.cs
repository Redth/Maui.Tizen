// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.NUI.Components;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>ISlider</c> property mappings.
	/// </summary>
	public static class TizenSliderExtensions
	{
		/// <remarks>
		/// NUI validates that the minimum is below the maximum when either is assigned, so the
		/// bounds are written as a pair. Setting them one at a time makes the order of the
		/// mapper keys load-bearing, which it should not be.
		/// </remarks>
		public static void UpdateMinimum(this Slider platformSlider, ISlider slider) =>
			platformSlider.UpdateRange(slider);

		/// <remarks>See <see cref="UpdateMinimum"/>.</remarks>
		public static void UpdateMaximum(this Slider platformSlider, ISlider slider) =>
			platformSlider.UpdateRange(slider);

		static void UpdateRange(this Slider platformSlider, ISlider slider)
		{
			var min = (float)slider.Minimum;
			var max = (float)slider.Maximum;

			if (max <= min)
			{
				// Degenerate range; give NUI a valid one and let the value clamp to it.
				max = min + float.Epsilon;
			}

			// Widen first so neither assignment ever passes through an inverted range.
			if (max >= platformSlider.MaxValue)
			{
				platformSlider.MaxValue = max;
				platformSlider.MinValue = min;
			}
			else
			{
				platformSlider.MinValue = min;
				platformSlider.MaxValue = max;
			}
		}

		public static void UpdateValue(this Slider platformSlider, ISlider slider) =>
			platformSlider.CurrentValue = (float)slider.Value;

		public static void UpdateMinimumTrackColor(this Slider platformSlider, ISlider slider) =>
			platformSlider.SlidedTrackColor = slider.MinimumTrackColor.ToTizenNativeColor();

		public static void UpdateMaximumTrackColor(this Slider platformSlider, ISlider slider) =>
			platformSlider.BgTrackColor = slider.MaximumTrackColor.ToTizenNativeColor();

		public static void UpdateThumbColor(this Slider platformSlider, ISlider slider) =>
			platformSlider.ThumbColor = slider.ThumbColor.ToTizenNativeColor();

		/// <summary>
		/// Applies an already-resolved thumb image, or clears it.
		/// </summary>
		/// <remarks>
		/// Resolution is deliberately not done here. It is asynchronous, and doing it inside an
		/// extension leaves nowhere to hold the cancellation and ownership state that a correct
		/// image load needs; the handler owns a <see cref="TizenImageLoader{TImage}"/> for that.
		/// Clearing on a null image is what stops a removed source leaving the previous bitmap in
		/// place.
		/// </remarks>
		/// <param name="platformSlider">The platform slider.</param>
		/// <param name="image">The resolved image, or <see langword="null"/> to clear.</param>
		public static void UpdateThumbImageSource(this Slider platformSlider, TizenImageSource? image) =>
			platformSlider.ThumbImageUrl = image?.ResourceUrl ?? string.Empty;
	}
}
