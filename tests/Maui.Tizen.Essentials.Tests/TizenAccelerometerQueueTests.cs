using System;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Behavioural tests for the shake-detection window used by <see cref="TizenAccelerometer"/>.
/// </summary>
public class TizenAccelerometerQueueTests
{
	const long Millisecond = 1_000_000;

	[Fact]
	public void IsNotShakingWhenEmpty() =>
		Assert.False(new TizenAccelerometerQueue().IsShaking);

	[Fact]
	public void IsNotShakingBeforeTheMinimumWindowElapses()
	{
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 10 * Millisecond, accelerating: true);

		// 70ms of samples is well below the 250ms minimum window.
		Assert.False(queue.IsShaking);
	}

	[Fact]
	public void IsShakingWhenMostOfALongEnoughWindowIsAccelerating()
	{
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 40 * Millisecond, accelerating: true);

		Assert.True(queue.IsShaking);
	}

	[Fact]
	public void IsNotShakingWhenTooFewSamplesAreAccelerating()
	{
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 40 * Millisecond, accelerating: i < 4);

		Assert.False(queue.IsShaking);
	}

	[Fact]
	public void ClearResetsTheWindow()
	{
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 40 * Millisecond, accelerating: true);

		Assert.True(queue.IsShaking);

		queue.Clear();

		Assert.False(queue.IsShaking);
	}

	[Fact]
	public void PurgesSamplesOlderThanTheMaximumWindow()
	{
		var queue = new TizenAccelerometerQueue();

		// Old, accelerating samples...
		for (var i = 0; i < 8; i++)
			queue.Add(i * 10 * Millisecond, accelerating: true);

		// ...followed by fresh, calm samples far beyond the 500ms window.
		for (var i = 0; i < 4; i++)
			queue.Add((5_000 + (i * 10)) * Millisecond, accelerating: false);

		Assert.False(queue.IsShaking);
	}

	[Fact]
	public void KeepsAMinimumNumberOfSamplesWhilePurging()
	{
		// Faithful to dotnet/maui's AccelerometerQueue: purging never drops below four samples, so a
		// single fresh sample after a burst still sees the tail of that burst.
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 10 * Millisecond, accelerating: true);

		queue.Add(5_000 * Millisecond, accelerating: false);

		Assert.True(queue.IsShaking);
	}

	[Fact]
	public void ConvertsUtcTimestampsToNanoseconds()
	{
		var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		var delta = TizenAccelerometerQueue.ToNanoseconds(start.AddMilliseconds(250)) -
			TizenAccelerometerQueue.ToNanoseconds(start);

		Assert.Equal(250 * Millisecond, delta);
	}
}
