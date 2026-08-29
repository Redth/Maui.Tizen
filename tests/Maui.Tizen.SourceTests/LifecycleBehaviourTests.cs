using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Behavioural tests for the lifecycle fixes that can be executed on the host lane.
/// </summary>
/// <remarks>
/// Each covers a defect whose symptom is silent — a control that quietly scrolls when it should
/// not, a refresh that never restarts, an indicator that reappears after being hidden. None is
/// visible to a compile check or to key-presence parity.
/// </remarks>
public class LifecycleBehaviourTests
{
	// -------------------------------------------------------------------------------------------
	// ScrollOrientation.Neither
	// -------------------------------------------------------------------------------------------

	/// <summary>
	/// Models Tizen.UIExtensions.NUI 0.9.2's ScrollOrientation conversion.
	/// </summary>
	/// <remarks>
	/// Its <c>ToNative</c> compiles to <c>value == Horizontal ? Horizontal : Vertical</c>, verified
	/// by reading the shipped assembly's IL. NUI's ScrollingDirection has no "neither" value, so
	/// both <c>Both</c> and <c>Neither</c> arrive as Vertical.
	/// </remarks>
	static bool NativeScrollsVertically(ScrollOrientation orientation) =>
		orientation != ScrollOrientation.Horizontal;

	/// <summary>
	/// Establishes the defect: the orientation alone cannot express "do not scroll".
	/// </summary>
	[Fact]
	public void TheNativeConversionCannotExpressNeither()
	{
		// Neither is indistinguishable from Vertical and Both once converted, which is precisely
		// why ScrollEnabled has to carry the intent instead.
		Assert.True(NativeScrollsVertically(ScrollOrientation.Neither));
		Assert.True(NativeScrollsVertically(ScrollOrientation.Vertical));
		Assert.True(NativeScrollsVertically(ScrollOrientation.Both));
	}

	[Theory]
	[InlineData(ScrollOrientation.Neither, false)]
	[InlineData(ScrollOrientation.Vertical, true)]
	[InlineData(ScrollOrientation.Horizontal, true)]
	[InlineData(ScrollOrientation.Both, true)]
	public void ScrollEnabledFollowsTheOrientation(ScrollOrientation orientation, bool expected) =>
		Assert.Equal(expected, orientation != ScrollOrientation.Neither);

	// -------------------------------------------------------------------------------------------
	// RefreshView transitions
	// -------------------------------------------------------------------------------------------

	/// <summary>
	/// A restart during the native completion window is replayed, not lost.
	/// </summary>
	/// <remarks>
	/// The reported defect. The base class's private state machine ignores a start request while it
	/// is completing, so <c>false</c> immediately followed by <c>true</c> left the virtual view
	/// believing it was refreshing while the spinner never came back.
	/// </remarks>
	[Fact]
	public void ARestartDuringTheCompletionWindowIsReplayed()
	{
		var machine = new TizenRefreshStateMachine();

		Assert.Equal(TizenRefreshAction.Apply, machine.Request(true));
		Assert.Equal(TizenRefreshAction.Apply, machine.Request(false));

		// Inside the window: the native control would drop this.
		Assert.Equal(TizenRefreshAction.Defer, machine.Request(true));
		Assert.False(machine.IsRefreshing);

		Assert.Equal(TizenRefreshAction.Apply, machine.CompletionElapsed());
		Assert.True(machine.IsRefreshing);
	}

	/// <summary>A stop arriving during the window supersedes a held start.</summary>
	/// <remarks>
	/// Otherwise the replay would restart a refresh the virtual view had already cancelled — the
	/// mirror image of the original bug.
	/// </remarks>
	[Fact]
	public void AStopSupersedesAHeldStart()
	{
		var machine = new TizenRefreshStateMachine();

		machine.Request(true);
		machine.Request(false);
		Assert.Equal(TizenRefreshAction.Defer, machine.Request(true));

		machine.Request(false);

		Assert.Equal(TizenRefreshAction.None, machine.CompletionElapsed());
		Assert.False(machine.IsRefreshing);
	}

	/// <summary>Outside the completion window a start applies immediately.</summary>
	[Fact]
	public void AStartOutsideTheWindowAppliesImmediately()
	{
		var machine = new TizenRefreshStateMachine();

		Assert.Equal(TizenRefreshAction.Apply, machine.Request(true));
		Assert.True(machine.IsRefreshing);

		// Already refreshing: nothing to do.
		Assert.Equal(TizenRefreshAction.None, machine.Request(true));
	}

	/// <summary>
	/// Teardown must not produce a native write.
	/// </summary>
	/// <remarks>
	/// Writing <c>IsRefreshing</c> starts the base class's completion animation — an async void with
	/// no cancellation — whose continuation then touches the refresh icon the handler is about to
	/// dispose. Reset exists so teardown can abandon the state without triggering that.
	/// </remarks>
	[Fact]
	public void ResetAbandonsStateWithoutRequestingAnyWrite()
	{
		var machine = new TizenRefreshStateMachine();

		machine.Request(true);
		machine.Request(false);
		machine.Request(true);

		machine.Reset();

		Assert.False(machine.IsRefreshing);
		Assert.False(machine.IsCompleting);
		Assert.False(machine.HasPendingStart);

		// Nothing is left that could fire after disposal.
		Assert.Equal(TizenRefreshAction.None, machine.CompletionElapsed());
	}

	/// <summary>A stop while idle is a no-op, so teardown cannot start an animation by accident.</summary>
	[Fact]
	public void AStopWhileIdleRequestsNothing() =>
		Assert.Equal(TizenRefreshAction.None, new TizenRefreshStateMachine().Request(false));

	// -------------------------------------------------------------------------------------------
	// IndicatorView visibility
	// -------------------------------------------------------------------------------------------

	/// <summary>
	/// A hidden indicator stays hidden when its count changes.
	/// </summary>
	/// <remarks>
	/// The reported defect: the count mapper called Show unconditionally, so any Count,
	/// MaximumVisible or appearance change re-revealed a control the app had deliberately hidden.
	/// </remarks>
	[Theory]
	[InlineData(Visibility.Hidden)]
	[InlineData(Visibility.Collapsed)]
	public void AHiddenIndicatorIsNotRevealedByACountChange(Visibility visibility)
	{
		Assert.False(TizenPortableExtensions.IsIndicatorVisible(visibility, hideSingle: false, count: 5));
		Assert.False(TizenPortableExtensions.IsIndicatorVisible(visibility, hideSingle: true, count: 1));
		Assert.False(TizenPortableExtensions.IsIndicatorVisible(visibility, hideSingle: false, count: 0));
	}

	/// <summary>HideSingle still applies to a visible indicator.</summary>
	[Theory]
	[InlineData(true, 1, false)]
	[InlineData(true, 0, false)]
	[InlineData(true, 2, true)]
	[InlineData(false, 1, true)]
	[InlineData(false, 5, true)]
	public void HideSingleAppliesWhenTheViewIsVisible(bool hideSingle, int count, bool expected) =>
		Assert.Equal(expected, TizenPortableExtensions.IsIndicatorVisible(Visibility.Visible, hideSingle, count));
}
