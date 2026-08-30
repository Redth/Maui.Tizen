using System;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Display metrics for the Tizen device.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Delegates to <c>Tizen.UIExtensions.Common.DeviceInfo</c> so that this backend, the NUI
	/// controls, gesture recognisers and swipe thresholds all scale identically. There is one
	/// source of truth at runtime and therefore no way for them to drift apart.
	/// </para>
	/// <para>
	/// It previously computed <c>Window.Default.Dpi.X / 160</c> and described itself as replacing
	/// DeviceInfo because the backend took no dependency on it. That was wrong on both counts: this
	/// package has always carried a PackageReference to Tizen.UIExtensions.NUI and uses its types
	/// throughout, and the formula disagreed with DeviceInfo in three ways - Dpi.X rather than
	/// Dpi.Y, no TV/IoT substitution, and no support for any display unit other than DP. See
	/// <see cref="TizenScalingPolicy"/>.
	/// </para>
	/// </remarks>
	public static class TizenDisplayDensity
	{
		/// <summary>The baseline density used by MAUI to convert between DP and pixels.</summary>
		public const double BaselineDpi = TizenScalingPolicy.BaselineDpi;

		static double? _overrideDensity;
		static TizenDisplayResolutionUnit _resolutionUnit = TizenDisplayResolutionUnit.DP;
		static double _viewPortWidth;

		/// <summary>
		/// Gets the current display density (device pixels per device-independent pixel).
		/// </summary>
		public static double Current
		{
			get
			{
				if (_overrideDensity is double density)
					return density;

#if TIZEN
				try
				{
					// The single runtime source of truth. DeviceInfo already applies the TV/IoT
					// DPI substitution and the display-unit rules; reimplementing them here is how
					// the two drifted apart in the first place.
					return global::Tizen.UIExtensions.Common.DeviceInfo.ScalingFactor;
				}
				catch (Exception)
				{
					// No NUI window yet (for example before OnPreCreate); fall through to the
					// baseline rather than throwing during startup.
				}
#endif
				return 1.0;
			}
		}

		/// <summary>
		/// Gets the physical scale, DPI / 160, regardless of the display resolution unit.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="Current"/> and not interchangeable with it. Upstream MAUI's
		/// Tizen backend keeps the same split - <c>ToPixel</c> is physical, <c>ToScaledPixel</c> is
		/// scaled - and they coincide only in DP mode. This backend used the physical formula for
		/// both, so every scaled conversion silently ignored the display unit.
		/// </remarks>
		public static double PhysicalScale
		{
			get
			{
				if (_overrideDensity is double density)
					return density;

#if TIZEN
				try
				{
					return global::Tizen.UIExtensions.Common.DeviceInfo.PhysicalScale;
				}
				catch (Exception)
				{
					// See Current.
				}
#endif
				return 1.0;
			}
		}

		/// <summary>
		/// Gets or sets how device-independent units are interpreted.
		/// </summary>
		/// <remarks>
		/// Forwarded to DeviceInfo, so changing it moves this backend and the native controls
		/// together. A host that sets it on one side only would reintroduce the divergence.
		/// </remarks>
		public static TizenDisplayResolutionUnit DisplayResolutionUnit
		{
			get
			{
#if TIZEN
				return (TizenDisplayResolutionUnit)global::Tizen.UIExtensions.Common.DeviceInfo.DisplayResolutionUnit;
#else
				return _resolutionUnit;
#endif
			}

			set
			{
#if TIZEN
				global::Tizen.UIExtensions.Common.DeviceInfo.DisplayResolutionUnit =
					(global::Tizen.UIExtensions.Common.DisplayResolutionUnit)value;
#endif
				_resolutionUnit = value;
			}
		}

		/// <summary>
		/// Gets or sets the viewport width used by <see cref="TizenDisplayResolutionUnit.VP"/>.
		/// </summary>
		/// <remarks>Forwarded to DeviceInfo for the same reason as the unit.</remarks>
		public static double ViewPortWidth
		{
			get
			{
#if TIZEN
				return global::Tizen.UIExtensions.Common.DeviceInfo.ViewPortWidth;
#else
				return _viewPortWidth;
#endif
			}

			set
			{
#if TIZEN
				global::Tizen.UIExtensions.Common.DeviceInfo.ViewPortWidth = value;
#endif
				_viewPortWidth = value;
			}
		}

		/// <summary>
		/// Overrides the reported density. Intended for tests and for hosts that already know the
		/// device scaling factor.
		/// </summary>
		/// <param name="density">The density to report, or <see langword="null"/> to reset.</param>
		public static void SetDensityOverride(double? density) => _overrideDensity = density;

		/// <summary>Converts a device-independent value to physical pixels.</summary>
		/// <remarks>
		/// Uses <see cref="PhysicalScale"/>, so it ignores the display resolution unit. Use
		/// <see cref="ToScaledPixel"/> for layout geometry; this is for the few places that need
		/// true physical pixels.
		/// </remarks>
		/// <param name="dp">The device-independent value.</param>
		/// <returns>The value in physical pixels.</returns>
		public static int ToPixel(this double dp)
		{
			if (double.IsPositiveInfinity(dp))
				return int.MaxValue;

			return (int)Math.Round(dp * PhysicalScale);
		}

		/// <summary>Converts a device-independent value to scaled pixels.</summary>
		/// <param name="dp">The device-independent value.</param>
		/// <returns>The value in scaled pixels.</returns>
		public static int ToScaledPixel(this double dp)
		{
			if (double.IsPositiveInfinity(dp))
				return int.MaxValue;

			return (int)Math.Round(dp * Current);
		}

		/// <summary>Converts a scaled pixel value to device-independent units.</summary>
		/// <param name="pixel">The scaled pixel value.</param>
		/// <returns>The value in device-independent units.</returns>
		public static double ToScaledDP(this int pixel)
		{
			if (pixel == int.MaxValue)
				return double.PositiveInfinity;

			return pixel / Current;
		}

		/// <summary>Converts a scaled pixel value to device-independent units.</summary>
		/// <param name="pixel">The scaled pixel value.</param>
		/// <returns>The value in device-independent units.</returns>
		public static double ToScaledDP(this double pixel) => pixel / Current;
	}
}
