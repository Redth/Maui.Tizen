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

		public static double ResolveEmptyCell(double constraint, double allocated, bool spanCrossAxis) =>
			ResolveWithin(spanCrossAxis ? double.PositiveInfinity : constraint, allocated);

		public static bool NeedsEmptyPlaceholder(
			bool hasEmptyView,
			bool hasEmptyViewTemplate,
			bool hasHeader,
			bool hasFooter) =>
			hasEmptyView || hasEmptyViewTemplate || hasHeader || hasFooter;
	}

	internal static class ItemsViewMeasure
	{
		public static (double Width, double Height) Resolve(
			double availableWidth,
			double availableHeight,
			double allocatedWidth,
			double allocatedHeight,
			double canvasWidth,
			double canvasHeight,
			double fallbackWidth,
			double fallbackHeight,
			bool hasNativeLayout,
			bool isHorizontal)
		{
			var widthLimit = FiniteLimit(availableWidth, allocatedWidth, fallbackWidth);
			var heightLimit = FiniteLimit(availableHeight, allocatedHeight, fallbackHeight);
			if (!hasNativeLayout || allocatedWidth <= 0 || allocatedHeight <= 0)
				return (widthLimit, heightLimit);

			return isHorizontal
				? (ConstrainCanvas(canvasWidth, widthLimit), heightLimit)
				: (widthLimit, ConstrainCanvas(canvasHeight, heightLimit));
		}

		static double FiniteLimit(double available, double allocated, double fallback)
		{
			if (double.IsFinite(available))
				return Math.Max(0, available);
			if (allocated > 0 && double.IsFinite(allocated))
				return allocated;
			return Math.Max(0, double.IsFinite(fallback) ? fallback : 0);
		}

		static double ConstrainCanvas(double canvas, double viewport) =>
			canvas > 0 && double.IsFinite(canvas)
				? Math.Min(canvas, viewport)
				: viewport;
	}
}
