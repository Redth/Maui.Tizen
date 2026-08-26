// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System.Threading.Tasks;
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
		/// Resolves and applies the slider's thumb image.
		/// </summary>
		/// <remarks>
		/// Clearing the image when the source becomes <see langword="null"/> is handled too, so
		/// removing a thumb image at runtime restores the default thumb instead of leaving the
		/// previous bitmap in place.
		/// </remarks>
		public static async Task UpdateThumbImageSourceAsync(this Slider platformSlider, ISlider slider, IImageSourceServiceProvider? provider)
		{
			var thumbImageSource = slider.ThumbImageSource;

			if (thumbImageSource is null || provider is null)
			{
				platformSlider.ThumbImageUrl = string.Empty;
				return;
			}

			var result = await provider.GetTizenImageAsync(thumbImageSource).ConfigureAwait(false);

			if (result?.Value?.ResourceUrl is string url)
				platformSlider.ThumbImageUrl = url;
		}
	}
}
