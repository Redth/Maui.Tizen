using System;
using System.Collections.Generic;
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
	[InlineData(TizenGestureKind.Tap, false)]
	[InlineData(TizenGestureKind.LongPress, false)]
	[InlineData(TizenGestureKind.Pointer, false)]
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

	// The following gestures are detected by the Tizen backend but cannot currently be raised.
	// .NET MAUI keeps TapGestureRecognizer.SendTapped, LongPressGestureRecognizer.SendLongPressing
	// / SendLongPressed and the PointerGestureRecognizer send members internal, and only exposes
	// controller interfaces for pan, pinch and swipe. These tests pin that reality so the day the
	// upstream API lands they fail loudly and the dispatcher can be completed.

	[Fact]
	public void TapCannotBeRaisedBecauseMauiKeepsTheApiInternal()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new TapGestureRecognizer();
		var raised = false;
		recognizer.Tapped += (_, _) => raised = true;

		dispatcher.SendTapped(recognizer, View, Point.Zero);

		Assert.False(raised);
		Assert.False(dispatcher.IsSupported(TizenGestureKind.Tap));
	}

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
	public void PointerCannotBeRaisedBecauseMauiKeepsTheApiInternal()
	{
		var dispatcher = new TizenGestureDispatcher();
		var recognizer = new PointerGestureRecognizer();
		var raised = false;
		recognizer.PointerEntered += (_, _) => raised = true;
		recognizer.PointerMoved += (_, _) => raised = true;
		recognizer.PointerPressed += (_, _) => raised = true;
		recognizer.PointerReleased += (_, _) => raised = true;
		recognizer.PointerExited += (_, _) => raised = true;

		foreach (var action in Enum.GetValues<TizenPointerAction>())
		{
			dispatcher.SendPointer(recognizer, View, action, Point.Zero);
		}

		Assert.False(raised);
		Assert.False(dispatcher.IsSupported(TizenGestureKind.Pointer));
	}

	[Fact]
	public void UnsupportedGesturesNeverThrow()
	{
		var dispatcher = new TizenGestureDispatcher();

		// Detection must stay harmless: a view with a tap recognizer should behave exactly as if
		// it had no gestures, not crash the application.
		var exception = Record.Exception(() =>
		{
			dispatcher.SendTapped(new TapGestureRecognizer(), View, Point.Zero);
			dispatcher.SendTapped(new TapGestureRecognizer(), View, Point.Zero);
		});

		Assert.Null(exception);
	}
}
