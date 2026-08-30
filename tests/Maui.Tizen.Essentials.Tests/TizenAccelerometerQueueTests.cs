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
	public void IsNotShakingWithFewerThanFourSamples()
	{
		var queue = new TizenAccelerometerQueue();

		queue.Add(0, accelerating: true);
		queue.Add(300 * Millisecond, accelerating: true);
		queue.Add(400 * Millisecond, accelerating: true);

		Assert.False(queue.IsShaking);
	}

	[Theory]
	[InlineData(4, 3, true)]
	[InlineData(5, 3, false)]
	[InlineData(5, 4, true)]
	[InlineData(7, 5, false)]
	[InlineData(7, 6, true)]
	public void UsesExactThreeQuarterThreshold(int count, int accelerating, bool expected)
	{
		var queue = new TizenAccelerometerQueue();
		var interval = 300 * Millisecond / (count - 1);

		for (var index = 0; index < count; index++)
		{
			queue.Add(
				index * interval,
				accelerating: index < accelerating);
		}

		Assert.Equal(expected, queue.IsShaking);
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
	public void PurgesEveryExpiredSampleAndUsesMinimumOnlyForDetection()
	{
		var queue = new TizenAccelerometerQueue();

		for (var i = 0; i < 8; i++)
			queue.Add(i * 10 * Millisecond, accelerating: true);

		queue.Add(5_000 * Millisecond, accelerating: false);

		Assert.False(queue.IsShaking);
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
