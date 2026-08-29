using System;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	internal static class ViewportConstraint
	{
		public static double Resolve(double constraint, double allocated) =>
			Math.Max(0, double.IsFinite(constraint) ? constraint : allocated);
	}
}
