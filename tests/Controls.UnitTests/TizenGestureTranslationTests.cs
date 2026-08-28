using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers the gesture translation logic: how native detector events become .NET MAUI gesture
/// events, including running totals, pixel-to-DP conversion and gesture identity.
/// </summary>
public class TizenGestureTranslationTests
{
	const double ScalingFactor = 2d;

	static (THandler Handler, FakeNativeGestureDetector Detector, RecordingGestureDispatcher Dispatcher, Label View) Build<THandler>(
		Func<ITizenNativeGestureDetector, ITizenGestureDispatcher, ITizenPixelScaler, THandler> create)
		where THandler : TizenGestureHandler
	{
		var detector = new FakeNativeGestureDetector();
		var dispatcher = new RecordingGestureDispatcher();
		var view = new Label();
		var handler = create(detector, dispatcher, new TizenPixelScaler(ScalingFactor));
		handler.Attach(new StubViewHandler(view));
		return (handler, detector, dispatcher, view);
	}

	static TizenGestureEventArgs Event(TizenGestureKind kind, TizenGestureState state) => new(kind, state);

	[Fact]
	public void PanAccumulatesDisplacementAndConvertsToDeviceIndependentUnits()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing) { Displacement = new Point(10, 20) });
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing) { Displacement = new Point(10, 20) });
		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Finished));

		Assert.Collection(
			dispatcher.Pans,
			p => Assert.Equal((TizenGestureState.Started, 0d, 0d, 1), p),
			// Native detectors report per-frame displacement, so the handler keeps the running
			// total and converts pixels to DP using the display scaling factor.
			p => Assert.Equal((TizenGestureState.Continuing, 5d, 10d, 1), p),
			p => Assert.Equal((TizenGestureState.Continuing, 10d, 20d, 1), p),
			p => Assert.Equal((TizenGestureState.Finished, 0d, 0d, 1), p));
	}

	[Fact]
	public void PanIncludesStartedDisplacementInTheFirstRunningTotal()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started)
		{
			Displacement = new Point(6, 10),
		});
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing)
		{
			Displacement = new Point(4, 6),
		});

		Assert.Equal((TizenGestureState.Continuing, 5d, 8d, 1), dispatcher.Pans.Last());
	}

	[Theory]
	[InlineData(1, 2)]
	[InlineData(2, 1)]
	public void PanRequiresTheConfiguredTouchCount(int required, int rejected)
	{
		var recognizer = new PanGestureRecognizer { TouchPoints = required };
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started)
		{
			TouchCount = rejected,
		});

		Assert.Empty(dispatcher.Pans);

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started)
		{
			TouchCount = required,
		});
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing)
		{
			TouchCount = required,
			Displacement = new Point(10, 20),
		});

		Assert.Collection(
			dispatcher.Pans,
			p => Assert.Equal(TizenGestureState.Started, p.State),
			p => Assert.Equal((TizenGestureState.Continuing, 5d, 10d, 1), p));
	}

	[Fact]
	public void PanTouchPointChangesAfterAttachmentKeepTheNativeConfiguration()
	{
		var recognizer = new PanGestureRecognizer { TouchPoints = 2 };
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		recognizer.TouchPoints = 1;
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started)
		{
			TouchCount = 2,
		});
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing)
		{
			TouchCount = 2,
			Displacement = new Point(10, 20),
		});

		Assert.Collection(
			dispatcher.Pans,
			p => Assert.Equal(TizenGestureState.Started, p.State),
			p => Assert.Equal((TizenGestureState.Continuing, 5d, 10d, 1), p));
	}

	[Fact]
	public void EachPanGetsItsOwnGestureIdAndFreshTotals()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing) { Displacement = new Point(100, 0) });
		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Finished));

		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Continuing) { Displacement = new Point(4, 0) });

		var second = dispatcher.Pans.Last();
		Assert.Equal(2, second.GestureId);
		Assert.Equal(2d, second.X);
	}

	[Fact]
	public void CanceledPanIsReported()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));
		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Canceled));

		Assert.Equal(TizenGestureState.Canceled, dispatcher.Pans.Last().State);
	}

	[Theory]
	[InlineData(TizenGestureState.Finished, 0)]
	[InlineData(TizenGestureState.Canceled, 1)]
	public void AcceptedMultiTouchPanReportsTerminalStateAfterTouchesLift(
		TizenGestureState terminalState,
		int terminalTouchCount)
	{
		var recognizer = new PanGestureRecognizer { TouchPoints = 2 };
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started)
		{
			TouchCount = 2,
		});
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, terminalState)
		{
			TouchCount = terminalTouchCount,
		});

		Assert.Equal(terminalState, dispatcher.Pans.Last().State);
	}

	[Fact]
	public void PanLeavesTheNativeGestureUnconsumedSoOverlappingGesturesKeepWorking()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, _, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		var args = detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pan, TizenGestureState.Started) { Handled = true });

		Assert.False(args.Handled);
	}

	[Fact]
	public void SwipeAccumulatesMovementAndFinishesWithDetection()
	{
		var recognizer = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenSwipeGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Continuing) { Displacement = new Point(40, 0) });
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Continuing) { Displacement = new Point(40, 0) });
		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Finished));

		Assert.Collection(
			dispatcher.Swipes,
			s => Assert.Equal((TizenGestureState.Continuing, 20d, 0d), s),
			s => Assert.Equal((TizenGestureState.Continuing, 40d, 0d), s),
			s => Assert.Equal((TizenGestureState.Finished, 0d, 0d), s));
	}

	[Fact]
	public void SwipeIncludesStartedDisplacementWhenApplyingTheThreshold()
	{
		var recognizer = new SwipeGestureRecognizer
		{
			Direction = SwipeDirection.Right,
			Threshold = 100,
		};
		var detector = new FakeNativeGestureDetector();
		var view = new Label();
		using var handler = new TizenSwipeGestureHandler(
			recognizer,
			detector,
			new TizenGestureDispatcher(),
			new TizenPixelScaler(ScalingFactor));
		var raised = false;
		recognizer.Swiped += (_, _) => raised = true;
		handler.Attach(new StubViewHandler(view));

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Started)
		{
			Displacement = new Point(180, 0),
		});
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Continuing)
		{
			Displacement = new Point(30, 0),
		});
		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Finished));

		Assert.True(raised);
	}

	[Fact]
	public void SwipeTotalsResetBetweenGestures()
	{
		var recognizer = new SwipeGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenSwipeGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Continuing) { Displacement = new Point(100, 0) });
		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Finished));

		detector.Raise(Event(TizenGestureKind.Swipe, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Swipe, TizenGestureState.Continuing) { Displacement = new Point(6, 0) });

		Assert.Equal(3d, dispatcher.Swipes.Last().X);
	}

	[Fact]
	public void PinchCombinesNativeScaleWithTheViewScaleAtGestureStart()
	{
		var recognizer = new PinchGestureRecognizer();
		var (handler, detector, dispatcher, view) = Build((d, disp, s) => new TizenPinchGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		view.Scale = 2d;

		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Started));
		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pinch, TizenGestureState.Continuing) { Scale = 1.5d });

		// The native scale is relative to the start of the gesture, so it is composed with the
		// view scale captured when the pinch began: 1 + (1.5 - 1) * 2 == 2.
		Assert.Equal(2d, dispatcher.Pinches.Last().Scale);
	}

	[Fact]
	public void PinchOriginIsExpressedAsAFractionOfTheView()
	{
		var recognizer = new PinchGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPinchGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pinch, TizenGestureState.Started)
		{
			LocalPosition = new Point(50, 100),
			ViewSize = new Size(200, 400),
		});

		Assert.Equal(new Point(0.25, 0.25), dispatcher.Pinches.Single().Origin);
	}

	[Fact]
	public void PinchOnAnUnmeasuredViewDegradesToTheOriginInsteadOfProducingNaN()
	{
		var recognizer = new PinchGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPinchGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pinch, TizenGestureState.Started)
		{
			LocalPosition = new Point(50, 100),
			ViewSize = Size.Zero,
		});

		var origin = dispatcher.Pinches.Single().Origin;
		Assert.Equal(Point.Zero, origin);
		Assert.False(double.IsNaN(origin.X) || double.IsInfinity(origin.X));
	}

	[Fact]
	public void PinchReportsEndAndCancel()
	{
		var recognizer = new PinchGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPinchGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Started));
		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Finished));
		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Started));
		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Canceled));

		Assert.Equal(
			new[] { TizenGestureState.Started, TizenGestureState.Finished, TizenGestureState.Started, TizenGestureState.Canceled },
			dispatcher.Pinches.Select(p => p.State));
	}

	[Fact]
	public void TapIsOnlyReportedWhenTheRequiredTapCountIsReached()
	{
		var recognizer = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenTapGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Tap, TizenGestureState.Finished) { TapCount = 1 });
		Assert.Empty(dispatcher.Taps);

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Tap, TizenGestureState.Finished)
		{
			TapCount = 2,
			LocalPosition = new Point(30, 60),
		});

		Assert.Equal(new Point(15, 30), dispatcher.Taps.Single());
	}

	[Fact]
	public void LongPressReportsTheFullGestureSequenceInOrder()
	{
		var recognizer = new LongPressGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenLongPressGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(new TizenGestureEventArgs(TizenGestureKind.LongPress, TizenGestureState.Started) { LocalPosition = new Point(8, 16) });
		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Continuing));
		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Continuing));
		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Finished));

		// Continuing must survive translation. dotnet/maui's in-box Tizen handler drops it, so a
		// Tizen long press never reports Running and an app tracking the gesture sees Started jump
		// straight to Completed.
		Assert.Equal(
			new[]
			{
				TizenGestureState.Started,
				TizenGestureState.Continuing,
				TizenGestureState.Continuing,
				TizenGestureState.Finished,
			},
			dispatcher.LongPresses.Select(l => l.State));

		Assert.Equal(new Point(4, 8), dispatcher.LongPresses[0].Position);
	}

	[Fact]
	public void LongPressReportsCancellation()
	{
		var recognizer = new LongPressGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenLongPressGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Started));
		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Continuing));
		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Canceled));

		Assert.Equal(
			new[] { TizenGestureState.Started, TizenGestureState.Continuing, TizenGestureState.Canceled },
			dispatcher.LongPresses.Select(l => l.State));

		// A canceled press must never be reported as finished - that is what would fire
		// LongPressed and the recognizer's command.
		Assert.DoesNotContain(TizenGestureState.Finished, dispatcher.LongPresses.Select(l => l.State));
	}

	[Fact]
	public void LongPressIgnoresStatesThatCarryNoMeaning()
	{
		var recognizer = new LongPressGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenLongPressGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.LongPress, TizenGestureState.Possible));

		Assert.Empty(dispatcher.LongPresses);
	}

	[Fact]
	public void PointerReportsEveryTransition()
	{
		var recognizer = new PointerGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPointerGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		foreach (var action in Enum.GetValues<TizenPointerAction>())
		{
			detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pointer, TizenGestureState.Finished)
			{
				PointerAction = action,
				LocalPosition = new Point(2, 4),
			});
		}

		Assert.Equal(Enum.GetValues<TizenPointerAction>(), dispatcher.Pointers.Select(p => p.Action));
		Assert.All(dispatcher.Pointers, p => Assert.Equal(new Point(1, 2), p.Position));
	}

	[Fact]
	public void HandlersIgnoreEventsForOtherGestureKinds()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));
		using var _handler = handler;

		detector.Raise(Event(TizenGestureKind.Pinch, TizenGestureState.Started));

		Assert.Empty(dispatcher.Pans);
	}

	[Fact]
	public void EventsAreIgnoredAfterTheHandlerIsDisposed()
	{
		var recognizer = new PanGestureRecognizer();
		var (handler, detector, dispatcher, _) = Build((d, disp, s) => new TizenPanGestureHandler(recognizer, d, disp, s));

		handler.Dispose();
		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));

		Assert.Empty(dispatcher.Pans);
		Assert.True(detector.Disposed);
	}

	[Fact]
	public void EventsAreIgnoredWhenTheHandlerHasNoVirtualView()
	{
		var detector = new FakeNativeGestureDetector();
		var dispatcher = new RecordingGestureDispatcher();
		using var handler = new TizenPanGestureHandler(new PanGestureRecognizer(), detector, dispatcher, new TizenPixelScaler());

		handler.Attach(new StubViewHandler(virtualView: null));
		detector.Raise(Event(TizenGestureKind.Pan, TizenGestureState.Started));

		Assert.Empty(dispatcher.Pans);
	}

	[Fact]
	public void AttachIsANoOpWhenTheHandlerHasNoPlatformViewYet()
	{
		var detector = new FakeNativeGestureDetector();
		using var handler = new TizenPanGestureHandler(
			new PanGestureRecognizer(),
			detector,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());

		handler.Attach(new StubViewHandler(new Label(), platformView: null) { PlatformView = null });

		Assert.False(detector.IsAttached);
	}

	[Fact]
	public void AttachingToADifferentPlatformViewMovesTheDetector()
	{
		var detector = new FakeNativeGestureDetector();
		using var handler = new TizenPanGestureHandler(
			new PanGestureRecognizer(),
			detector,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());

		var view = new Label();
		var first = new object();
		var second = new object();

		handler.Attach(new StubViewHandler(view, first));
		handler.Attach(new StubViewHandler(view, second));

		Assert.Same(second, detector.AttachedView);
		Assert.Equal(2, detector.AttachCount);
		Assert.Equal(1, detector.DetachCount);
	}
}
