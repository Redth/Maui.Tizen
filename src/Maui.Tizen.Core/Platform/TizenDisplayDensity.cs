using System;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Display metrics for the Tizen device.
	/// </summary>
	/// <remarks>
	/// Replaces <c>Tizen.UIExtensions.Common.DeviceInfo</c>, which this backend deliberately does
	/// not take a package dependency on. See docs/net11-status.md.
	/// </remarks>
	public static class TizenDisplayDensity
	{
		/// <summary>The baseline density used by MAUI to convert between DP and pixels.</summary>
		public const double BaselineDpi = 160.0;

		static double? _overrideDensity;

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
					var dpi = global::Tizen.NUI.Window.Instance?.Dpi;
					if (dpi is not null && dpi.X > 0)
						return dpi.X / BaselineDpi;
				}
				catch (Exception)
				{
					// The NUI window is not available yet (for example before OnPreCreate);
					// fall through to the 1.0 baseline.
				}
#endif
				return 1.0;
			}
		}

		/// <summary>
		/// Overrides the reported density. Intended for tests and for hosts that already know the
		/// device scaling factor.
		/// </summary>
		/// <param name="density">The density to report, or <see langword="null"/> to reset.</param>
		public static void SetDensityOverride(double? density) => _overrideDensity = density;

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
