using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCViewportConstraintTests
{
	[Theory]
	[InlineData(120, 80, 120)]
	[InlineData(double.PositiveInfinity, 80, 80)]
	[InlineData(double.NegativeInfinity, 80, 80)]
	[InlineData(double.NaN, 80, 80)]
	[InlineData(-1, 80, 0)]
	public void ResolvesFiniteViewportSize(double constraint, double allocated, double expected) =>
		Assert.Equal(expected, ViewportConstraint.Resolve(constraint, allocated));

	[Theory]
	[InlineData(100, 15, 25, 60)]
	[InlineData(20, 15, 25, 0)]
	public void EmptyContentUsesViewportRemainingAfterDecorations(
		double allocated,
		double header,
		double footer,
		double expected) =>
		Assert.Equal(expected, ViewportConstraint.Remaining(allocated, header, footer));

	[Theory]
	[InlineData(120, 80, 80)]
	[InlineData(40, 80, 40)]
	[InlineData(double.PositiveInfinity, 80, 80)]
	public void EmptyContentNeverExceedsItsRemainingViewport(
		double constraint,
		double allocated,
		double expected) =>
		Assert.Equal(expected, ViewportConstraint.ResolveWithin(constraint, allocated));

	[Fact]
	public void EmptyContentSpansTheGridCrossAxis()
	{
		Assert.Equal(300, ViewportConstraint.ResolveEmptyCell(100, 300, spanCrossAxis: true));
		Assert.Equal(100, ViewportConstraint.ResolveEmptyCell(100, 300, spanCrossAxis: false));
	}

	[Theory]
	[InlineData(false, false, false, false, false)]
	[InlineData(true, false, false, false, true)]
	[InlineData(false, true, false, false, true)]
	[InlineData(false, false, true, false, true)]
	[InlineData(false, false, false, true, true)]
	public void HeaderOrFooterAloneKeepsAUsableEmptyExtent(
		bool emptyView,
		bool emptyTemplate,
		bool header,
		bool footer,
		bool expected) =>
		Assert.Equal(
			expected,
			ViewportConstraint.NeedsEmptyPlaceholder(emptyView, emptyTemplate, header, footer));

	[Theory]
	[InlineData(320, 480, 320, 480)]
	[InlineData(double.PositiveInfinity, 480, 1080, 480)]
	[InlineData(320, double.PositiveInfinity, 320, 1920)]
	public void ItemsMeasurementIsFiniteBeforeNativeAllocation(
		double availableWidth,
		double availableHeight,
		double expectedWidth,
		double expectedHeight)
	{
		var measured = ItemsViewMeasure.Resolve(
			availableWidth,
			availableHeight,
			0,
			0,
			0,
			0,
			1080,
			1920,
			hasNativeLayout: false,
			isHorizontal: false);

		Assert.Equal((expectedWidth, expectedHeight), measured);
	}

	[Theory]
	[InlineData(false, 300, 400, 300, 1000, 300, 400)]
	[InlineData(false, 300, 400, 300, 180, 300, 180)]
	[InlineData(true, 300, 400, 1000, 400, 300, 400)]
	[InlineData(true, 300, 400, 120, 400, 120, 400)]
	public void AllocatedItemsMeasurementUsesViewportConstrainedScrollCanvas(
		bool horizontal,
		double allocatedWidth,
		double allocatedHeight,
		double canvasWidth,
		double canvasHeight,
		double expectedWidth,
		double expectedHeight)
	{
		var measured = ItemsViewMeasure.Resolve(
			double.PositiveInfinity,
			double.PositiveInfinity,
			allocatedWidth,
			allocatedHeight,
			canvasWidth,
			canvasHeight,
			1080,
			1920,
			hasNativeLayout: true,
			isHorizontal: horizontal);

		Assert.Equal((expectedWidth, expectedHeight), measured);
	}
}
