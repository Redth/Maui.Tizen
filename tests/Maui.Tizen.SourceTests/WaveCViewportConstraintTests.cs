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
}
