// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class RefreshNativeIdlePollerTests
	{
		static Task DispatchInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		[Fact]
		public async Task PollsActualNativeStateAcrossFramesUntilIdle()
		{
			var refreshing = true;
			var frames = 0;

			var observed = await TizenRefreshNativeIdlePoller.WaitAsync(
				() => refreshing,
				DispatchInline,
				_ =>
				{
					frames++;
					if (frames == 3)
						refreshing = false;
					return Task.CompletedTask;
				},
				maximumFrames: 8,
				CancellationToken.None);

			Assert.True(observed);
			Assert.Equal(3, frames);
		}

		[Fact]
		public async Task BoundedTimeoutReturnsFalseInsteadOfAuthorizingUnsafeDisposal()
		{
			var frames = 0;

			var observed = await TizenRefreshNativeIdlePoller.WaitAsync(
				static () => true,
				DispatchInline,
				_ =>
				{
					frames++;
					return Task.CompletedTask;
				},
				maximumFrames: 3,
				CancellationToken.None);

			Assert.False(observed);
			Assert.Equal(2, frames);
		}

		[Fact]
		public async Task CancellationStopsPolling()
		{
			using var cancellation = new CancellationTokenSource();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
				TizenRefreshNativeIdlePoller.WaitAsync(
					static () => true,
					DispatchInline,
					_ =>
					{
						cancellation.Cancel();
						return Task.CompletedTask;
					},
					maximumFrames: 8,
					cancellation.Token));
		}

		[Fact]
		public void CancelledPullRemainsOwnedUntilExplicitResetCompletes()
		{
			var activity = new TizenRefreshNativeActivity();

			activity.BeginPull();
			activity.ReleasePull();
			activity.BeginReset();

			Assert.True(activity.HasPendingActivity);
			Assert.True(activity.IsResetPending);

			activity.CompleteReset();

			Assert.False(activity.HasPendingActivity);
			Assert.False(activity.IsResetPending);
		}
	}
}
