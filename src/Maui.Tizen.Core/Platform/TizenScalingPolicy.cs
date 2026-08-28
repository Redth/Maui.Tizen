using System;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// How a device-independent unit is interpreted when converting to pixels.
	/// </summary>
	/// <remarks>
	/// Mirrors <c>Tizen.UIExtensions.Common.DisplayResolutionUnit</c>. Declared here so the policy
	/// below can be exercised on the host, where TizenFX does not exist; the values and order match
	/// so the two convert directly.
	/// </remarks>
	public enum TizenDisplayResolutionUnit
	{
		/// <summary>Geometry is in raw pixels; no scaling is applied.</summary>
		Pixel,

		/// <summary>Raw pixels, scaled up on very large displays.</summary>
		DeviceScaledPixel,

		/// <summary>Device-independent pixels: DPI / 160.</summary>
		DP,

		/// <summary>Device-independent pixels, scaled up on very large displays.</summary>
		DeviceScaledDP,

		/// <summary>Viewport units: the screen is treated as a fixed number of units wide.</summary>
		VP,
	}

	/// <summary>
	/// The raw display metrics a scaling factor is computed from.
	/// </summary>
	/// <param name="Dpi">Effective DPI. See <see cref="TizenScalingPolicy.ResolveDpi"/>.</param>
	/// <param name="ScreenWidth">Screen width in pixels.</param>
	/// <param name="ScreenHeight">Screen height in pixels.</param>
	/// <param name="Unit">How device-independent units are interpreted.</param>
	/// <param name="ViewPortWidth">Viewport width, used only by <see cref="TizenDisplayResolutionUnit.VP"/>.</param>
	public readonly record struct TizenDisplayMetrics(
		int Dpi,
		int ScreenWidth,
		int ScreenHeight,
		TizenDisplayResolutionUnit Unit = TizenDisplayResolutionUnit.DP,
		double ViewPortWidth = 0);

	/// <summary>
	/// The single display-scaling policy for this backend.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists because Core and the native controls disagreed. Core computed
	/// <c>Window.Default.Dpi.X / 160</c> while Tizen.UIExtensions - which the NUI controls, gesture
	/// recognisers and swipe thresholds all use - computes
	/// <c>Tizen.UIExtensions.Common.DeviceInfo.ScalingFactor</c>. Three separate divergences:
	/// </para>
	/// <list type="number">
	/// <item>
	/// UIExtensions reads <b>Dpi.Y</b>, not Dpi.X. They are equal on most panels and not on all.
	/// </item>
	/// <item>
	/// On <b>TV and IoT</b> it does not read the panel at all - it returns a hard-coded <b>213</b>.
	/// Reading the reported DPI there produced a completely different scale from every native
	/// control on the same screen.
	/// </item>
	/// <item>
	/// DPI / 160 is only correct for the <c>DP</c> unit. The other four modes scale differently,
	/// and Core implemented none of them.
	/// </item>
	/// </list>
	/// <para>
	/// There is also a distinction Core had collapsed. Upstream MAUI's Tizen backend keeps two
	/// conversions: <c>ToPixel</c> uses <see cref="PhysicalScale"/> (DPI / 160) and
	/// <c>ToScaledPixel</c> uses the scaling factor. They coincide only in <c>DP</c> mode. Core used
	/// the physical formula for both, so every "scaled" conversion silently ignored the display
	/// unit.
	/// </para>
	/// <para>
	/// At runtime on a device the backend delegates to <c>DeviceInfo.ScalingFactor</c> itself, so
	/// there is exactly one source of truth and no possibility of drift. This type is the same
	/// algorithm expressed over injectable metrics: it drives the host tests, documents the policy
	/// for Wave B and alerts to consume, and provides the answer when no NUI window exists yet.
	/// </para>
	/// </remarks>
	public static class TizenScalingPolicy
	{
		/// <summary>The DPI at which one device-independent pixel equals one physical pixel.</summary>
		public const double BaselineDpi = 160.0;

		/// <summary>The fixed DPI reported for TV and IoT profiles.</summary>
		/// <remarks>
		/// TV and IoT panels report a DPI that reflects the physical panel rather than the viewing
		/// distance, so UIExtensions substitutes this constant. Anything that reads the panel
		/// directly ends up on a different scale from every native control.
		/// </remarks>
		public const int TvAndIoTDpi = 213;

		/// <summary>
		/// The effective DPI for a device, applying the TV/IoT substitution.
		/// </summary>
		/// <param name="reportedDpiY">The panel's reported vertical DPI.</param>
		/// <param name="isTv">Whether the device profile is TV.</param>
		/// <param name="isIoT">Whether the device profile is IoT.</param>
		public static int ResolveDpi(double reportedDpiY, bool isTv, bool isIoT)
		{
			if (isTv || isIoT)
				return TvAndIoTDpi;

			return (int)reportedDpiY;
		}

		/// <summary>Physical scale: DPI / 160, independent of the display unit.</summary>
		/// <remarks>Backs <c>ToPixel</c>, which upstream keeps distinct from <c>ToScaledPixel</c>.</remarks>
		public static double PhysicalScale(int dpi) => dpi / BaselineDpi;

		/// <summary>
		/// The scaling factor for the given metrics.
		/// </summary>
		/// <remarks>
		/// Mirrors <c>DeviceInfo.UpdateScalingFactor</c> exactly, including the order of its
		/// conditions - <c>DeviceScaledDP</c> deliberately satisfies both the DP branch and the
		/// device-scaled branch, so it is DPI / 160 AND multiplied on large displays.
		/// </remarks>
		public static double ComputeScalingFactor(TizenDisplayMetrics metrics)
		{
			// Pixel mode means scaling is off: geometry units are raw pixels.
			var scalingFactor = 1.0;

			if (metrics.Unit == TizenDisplayResolutionUnit.VP && metrics.ViewPortWidth > 0)
				return metrics.ScreenWidth / metrics.ViewPortWidth;

			if (metrics.Unit is TizenDisplayResolutionUnit.DP or TizenDisplayResolutionUnit.DeviceScaledDP)
				scalingFactor = PhysicalScale(metrics.Dpi);

			if (metrics.Unit is TizenDisplayResolutionUnit.DeviceScaledPixel or TizenDisplayResolutionUnit.DeviceScaledDP)
			{
				var physical = PhysicalScale(metrics.Dpi);

				// Measured in DP, so the thresholds mean the same thing on any panel density.
				var portraitSize = Math.Min(
					metrics.ScreenWidth / physical,
					metrics.ScreenHeight / physical);

				if (portraitSize > 2000)
					scalingFactor *= 4;
				else if (portraitSize > 1000)
					scalingFactor *= 2.5;
			}

			return scalingFactor;
		}
	}
}
