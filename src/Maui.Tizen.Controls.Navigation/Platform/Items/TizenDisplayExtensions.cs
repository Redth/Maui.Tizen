using Tizen.UIExtensions.Common;
using TSize = Tizen.UIExtensions.Common.Size;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Extension methods for Tizen display-independent unit conversions.
	/// </summary>
	/// <remarks>
	/// These methods convert between density-independent pixels (DP) and scaled pixels
	/// using the device's scaling factor from <see cref="DeviceInfo.ScalingFactor"/>.
	/// </remarks>
	public static class TizenDisplayExtensions
	{
		/// <summary>
		/// Converts a value from density-independent pixels (DP) to scaled pixels.
		/// </summary>
		/// <param name="dp">The value in density-independent pixels.</param>
		/// <returns>The value in scaled pixels.</returns>
		public static float ToScaledPixel(this double dp)
		{
			return (float)(dp * DeviceInfo.ScalingFactor);
		}

		/// <summary>
		/// Converts a value from density-independent pixels (DP) to scaled pixels.
		/// </summary>
		/// <param name="dp">The value in density-independent pixels.</param>
		/// <returns>The value in scaled pixels.</returns>
		public static float ToScaledPixel(this float dp)
		{
			return (float)(dp * DeviceInfo.ScalingFactor);
		}

		/// <summary>
		/// Converts a value from scaled pixels to density-independent pixels (DP).
		/// </summary>
		/// <param name="pixel">The value in scaled pixels.</param>
		/// <returns>The value in density-independent pixels.</returns>
		public static double ToScaledDP(this double pixel)
		{
			return pixel / DeviceInfo.ScalingFactor;
		}

		/// <summary>
		/// Converts a value from scaled pixels to density-independent pixels (DP).
		/// </summary>
		/// <param name="pixel">The value in scaled pixels.</param>
		/// <returns>The value in density-independent pixels.</returns>
		public static double ToScaledDP(this float pixel)
		{
			return pixel / DeviceInfo.ScalingFactor;
		}

		/// <summary>
		/// Converts a <see cref="Microsoft.Maui.Graphics.Size"/> from DP to Tizen scaled pixels.
		/// </summary>
		/// <param name="size">The size in density-independent pixels.</param>
		/// <returns>The size in scaled pixels as a Tizen <see cref="TSize"/>.</returns>
		public static TSize ToPixel(this Microsoft.Maui.Graphics.Size size)
		{
			return new TSize(size.Width.ToScaledPixel(), size.Height.ToScaledPixel());
		}
	}
}
