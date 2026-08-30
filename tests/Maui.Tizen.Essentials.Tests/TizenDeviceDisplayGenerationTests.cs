using System;
using System.Collections.Generic;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;
using TizenDeviceOrientation = Tizen.Applications.DeviceOrientation;

namespace Maui.Tizen.Essentials.Tests;

public class TizenDeviceDisplayGenerationTests
{
	[Fact]
	public void RetainedAndQueuedOldNativeCallbacksCannotMutateReplacementState()
	{
		var dispatcher = new ManualCallbackDispatcher();
		var native = new FakeDeviceDisplayNative();
		using var display = new TizenDeviceDisplay(
			native,
			new TizenNativeCallbackCoordinator(dispatcher));
		var firstCalls = 0;
		var replacementCalls = 0;
		EventHandler<DisplayInfoChangedEventArgs> first = (_, _) => firstCalls++;
		EventHandler<DisplayInfoChangedEventArgs> replacement = (_, _) => replacementCalls++;

		display.MainDisplayInfoChanged += first;
		var oldNative = Assert.Single(native.RetainedCallbacks);
		oldNative(TizenDeviceOrientation.Orientation_90);

		display.MainDisplayInfoChanged -= first;
		display.MainDisplayInfoChanged += replacement;
		var currentNative = native.CurrentCallback;
		Assert.NotNull(currentNative);
		Assert.NotSame(oldNative, currentNative);

		// Retained callback after replacement is rejected immediately. The callback queued before
		// replacement is rejected again when the deferred dispatcher action executes.
		oldNative(TizenDeviceOrientation.Orientation_180);
		dispatcher.RunAll();

		Assert.Equal(0, firstCalls);
		Assert.Equal(0, replacementCalls);
		Assert.Equal(DisplayRotation.Rotation0, display.MainDisplayInfo.Rotation);
		Assert.Equal(DisplayOrientation.Portrait, display.MainDisplayInfo.Orientation);

		currentNative!(TizenDeviceOrientation.Orientation_90);
		dispatcher.RunAll();

		Assert.Equal(1, replacementCalls);
		Assert.Equal(DisplayRotation.Rotation90, display.MainDisplayInfo.Rotation);
		Assert.Equal(DisplayOrientation.Landscape, display.MainDisplayInfo.Orientation);
	}

	sealed class ManualCallbackDispatcher : ITizenNativeCallbackDispatcher
	{
		readonly Queue<Action> _work = [];

		public void PostDeferred(Action action) => _work.Enqueue(action);

		public void RunAll()
		{
			while (_work.TryDequeue(out var action))
				action();
		}
	}

	sealed class FakeDeviceDisplayNative : ITizenDeviceDisplayNative
	{
		public List<Action<TizenDeviceOrientation>> RetainedCallbacks { get; } = [];

		public Action<TizenDeviceOrientation>? CurrentCallback { get; private set; }

		public TizenDisplayMetrics GetMetrics() => new(100, 200, 160);

		public Action Subscribe(Action<TizenDeviceOrientation> callback)
		{
			CurrentCallback = callback;
			RetainedCallbacks.Add(callback);
			return () =>
			{
				if (ReferenceEquals(CurrentCallback, callback))
					CurrentCallback = null;
			};
		}
	}
}
