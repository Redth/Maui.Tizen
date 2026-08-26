using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Behavioural tests for the swipe maths reproduced from MAUI internals.
/// </summary>
/// <remarks>
/// <para>
/// <c>SwipeViewExtensions</c> and <c>SwipeDirectionHelper</c> are <see langword="internal"/> to
/// Microsoft.Maui.Core, so this backend cannot call them and cannot diff against them at build
/// time either. That makes them the easiest thing in the port to get quietly wrong, which is
/// exactly what happened: an earlier revision used a "larger delta wins" approximation that also
/// returned <see langword="null"/> below a minimum travel distance. The caller stores the result in
/// a nullable field that upstream only ever clears explicitly, so a small drag left the gesture
/// with no direction and the swipe never opened.
/// </para>
/// <para>
/// These tests pin the upstream behaviour rather than the current implementation's.
/// </para>
/// </remarks>
public class SwipeMetricsTests
{
	[Fact]
	public void ConstantsMatchUpstream()
	{
		Assert.Equal(250, TizenSwipeMetrics.SwipeThreshold);
		Assert.Equal(100, TizenSwipeMetrics.SwipeItemWidth);
	}

	[Theory]
	// Cardinal directions. Y grows downward, so a larger Y is a downward swipe.
	[InlineData(0, 0, 100, 0, SwipeDirection.Right)]
	[InlineData(0, 0, -100, 0, SwipeDirection.Left)]
	[InlineData(0, 0, 0, 100, SwipeDirection.Down)]
	[InlineData(0, 0, 0, -100, SwipeDirection.Up)]
	public void ResolvesCardinalDirections(double x1, double y1, double x2, double y2, SwipeDirection expected) =>
		Assert.Equal(expected, TizenSwipeMetrics.GetSwipeDirection(new Point(x1, y1), new Point(x2, y2)));

	[Theory]
	// Diagonals resolve to the dominant axis; the boundaries sit at 45 degrees.
	[InlineData(0, 0, 100, -10, SwipeDirection.Right)]
	[InlineData(0, 0, 10, -100, SwipeDirection.Up)]
	[InlineData(0, 0, -100, 10, SwipeDirection.Left)]
	[InlineData(0, 0, -10, 100, SwipeDirection.Down)]
	public void ResolvesDiagonalsToTheDominantAxis(double x1, double y1, double x2, double y2, SwipeDirection expected) =>
		Assert.Equal(expected, TizenSwipeMetrics.GetSwipeDirection(new Point(x1, y1), new Point(x2, y2)));

	/// <summary>
	/// The regression this file exists for: tiny movements must still resolve to a direction.
	/// </summary>
	[Theory]
	[InlineData(1, 0, SwipeDirection.Right)]
	[InlineData(-1, 0, SwipeDirection.Left)]
	[InlineData(0, 1, SwipeDirection.Down)]
	[InlineData(0, -1, SwipeDirection.Up)]
	public void SubPixelMovementStillResolvesToADirection(double dx, double dy, SwipeDirection expected) =>
		Assert.Equal(expected, TizenSwipeMetrics.GetSwipeDirection(Point.Zero, new Point(dx, dy)));

	/// <summary>
	/// Upstream returns a non-nullable <see cref="SwipeDirection"/>, so even a zero-length gesture
	/// yields a value.
	/// </summary>
	/// <remarks>
	/// <c>Right</c>, not the <c>Left</c> fall-through one might expect: <c>Atan2(0, 0)</c> is 0, so
	/// the angle normalises to 0 degrees, which lands in the <c>[0, 45)</c> Right bucket. Verified
	/// against upstream's formula rather than assumed — the first draft of this test asserted Left
	/// and was wrong.
	/// </remarks>
	[Fact]
	public void ZeroLengthGestureResolvesToRight() =>
		Assert.Equal(SwipeDirection.Right, TizenSwipeMetrics.GetSwipeDirection(Point.Zero, Point.Zero));

	[Theory]
	// Verbatim from upstream GetSwipeDirectionFromAngle.
	[InlineData(0, SwipeDirection.Right)]
	[InlineData(44.9, SwipeDirection.Right)]
	[InlineData(45, SwipeDirection.Up)]
	[InlineData(134.9, SwipeDirection.Up)]
	[InlineData(135, SwipeDirection.Left)]
	[InlineData(224.9, SwipeDirection.Left)]
	[InlineData(225, SwipeDirection.Down)]
	[InlineData(314.9, SwipeDirection.Down)]
	[InlineData(315, SwipeDirection.Right)]
	[InlineData(359.9, SwipeDirection.Right)]
	public void AngleBoundariesMatchUpstream(double angle, SwipeDirection expected) =>
		Assert.Equal(expected, TizenSwipeMetrics.GetSwipeDirectionFromAngle(angle));

	[Theory]
	[InlineData(0, 0, 1, 0, 0)]     // due right
	[InlineData(0, 0, 0, -1, 90)]   // due up
	[InlineData(0, 0, -1, 0, 180)]  // due left
	[InlineData(0, 0, 0, 1, 270)]   // due down
	public void AngleFromPointsMatchesUpstream(double x1, double y1, double x2, double y2, double expected) =>
		Assert.Equal(expected, TizenSwipeMetrics.GetAngleFromPoints(x1, y1, x2, y2), 3);
}
