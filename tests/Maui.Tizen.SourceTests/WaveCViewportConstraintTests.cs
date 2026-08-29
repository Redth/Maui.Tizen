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
}
