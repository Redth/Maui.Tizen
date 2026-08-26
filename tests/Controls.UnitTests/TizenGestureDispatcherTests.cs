using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers the seam between the Tizen gesture pipeline and .NET MAUI's gesture recognizers.
/// </summary>
/// <remarks>
/// These tests use real recognizers rather than doubles, so they verify that the public
/// controller interfaces actually deliver events - and they pin the gestures that .NET MAUI does
/// not currently let an out-of-tree backend raise. See docs/tizen-gesture-support-matrix.md.
/// </remarks>
public class TizenGestureDispatcherTests
{
	static readonly Label View = new();

	[Theory]
	[InlineData(TizenGestureKind.Pan, true)]
	[InlineData(TizenGestureKind.Pinch, true)]
	[InlineData(TizenGestureKind.Swipe, true)]
	[InlineData(TizenGestureKind.Tap, true)]
	[InlineData(TizenGestureKind.Pointer, true)]
	[InlineData(TizenGestureKind.LongPress, false)]
	public void SupportMatrixMatchesWhatMauiExposesPublicly(TizenGestureKind kind, bool expected)
	{
		var dispatcher = new TizenGestureDispatcher();

		Assert.Equal(expected, dispatcher.IsSupported(kind));
	}

	[Fact]
	public void PanIsDeliveredThroughThePublicPanController()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PanGestureRecognizer();
		var updates = new List<PanUpdatedEventArgs>();
		recognizer.PanUpdated += (_, e) => updates.Add(e);

		dispatcher.SendPan(recognizer, View, TizenGestureState.Started, 0, 0, 7);
		dispatcher.SendPan(recognizer, View, TizenGestureState.Continuing, 12, 34, 7);
		dispatcher.SendPan(recognizer, View, TizenGestureState.Finished, 0, 0, 7);

		Assert.Collection(
			updates,
			e => Assert.Equal(GestureStatus.Started, e.StatusType),
			e =>
			{
				Assert.Equal(GestureStatus.Running, e.StatusType);
				Assert.Equal(12d, e.TotalX);
				Assert.Equal(34d, e.TotalY);
				Assert.Equal(7, e.GestureId);
			},
			e => Assert.Equal(GestureStatus.Completed, e.StatusType));
	}

	[Fact]
	public void CanceledPanIsDeliveredThroughThePublicPanController()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PanGestureRecognizer();
		var updates = new List<PanUpdatedEventArgs>();
		recognizer.PanUpdated += (_, e) => updates.Add(e);

		dispatcher.SendPan(recognizer, View, TizenGestureState.Started, 0, 0, 1);
		dispatcher.SendPan(recognizer, View, TizenGestureState.Canceled, 0, 0, 1);

		Assert.Equal(GestureStatus.Canceled, Assert.IsType<PanUpdatedEventArgs>(updates[^1]).StatusType);
	}

	[Fact]
	public void PinchIsDeliveredThroughThePublicPinchController()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PinchGestureRecognizer();
		var updates = new List<PinchGestureUpdatedEventArgs>();
		recognizer.PinchUpdated += (_, e) => updates.Add(e);

		dispatcher.SendPinch(recognizer, View, TizenGestureState.Started, 1, new Point(0.5, 0.5));
		dispatcher.SendPinch(recognizer, View, TizenGestureState.Continuing, 2.5, new Point(0.25, 0.75));
		dispatcher.SendPinch(recognizer, View, TizenGestureState.Finished, 2.5, Point.Zero);

		Assert.Collection(
			updates,
			e => Assert.Equal(GestureStatus.Started, e.Status),
			e =>
			{
				Assert.Equal(GestureStatus.Running, e.Status);
				Assert.Equal(2.5, e.Scale);
				Assert.Equal(new Point(0.25, 0.75), e.ScaleOrigin);
			},
			e => Assert.Equal(GestureStatus.Completed, e.Status));
	}

	[Fact]
	public void SwipeIsDeliveredThroughThePublicSwipeController()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new SwipeGestureRecognizer { Direction = SwipeDirection.Right, Threshold = 100 };
		SwipedEventArgs? swiped = null;
		recognizer.Swiped += (_, e) => swiped = e;

		dispatcher.SendSwipe(recognizer, View, TizenGestureState.Continuing, 150, 0);
		dispatcher.SendSwipe(recognizer, View, TizenGestureState.Finished, 0, 0);

		Assert.NotNull(swiped);
		Assert.Equal(SwipeDirection.Right, swiped!.Direction);
	}

	[Fact]
	public void SwipeBelowTheThresholdIsNotReported()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new SwipeGestureRecognizer { Direction = SwipeDirection.Right, Threshold = 100 };
		var raised = false;
		recognizer.Swiped += (_, _) => raised = true;

		dispatcher.SendSwipe(recognizer, View, TizenGestureState.Continuing, 10, 0);
		dispatcher.SendSwipe(recognizer, View, TizenGestureState.Finished, 0, 0);

		Assert.False(raised);
	}

	// Tap and pointer became dispatchable in MAUI 11.0.0-preview.7.26426.4 via
	// dotnet/maui#37420 and #37671. These tests use real recognizers, so they prove the public
	// path actually delivers events rather than just that it compiles.

	[Fact]
	public void TapIsDeliveredThroughThePublicSendTapped()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new TapGestureRecognizer();
		var raised = 0;
		recognizer.Tapped += (_, _) => raised++;

		dispatcher.SendTapped(recognizer, View, new Point(12, 34));

		Assert.Equal(1, raised);
	}

	[Fact]
	public void TapReportsThePositionRelativeToItsOwnView()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new TapGestureRecognizer();
		TappedEventArgs? args = null;
		recognizer.Tapped += (_, e) => args = e;

		dispatcher.SendTapped(recognizer, View, new Point(12, 34));

		Assert.NotNull(args);
		Assert.Equal(new Point(12, 34), args!.GetPosition(View));
		Assert.Equal(new Point(12, 34), args.GetPosition(null));
	}

	[Fact]
	public void TapReportsAnUnknownPositionForAnUnrelatedElement()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new TapGestureRecognizer();
		TappedEventArgs? args = null;
		recognizer.Tapped += (_, e) => args = e;

		dispatcher.SendTapped(recognizer, View, new Point(12, 34));

		// Translating between two elements needs both on-screen origins, which the Tizen platform
		// layer does not expose here. MAUI models "unknown" as null, which is honest; returning a
		// view-local coordinate for a different element would be silently wrong.
		Assert.Null(args!.GetPosition(new Label()));
	}

	[Theory]
	[InlineData(TizenPointerAction.Entered)]
	[InlineData(TizenPointerAction.Moved)]
	[InlineData(TizenPointerAction.Pressed)]
	[InlineData(TizenPointerAction.Released)]
	[InlineData(TizenPointerAction.Exited)]
	public void EveryPointerTransitionIsDeliveredThroughItsPublicSendMember(TizenPointerAction action)
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PointerGestureRecognizer();
		var fired = new List<string>();

		recognizer.PointerEntered += (_, _) => fired.Add(nameof(TizenPointerAction.Entered));
		recognizer.PointerMoved += (_, _) => fired.Add(nameof(TizenPointerAction.Moved));
		recognizer.PointerPressed += (_, _) => fired.Add(nameof(TizenPointerAction.Pressed));
		recognizer.PointerReleased += (_, _) => fired.Add(nameof(TizenPointerAction.Released));
		recognizer.PointerExited += (_, _) => fired.Add(nameof(TizenPointerAction.Exited));

		dispatcher.SendPointer(recognizer, View, action, new Point(5, 6));

		Assert.Equal(action.ToString(), Assert.Single(fired));
	}

	[Fact]
	public void PointerReportsThePositionRelativeToItsOwnView()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PointerGestureRecognizer();
		PointerEventArgs? args = null;
		recognizer.PointerMoved += (_, e) => args = e;

		dispatcher.SendPointer(recognizer, View, TizenPointerAction.Moved, new Point(5, 6));

		Assert.NotNull(args);
		Assert.Equal(new Point(5, 6), args!.GetPosition(View));
	}

	// Long press is the ONE gesture this backend detects but cannot raise:
	// LongPressGestureRecognizer.SendLongPressed and SendLongPressing are still internal in
	// 11.0.0-preview.7.26426.4. This test pins that, and fails once they go public.

	[Fact]
	public void LongPressCannotBeRaisedBecauseMauiKeepsTheApiInternal()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new LongPressGestureRecognizer();
		var raised = false;
		recognizer.LongPressed += (_, _) => raised = true;
		recognizer.LongPressing += (_, _) => raised = true;

		dispatcher.SendLongPress(recognizer, View, TizenGestureState.Started, Point.Zero);
		dispatcher.SendLongPress(recognizer, View, TizenGestureState.Finished, Point.Zero);

		Assert.False(raised);
		Assert.False(dispatcher.IsSupported(TizenGestureKind.LongPress));
	}

	[Fact]
	public void LongPressSendMembersAreStillInternalUpstream()
	{
		// The support matrix claims exactly two members are missing. Assert that rather than
		// trusting the claim: when upstream makes them public this fails and points at the
		// dispatcher, the matrix and the test above.
		var type = typeof(LongPressGestureRecognizer);

		foreach (var name in new[] { "SendLongPressed", "SendLongPressing" })
		{
			Assert.Null(type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance));
			Assert.NotNull(type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance));
		}
	}

	[Fact]
	public void UnsupportedGesturesNeverThrow()
	{
		var dispatcher = new TizenGestureDispatcher();

		// Detection must stay harmless: a view with a long-press recognizer should behave exactly
		// as if it had no gestures, not crash the application.
		var exception = Record.Exception(() =>
		{
			dispatcher.SendLongPress(new LongPressGestureRecognizer(), View, TizenGestureState.Started, Point.Zero);
			dispatcher.SendLongPress(new LongPressGestureRecognizer(), View, TizenGestureState.Started, Point.Zero);
		});

		Assert.Null(exception);
	}
}
