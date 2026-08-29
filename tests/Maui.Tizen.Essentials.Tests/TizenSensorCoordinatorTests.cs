using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenSensorCoordinatorTests
{
	[Fact]
	public void MultipleWrappersShareOneNativeLifetime()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var sensor = new FakeSensor();
		var first = new object();
		var second = new object();
		var firstSubscribed = false;
		var secondSubscribed = false;

		Start(coordinator, first, sensor, 200, () => firstSubscribed = true, () => firstSubscribed = false);
		Start(coordinator, second, sensor, 20, () => secondSubscribed = true, () => secondSubscribed = false);

		Assert.Equal(1, sensor.StartCalls);
		Assert.Equal(20u, sensor.Interval);
		Assert.True(firstSubscribed);
		Assert.True(secondSubscribed);

		Stop(coordinator, first, sensor);
		Assert.Equal(0, sensor.StopCalls);
		Assert.False(firstSubscribed);
		Assert.True(secondSubscribed);

		Stop(coordinator, second, sensor);
		Assert.Equal(1, sensor.StopCalls);
		Assert.Equal(1, sensor.ResetCalls);
		Assert.False(secondSubscribed);
	}

	[Fact]
	public void FailedStartUnsubscribesStopsAndResetsBeforeAnotherStart()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var sensor = new FakeSensor { ThrowOnStart = true };
		var subscribed = false;

		Assert.Throws<InvalidOperationException>(() =>
			Start(
				coordinator,
				new object(),
				sensor,
				60,
				() => subscribed = true,
				() => subscribed = false));

		Assert.False(subscribed);
		Assert.Equal(0, coordinator.ActiveCount);
		Assert.Equal(1, sensor.StopCalls);
		Assert.Equal(1, sensor.ResetCalls);

		sensor.ThrowOnStart = false;
		Start(coordinator, new object(), sensor, 60, static () => { }, static () => { });
		Assert.Equal(2, sensor.StartCalls);
	}

	[Fact]
	public void FailedSecondOwnerDoesNotStopOrResetTheSharedSensor()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var sensor = new FakeSensor();
		var first = new object();

		Start(coordinator, first, sensor, 60, static () => { }, static () => { });

		Assert.Throws<InvalidOperationException>(() =>
			coordinator.Start(
				new object(),
				() => throw new InvalidOperationException("must reuse the cached sensor"),
				20,
				static (native, value) => native.Interval = value,
				static _ => throw new InvalidOperationException("subscribe failed"),
				native => native.StartCalls++,
				native => native.StopCalls++,
				native => native.ResetCalls++,
				static () => { },
				static () => { }));

		Assert.Equal(1, coordinator.ActiveCount);
		Assert.Equal(1, sensor.StartCalls);
		Assert.Equal(0, sensor.StopCalls);
		Assert.Equal(0, sensor.ResetCalls);
		Assert.Equal(60u, sensor.Interval);
	}

	[Fact]
	public void SessionStateIsInitializedBeforeNativeStart()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var sensor = new FakeSensor();
		var initialized = false;

		coordinator.Start(
			new object(),
			() => sensor,
			60,
			static (native, value) => native.Interval = value,
			static _ => static () => { },
			native =>
			{
				Assert.True(initialized);
				native.StartCalls++;
			},
			native => native.StopCalls++,
			native => native.ResetCalls++,
			() => initialized = true,
			() => initialized = false);
	}

	[Fact]
	public async Task StopCompletesSerializedCleanupBeforeANewStart()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var sensor = new FakeSensor();
		var first = new object();
		var second = new object();
		using var stopEntered = new ManualResetEventSlim();
		using var releaseStop = new ManualResetEventSlim();

		Start(coordinator, first, sensor, 60, static () => { }, static () => { });
		var stop = Task.Run(() =>
			coordinator.Stop(
				first,
				static () => { },
				_ =>
				{
					stopEntered.Set();
					releaseStop.Wait(TestContext.Current.CancellationToken);
					sensor.StopCalls++;
				},
				_ => sensor.ResetCalls++,
				static (_, _) => { },
				static () => { }),
			TestContext.Current.CancellationToken);

		stopEntered.Wait(TestContext.Current.CancellationToken);
		var start = Task.Run(
			() => Start(coordinator, second, sensor, 20, static () => { }, static () => { }),
			TestContext.Current.CancellationToken);
		await Task.Delay(25, TestContext.Current.CancellationToken);
		Assert.False(start.IsCompleted);

		releaseStop.Set();
		await Task.WhenAll(stop, start);
		Assert.Equal(2, sensor.StartCalls);
	}

	[Fact]
	public async Task LastOwnerStopFinishesBeforeNewSensorAcquisition()
	{
		var coordinator = new TizenSensorLifetimeCoordinator<FakeSensor>();
		var firstSensor = new FakeSensor();
		var secondSensor = new FakeSensor();
		var current = firstSensor;
		var invalidated = false;
		var first = new object();
		var second = new object();
		using var stopEntered = new ManualResetEventSlim();
		using var releaseStop = new ManualResetEventSlim();

		coordinator.Start(
			first,
			() => current,
			60,
			static (native, value) => native.Interval = value,
			static _ => static () => { },
			native => native.StartCalls++,
			native => native.StopCalls++,
			native => native.ResetCalls++,
			static () => { },
			static () => { });

		var stop = Task.Run(
			() => coordinator.Stop(
				first,
				() => invalidated = true,
				_ =>
				{
					Assert.True(invalidated);
					stopEntered.Set();
					releaseStop.Wait(TestContext.Current.CancellationToken);
					firstSensor.StopCalls++;
				},
				_ =>
				{
					firstSensor.ResetCalls++;
					current = secondSensor;
				},
				static (native, value) => native.Interval = value,
				static () => { }),
			TestContext.Current.CancellationToken);
		stopEntered.Wait(TestContext.Current.CancellationToken);

		var start = Task.Run(
			() => coordinator.Start(
				second,
				() => current,
				20,
				static (native, value) => native.Interval = value,
				static _ => static () => { },
				native => native.StartCalls++,
				native => native.StopCalls++,
				native => native.ResetCalls++,
				static () => { },
				static () => { }),
			TestContext.Current.CancellationToken);

		await Task.Delay(25, TestContext.Current.CancellationToken);
		Assert.Equal(0, secondSensor.StartCalls);
		releaseStop.Set();
		await Task.WhenAll(stop, start);

		Assert.Equal(1, firstSensor.StopCalls);
		Assert.Equal(1, firstSensor.ResetCalls);
		Assert.Equal(1, secondSensor.StartCalls);
	}

	[Fact]
	public void OldGenerationNeverBecomesCurrentAfterStopStart()
	{
		var gate = new TizenSensorGenerationGate();
		var first = gate.BeginStart(useSyncContext: true, "sensor");
		Assert.True(gate.IsCurrent(first));
		Assert.True(gate.Invalidate());
		Assert.False(gate.IsCurrent(first));

		var second = gate.BeginStart(useSyncContext: false, "sensor");
		Assert.False(gate.IsCurrent(first));
		Assert.True(gate.IsCurrent(second));
		Assert.False(gate.UseSyncContext);
	}

	static void Start(
		TizenSensorLifetimeCoordinator<FakeSensor> coordinator,
		object owner,
		FakeSensor sensor,
		uint interval,
		Action subscribe,
		Action unsubscribe) =>
		coordinator.Start(
			owner,
			() => sensor,
			interval,
			static (native, value) => native.Interval = value,
			_ =>
			{
				subscribe();
				return unsubscribe;
			},
			native =>
			{
				native.StartCalls++;
				if (native.ThrowOnStart)
					throw new InvalidOperationException("start failed");
			},
			native => native.StopCalls++,
			native => native.ResetCalls++,
			static () => { },
			static () => { });

	static void Stop(
		TizenSensorLifetimeCoordinator<FakeSensor> coordinator,
		object owner,
		FakeSensor sensor) =>
		coordinator.Stop(
			owner,
			static () => { },
			native => native.StopCalls++,
			native => native.ResetCalls++,
			static (native, value) => native.Interval = value,
			static () => { });

	sealed class FakeSensor
	{
		public uint Interval { get; set; }

		public int StartCalls { get; set; }

		public int StopCalls { get; set; }

		public int ResetCalls { get; set; }

		public bool ThrowOnStart { get; set; }
	}
}
