using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Behavioural tests for the indicator position windowing.
/// </summary>
/// <remarks>
/// The dot count is capped at <c>MaximumVisible</c> but the position is not. The native highlight
/// call bounds-checks and returns silently, so before this logic existed a position beyond the cap
/// simply stopped being drawn — the indicator looked frozen on whichever dot it last managed to
/// highlight, with no error anywhere.
/// </remarks>
public class IndicatorWindowTests
{
	[Theory]
	// Everything fits: the position maps straight through.
	[InlineData(0, 5, 5, 0)]
	[InlineData(3, 5, 5, 3)]
	[InlineData(4, 5, 5, 4)]
	public void PositionsWithinTheWindowMapDirectly(int position, int count, int visible, int expected) =>
		Assert.Equal(expected, TizenPortableExtensions.GetVisibleIndicatorPosition(position, count, visible));

	[Theory]
	// 20 items, 5 dots. Early positions sit where they are; later ones pin to the trailing dot as
	// the window slides, so the selection stays visible instead of vanishing.
	[InlineData(0, 20, 5, 0)]
	[InlineData(4, 20, 5, 4)]
	[InlineData(10, 20, 5, 4)]
	[InlineData(19, 20, 5, 4)]
	public void PositionsBeyondTheWindowStayVisible(int position, int count, int visible, int expected) =>
		Assert.Equal(expected, TizenPortableExtensions.GetVisibleIndicatorPosition(position, count, visible));

	/// <summary>The regression itself: a capped indicator must never lose its selection.</summary>
	[Fact]
	public void NoPositionEverFallsOutsideTheDotList()
	{
		const int Count = 50;
		const int Visible = 4;

		for (var position = 0; position < Count; position++)
		{
			var index = TizenPortableExtensions.GetVisibleIndicatorPosition(position, Count, Visible);

			Assert.InRange(index, 0, Visible - 1);
		}
	}

	[Theory]
	[InlineData(-1, 10, 5)]   // no selection
	[InlineData(0, 10, 0)]    // no dots created
	[InlineData(0, 0, 0)]     // empty indicator
	public void DegenerateInputsSelectNothing(int position, int count, int visible) =>
		Assert.Equal(-1, TizenPortableExtensions.GetVisibleIndicatorPosition(position, count, visible));

	/// <summary>A position past the end of a short list clamps rather than indexing out of range.</summary>
	[Fact]
	public void APositionPastTheEndClamps() =>
		Assert.Equal(2, TizenPortableExtensions.GetVisibleIndicatorPosition(9, 3, 3));

	[Fact]
	public void PositionTenUsesWindowSixThroughTenAndTranslatesTaps()
	{
		var window = TizenPortableExtensions.GetIndicatorWindow(
			position: 10,
			count: 20,
			maximumVisible: 5);

		Assert.Equal(6, window.Start);
		Assert.Equal(5, window.VisibleCount);
		Assert.Equal(4, window.SelectedIndex);
		Assert.Equal(6, window.ToAbsolutePosition(0));
		Assert.Equal(8, window.ToAbsolutePosition(2));
		Assert.Equal(10, window.ToAbsolutePosition(4));
	}

	[Theory]
	[InlineData(0, 20, 5, 0, 0)]
	[InlineData(4, 20, 5, 0, 4)]
	[InlineData(19, 20, 5, 15, 4)]
	[InlineData(9, 3, 3, 0, 2)]
	public void EdgeWindowsClampStartAndSelection(
		int position,
		int count,
		int visible,
		int expectedStart,
		int expectedSelection)
	{
		var window = TizenPortableExtensions.GetIndicatorWindow(position, count, visible);

		Assert.Equal(expectedStart, window.Start);
		Assert.Equal(expectedSelection, window.SelectedIndex);
	}
}
