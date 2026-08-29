using System;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	internal static class ViewportConstraint
	{
		public static double Resolve(double constraint, double allocated) =>
			Math.Max(0, double.IsFinite(constraint) ? constraint : allocated);

		public static double Remaining(double allocated, double header, double footer) =>
			Math.Max(0, allocated - header - footer);

		public static double ResolveWithin(double constraint, double allocated) =>
			Math.Min(allocated, Resolve(constraint, allocated));
	}
}
