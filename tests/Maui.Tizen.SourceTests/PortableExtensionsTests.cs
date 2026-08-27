using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Behavioural tests for the MAUI helpers reproduced in <see cref="TizenPortableExtensions"/>.
/// </summary>
/// <remarks>
/// <c>RectF.ContainsAny</c> and the Tizen visibility conversion are <see langword="internal"/> to
/// Microsoft.Maui.Core, so there is nothing for the compiler to diff a reproduction against.
/// <c>ToPlatformVisibility</c> was in fact reproduced incorrectly at first — it compared against
/// <c>Visible</c> rather than switching on <c>Hidden</c>/<c>Collapsed</c> — which is exactly the
/// class of mistake these tests exist to catch.
/// </remarks>
public class PortableExtensionsTests
{
	[Theory]
	[InlineData(Visibility.Visible, true)]
	[InlineData(Visibility.Hidden, false)]
	[InlineData(Visibility.Collapsed, false)]
	public void VisibilityMapsAsUpstreamDoes(Visibility visibility, bool expected) =>
		Assert.Equal(expected, visibility.ToPlatformVisibility());

	/// <summary>
	/// Upstream switches on the hidden cases and defaults to visible, so an unknown value must be
	/// treated as visible rather than hidden.
	/// </summary>
	[Fact]
	public void UnknownVisibilityDefaultsToVisible() =>
		Assert.True(((Visibility)0x7FFF).ToPlatformVisibility());

	[Fact]
	public void ContainsAnyIsTrueWhenAnySinglePointIsInside()
	{
		var rect = new RectF(0, 0, 10, 10);

		Assert.True(rect.ContainsAny(new[] { new PointF(50, 50), new PointF(5, 5) }));
	}

	[Fact]
	public void ContainsAnyIsFalseWhenEveryPointIsOutside()
	{
		var rect = new RectF(0, 0, 10, 10);

		Assert.False(rect.ContainsAny(new[] { new PointF(-1, -1), new PointF(50, 50) }));
	}

	[Fact]
	public void ContainsAnyIsFalseForAnEmptyPointSet() =>
		Assert.False(new RectF(0, 0, 10, 10).ContainsAny(Array.Empty<PointF>()));

	/// <summary>
	/// Delegates to <see cref="RectF.Contains(PointF)"/>, so boundary behaviour must match MAUI's
	/// own rather than a hand-rolled comparison.
	/// </summary>
	[Theory]
	[InlineData(0f, 0f, true)]
	[InlineData(10f, 10f, false)]
	[InlineData(9.99f, 9.99f, true)]
	public void ContainsAnyDefersToRectFContains(float x, float y, bool expected)
	{
		var rect = new RectF(0, 0, 10, 10);

		Assert.Equal(expected, rect.ContainsAny(new[] { new PointF(x, y) }));
		Assert.Equal(rect.Contains(new PointF(x, y)), rect.ContainsAny(new[] { new PointF(x, y) }));
	}
}
