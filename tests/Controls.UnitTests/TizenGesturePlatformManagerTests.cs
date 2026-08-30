using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers the lifecycle of the gesture infrastructure: factory behaviour, detector attach and
/// detach, and how recognizer collection and enabled-state changes are tracked.
/// </summary>
public class TizenGesturePlatformManagerTests
{
	static (TizenGesturePlatformManagerFactory Factory, FakeNativeGestureDetectorFactory Detectors) CreateFactory(
		params TizenGestureKind[] unsupported)
	{
		var detectors = new FakeNativeGestureDetectorFactory(unsupported);
		var handlerFactory = new TizenGestureHandlerFactory(detectors, new RecordingGestureDispatcher(), new TizenPixelScaler());
		return (new TizenGesturePlatformManagerFactory(handlerFactory), detectors);
	}

	static Label ViewWith(params IGestureRecognizer[] recognizers)
	{
		var label = new Label();

		foreach (var recognizer in recognizers)
		{
			label.GestureRecognizers.Add(recognizer);
		}

		return label;
	}

	static Label ViewWithPointerOverState()
	{
		var label = new Label { Opacity = 1d };
		var normal = new VisualState { Name = VisualStateManager.CommonStates.Normal };
		normal.Setters.Add(new Setter
		{
			Property = VisualElement.OpacityProperty,
			Value = 1d,
		});
		var pointerOver = new VisualState { Name = VisualStateManager.CommonStates.PointerOver };
		pointerOver.Setters.Add(new Setter
		{
			Property = VisualElement.OpacityProperty,
			Value = 0.5d,
		});
		var commonStates = new VisualStateGroup { Name = nameof(VisualStateManager.CommonStates) };
		commonStates.States.Add(normal);
		commonStates.States.Add(pointerOver);
		var groups = new VisualStateGroupList { commonStates };
		VisualStateManager.SetVisualStateGroups(label, groups);
		return label;
	}

	[Fact]
	public void FactoryImplementsThePublicMauiContract()
	{
		var (factory, _) = CreateFactory();

		Assert.IsAssignableFrom<IGesturePlatformManagerFactory>(factory);
	}

	[Fact]
	public void FactoryReturnsANewManagerForEveryConnection()
	{
		var (factory, _) = CreateFactory();
		var handler = new StubViewHandler(ViewWith());

		// .NET MAUI disposes and recreates the manager on every connect or handler change, so a
		// cached instance would be handed back already disposed.
		var first = factory.CreateGesturePlatformManager(handler);
		var second = factory.CreateGesturePlatformManager(handler);

		Assert.NotSame(first, second);
	}

	[Fact]
	public void ManagerWorksWithAPlainViewHandler()
	{
		var (factory, detectors) = CreateFactory();
		var handler = new StubViewHandler(ViewWith(new PanGestureRecognizer()));

		// The stub deliberately does not implement IPlatformViewHandler. The built-in Apple and
		// Windows managers require it; the Tizen backend must not. The type is platform-only, so
		// the check is by name rather than by reference.
		Assert.DoesNotContain(
			handler.GetType().GetInterfaces(),
			i => i.Name == "IPlatformViewHandler");

		using var manager = factory.CreateGesturePlatformManager(handler);

		Assert.Single(detectors.Created);
		Assert.True(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void ExistingRecognizersAreAttachedOnConnect()
	{
		var (factory, detectors) = CreateFactory();
		var handler = new StubViewHandler(ViewWith(new PanGestureRecognizer(), new PinchGestureRecognizer()));

		using var manager = factory.CreateGesturePlatformManager(handler);

		Assert.Equal(2, detectors.Created.Count);
		Assert.All(detectors.Created, d => Assert.True(d.IsAttached));
	}

	[Fact]
	public void NativeTapDetectorAndManagedHandlerShareOneConfigurationSnapshot()
	{
		var (factory, detectors) = CreateFactory();
		var recognizer = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		var handler = new StubViewHandler(ViewWith(recognizer));

		using var manager = factory.CreateGesturePlatformManager(handler);
		recognizer.NumberOfTapsRequired = 1;

		var request = Assert.Single(detectors.Requests);
		Assert.Equal(2, request.Configuration.RequiredTapCount);
	}

	[Fact]
	public void DefaultNativeGestureConfigurationUsesSafeSingleCounts()
	{
		var configuration = default(TizenNativeGestureConfiguration);

		Assert.Equal(1, configuration.RequiredTapCount);
		Assert.Equal(1, configuration.RequiredTouchCount);
	}

	[Fact]
	public void ContainerViewIsPreferredOverPlatformView()
	{
		var (factory, detectors) = CreateFactory();
		var platformView = new object();
		var containerView = new object();
		var handler = new StubViewHandler(ViewWith(new PanGestureRecognizer()), platformView, containerView);

		using var manager = factory.CreateGesturePlatformManager(handler);

		Assert.Same(containerView, detectors.Created[0].AttachedView);
	}

	[Fact]
	public void PlatformViewIsUsedWhenThereIsNoContainer()
	{
		var (factory, detectors) = CreateFactory();
		var platformView = new object();
		var handler = new StubViewHandler(ViewWith(new PanGestureRecognizer()), platformView);

		using var manager = factory.CreateGesturePlatformManager(handler);

		Assert.Same(platformView, detectors.Created[0].AttachedView);
	}

	[Fact]
	public void NoDetectorIsCreatedForAViewWithoutRecognizers()
	{
		var (factory, detectors) = CreateFactory();
		var handler = new StubViewHandler(ViewWith());

		using var manager = (TizenGesturePlatformManager)factory.CreateGesturePlatformManager(handler);

		Assert.Empty(detectors.Created);
		Assert.Null(manager.GestureDetector);
	}

	[Fact]
	public void RecognizersAddedAfterConnectAreAttached()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith();
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);
		view.GestureRecognizers.Add(new PanGestureRecognizer());

		Assert.Single(detectors.Created);
		Assert.True(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void FrameworkPointerOverRecognizerIsAttachedAndDispatched()
	{
		var detectors = new FakeNativeGestureDetectorFactory();
		var handlerFactory = new TizenGestureHandlerFactory(
			detectors,
			new TizenGestureDispatcher(),
			new TizenPixelScaler());
		var factory = new TizenGesturePlatformManagerFactory(handlerFactory);
		var view = ViewWithPointerOverState();
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);

		Assert.Empty(view.GestureRecognizers);
		var request = Assert.Single(detectors.Requests);
		Assert.Equal(TizenGestureKind.Pointer, request.Kind);
		Assert.IsType<PointerGestureRecognizer>(request.Recognizer);
		Assert.True(request.Detector.IsAttached);

		request.Detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pointer, TizenGestureState.Finished)
		{
			PointerAction = TizenPointerAction.Entered,
		});
		Assert.Equal(0.5d, view.Opacity);

		request.Detector.Raise(new TizenGestureEventArgs(TizenGestureKind.Pointer, TizenGestureState.Finished)
		{
			PointerAction = TizenPointerAction.Exited,
		});
		Assert.Equal(1d, view.Opacity);
	}

	[Fact]
	public void CompositeRecognizerUpdatesAreTrackedUntilDisposal()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith();
		var composite = ((IGestureController)view).CompositeGestureRecognizers;
		var handler = new StubViewHandler(view);
		var manager = factory.CreateGesturePlatformManager(handler);
		var pointer = new PointerGestureRecognizer();

		composite.Add(pointer);

		var detector = Assert.Single(detectors.Created);
		Assert.True(detector.IsAttached);

		composite.Remove(pointer);

		Assert.True(detector.Disposed);
		Assert.False(detector.IsAttached);

		manager.Dispose();
		composite.Add(new PointerGestureRecognizer());

		Assert.Single(detectors.Created);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void PointerLeaveRequirementIsSharedAndRestoresThePreviousValue(bool originalValue)
	{
		var requirement = new SharedBooleanPropertyLease<BooleanPropertyTarget>(
			static target => target.Value,
			static (target, value) => target.Value = value);
		var target = new BooleanPropertyTarget { Value = originalValue };
		var first = requirement.Acquire(target);
		var second = requirement.Acquire(target);

		Assert.True(target.Value);

		first.Dispose();
		first.Dispose();

		Assert.True(target.Value);

		second.Dispose();

		Assert.Equal(originalValue, target.Value);
	}

	[Fact]
	public async Task PointerLeaveRequirementLeaseCanBeDisposedConcurrently()
	{
		var requirement = new SharedBooleanPropertyLease<BooleanPropertyTarget>(
			static target => target.Value,
			static (target, value) => target.Value = value);
		var target = new BooleanPropertyTarget();
		var lease = requirement.Acquire(target);
		using var start = new ManualResetEventSlim();
		var disposals = Enumerable.Range(0, 32)
			.Select(_ => Task.Run(() =>
			{
				start.Wait();
				lease.Dispose();
			}))
			.ToArray();

		start.Set();
		await Task.WhenAll(disposals);

		Assert.False(target.Value);
	}

	[Fact]
	public void RemovingARecognizerDisposesItsDetector()
	{
		var (factory, detectors) = CreateFactory();
		var recognizer = new PanGestureRecognizer();
		var view = ViewWith(recognizer);
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);
		view.GestureRecognizers.Remove(recognizer);

		Assert.True(detectors.Created[0].Disposed);
		Assert.False(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void ReplacingARecognizerSwapsTheDetector()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);
		view.GestureRecognizers[0] = new PinchGestureRecognizer();

		Assert.Equal(2, detectors.Created.Count);
		Assert.True(detectors.Created[0].Disposed);
		Assert.True(detectors.Created[1].IsAttached);
	}

	[Fact]
	public void ClearingTheCollectionDisposesEveryDetector()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer(), new PinchGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = (TizenGesturePlatformManager)factory.CreateGesturePlatformManager(handler);
		view.GestureRecognizers.Clear();

		Assert.All(detectors.Created, d => Assert.True(d.Disposed));
		Assert.Equal(0, manager.GestureDetector!.Count);
	}

	[Fact]
	public void DisablingTheElementDetachesDetectors()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);
		Assert.True(detectors.Created[0].IsAttached);

		view.IsEnabled = false;
		Assert.False(detectors.Created[0].IsAttached);

		view.IsEnabled = true;
		Assert.True(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void InputTransparentDetachesDetectors()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);

		view.InputTransparent = true;
		Assert.False(detectors.Created[0].IsAttached);

		view.InputTransparent = false;
		Assert.True(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void RecognizersAddedWhileDisabledAreNotAttached()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith();
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);
		view.IsEnabled = false;
		view.GestureRecognizers.Add(new PanGestureRecognizer());

		Assert.Single(detectors.Created);
		Assert.False(detectors.Created[0].IsAttached);

		view.IsEnabled = true;
		Assert.True(detectors.Created[0].IsAttached);
	}

	[Fact]
	public void DisposeReleasesDetectorsAndStopsTrackingTheCollection()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		var manager = factory.CreateGesturePlatformManager(handler);
		manager.Dispose();

		Assert.True(detectors.Created[0].Disposed);

		// Adding after disposal must not resurrect the detector graph.
		view.GestureRecognizers.Add(new PinchGestureRecognizer());
		Assert.Single(detectors.Created);
	}

	[Fact]
	public void DisposeIsIdempotent()
	{
		var (factory, _) = CreateFactory();
		var manager = factory.CreateGesturePlatformManager(new StubViewHandler(ViewWith(new PanGestureRecognizer())));

		manager.Dispose();
		manager.Dispose();
	}

	[Fact]
	public void HandlerDisposeAttemptsUnsubscribeDetachAndNativeDisposeWhenEachThrows()
	{
		var detector = new FakeNativeGestureDetector();
		var handler = new TizenPanGestureHandler(
			new PanGestureRecognizer(),
			detector,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());
		handler.Attach(new StubViewHandler(new Label()));
		var unsubscribe = new InvalidOperationException("unsubscribe");
		var detach = new InvalidOperationException("detach");
		var dispose = new InvalidOperationException("dispose");
		detector.UnsubscribeFailure = unsubscribe;
		detector.DetachFailure = detach;
		detector.DisposeFailure = dispose;

		var failure = Assert.Throws<AggregateException>(handler.Dispose);

		Assert.Contains(unsubscribe, failure.InnerExceptions);
		Assert.Contains(detach, failure.InnerExceptions);
		Assert.Contains(dispose, failure.InnerExceptions);
		Assert.Equal(1, detector.UnsubscribeCount);
		Assert.Equal(1, detector.DetachCount);
		Assert.Equal(1, detector.DisposeCount);
		Assert.False(detector.IsAttached);
		Assert.True(detector.Disposed);
	}

	[Fact]
	public void ManagerDisposeClearsOwnershipAndAttemptsEveryCompositeChild()
	{
		var detectors = new FakeNativeGestureDetectorFactory();
		var created = 0;
		detectors.ConfigureDetector = detector =>
		{
			if (created++ == 0)
			{
				detector.DetachFailure = new InvalidOperationException("first detach");
				detector.DisposeFailure = new InvalidOperationException("first dispose");
			}
		};
		var handlerFactory = new TizenGestureHandlerFactory(
			detectors,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());
		var view = ViewWith(new PanGestureRecognizer());
		((IGestureController)view).CompositeGestureRecognizers.Add(new PointerGestureRecognizer());
		var logger = new RecordingLogger<TizenGesturePlatformManager>();
		var manager = new TizenGesturePlatformManager(new StubViewHandler(view), handlerFactory, logger);

		var failure = Record.Exception(manager.Dispose);

		Assert.Null(failure);
		Assert.Equal(2, detectors.Created.Count);
		Assert.All(detectors.Created, detector => Assert.True(detector.Disposed));
		Assert.Equal(0, manager.GestureDetector!.Count);
		Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);

		view.GestureRecognizers.Add(new PinchGestureRecognizer());
		Assert.Equal(2, detectors.Created.Count);
	}

	[Fact]
	public void ManagerDisposeStillReturnsWhenFailureLoggingThrows()
	{
		var detectors = new FakeNativeGestureDetectorFactory
		{
			ConfigureDetector = detector =>
				detector.DisposeFailure = new InvalidOperationException("dispose"),
		};
		var handlerFactory = new TizenGestureHandlerFactory(
			detectors,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());
		var logger = new RecordingLogger<TizenGesturePlatformManager> { ThrowOnLog = true };
		var manager = new TizenGesturePlatformManager(
			new StubViewHandler(ViewWith(new PanGestureRecognizer())),
			handlerFactory,
			logger);

		Assert.Null(Record.Exception(manager.Dispose));
		Assert.True(Assert.Single(detectors.Created).Disposed);
	}

	[Fact]
	public void HandlerConstructionFailureStillDisposesTheNativeDetector()
	{
		var detectors = new FakeNativeGestureDetectorFactory
		{
			ConfigureDetector = detector =>
				detector.SubscribeFailure = new InvalidOperationException("subscribe"),
		};
		var factory = new TizenGestureHandlerFactory(
			detectors,
			new RecordingGestureDispatcher(),
			new TizenPixelScaler());

		Assert.Throws<InvalidOperationException>(
			() => factory.CreateHandler(new PanGestureRecognizer()));

		var detector = Assert.Single(detectors.Created);
		Assert.True(detector.Disposed);
		Assert.Equal(1, detector.DisposeCount);
	}

	[Fact]
	public void UnsupportedGestureKindsProduceNoHandler()
	{
		var (factory, detectors) = CreateFactory(TizenGestureKind.Pinch);
		var view = ViewWith(new PinchGestureRecognizer(), new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = (TizenGesturePlatformManager)factory.CreateGesturePlatformManager(handler);

		// The pinch recognizer is skipped entirely rather than throwing, so a profile without
		// pinch support still runs the rest of the view's gestures.
		Assert.Single(detectors.Created);
		Assert.Equal(1, manager.GestureDetector!.Count);
	}

	[Fact]
	public void UnknownRecognizerTypesAreIgnored()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new CustomRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = (TizenGesturePlatformManager)factory.CreateGesturePlatformManager(handler);

		Assert.Empty(detectors.Created);
	}

	[Fact]
	public void DragAndDropRecognizersAreNotSupported()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new DragGestureRecognizer(), new DropGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);

		// See docs/tizen-gesture-support-matrix.md: NUI has no view-level drag/drop gesture that
		// maps onto these recognizers, and .NET MAUI exposes no public dispatch for them.
		Assert.Empty(detectors.Created);
	}

	[Fact]
	public void AttachIsNotRepeatedForTheSamePlatformView()
	{
		var (factory, detectors) = CreateFactory();
		var view = ViewWith(new PanGestureRecognizer());
		var handler = new StubViewHandler(view);

		using var manager = factory.CreateGesturePlatformManager(handler);

		view.IsEnabled = true;
		view.IsEnabled = true;

		Assert.Equal(1, detectors.Created[0].AttachCount);
	}

	sealed class CustomRecognizer : Element, IGestureRecognizer
	{
	}

	sealed class BooleanPropertyTarget
	{
		public bool Value { get; set; }
	}
}
