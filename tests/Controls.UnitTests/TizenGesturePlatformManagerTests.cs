using System;
using System.Linq;
using Microsoft.Maui.Controls;
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
}
