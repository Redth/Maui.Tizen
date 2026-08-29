// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class RefreshCoordinatorTests
	{
		sealed class ManualNativeIdle
		{
			readonly TaskCompletionSource _completion =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public int WaiterCount { get; private set; }

			public Task Wait(CancellationToken token)
			{
				WaiterCount++;
				return _completion.Task.WaitAsync(token);
			}

			public void Complete() => _completion.TrySetResult();
		}

		static Task DispatchInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		[Fact]
		public async Task StopRestartReplaysExactlyOnceAfterCompletion()
		{
			var nativeIdle = new ManualNativeIdle();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(desired: true, enabled: true));
			var expiry = coordinator.Request(desired: false, enabled: true);
			Assert.NotNull(expiry);
			Assert.Null(coordinator.Request(desired: true, enabled: true));

			Assert.Equal(new[] { true, false }, writes);
			Assert.True(state.HasPendingStart);

			nativeIdle.Complete();
			await expiry!;

			Assert.Equal(new[] { true, false, true }, writes);
			Assert.False(state.IsCompleting);
			Assert.False(state.HasPendingStart);
		}

		[Fact]
		public async Task RepeatedStopCancelsPendingRestartWithoutRescheduling()
		{
			var nativeIdle = new ManualNativeIdle();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);
			Assert.Null(coordinator.Request(true, true));

			Assert.Null(coordinator.Request(false, true));
			Assert.Equal(1, nativeIdle.WaiterCount);

			nativeIdle.Complete();
			await expiry!;

			Assert.Equal(new[] { true, false }, writes);
			Assert.False(state.IsCompleting);
			Assert.False(state.HasPendingStart);
		}

		[Fact]
		public async Task DisableClearsDesiredStateAndSuppressesReplay()
		{
			var nativeIdle = new ManualNativeIdle();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);
			Assert.Null(coordinator.Request(true, true));
			Assert.Null(coordinator.Request(false, enabled: false));

			nativeIdle.Complete();
			await expiry!;

			Assert.Equal(new[] { true, false }, writes);
			Assert.False(state.IsRefreshing);
			Assert.False(state.IsCompleting);
		}

		[Fact]
		public async Task OrdinaryStopExpiresEvenWithoutRestart()
		{
			var nativeIdle = new ManualNativeIdle();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, static _ => { }, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);

			Assert.True(state.IsCompleting);

			nativeIdle.Complete();
			await expiry!;

			Assert.False(state.IsCompleting);
		}

		[Fact]
		public async Task DisconnectCancelsReplayButRetainsPlatformUntilNativeCompletionWindow()
		{
			var nativeIdle = new ManualNativeIdle();
			var state = new TizenRefreshStateMachine();
			var disposed = 0;
			var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, static _ => { }, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var replay = coordinator.Request(false, true);
			var retainedDisposal = coordinator.RetainPlatformUntilCompletionAsync(() => disposed++);

			coordinator.Dispose();
			await replay!;

			Assert.Equal(0, disposed);
			Assert.Equal(2, nativeIdle.WaiterCount);

			nativeIdle.Complete();
			await retainedDisposal;

			Assert.Equal(1, disposed);
		}

		[Fact]
		public async Task ObservedNativeStartCanBeForcedToCompletionWhenDisabled()
		{
			var nativeIdle = new ManualNativeIdle();
			var state = new TizenRefreshStateMachine();
			var writes = new List<bool>();
			using var coordinator = new TizenRefreshCoordinator(
				state, nativeIdle.Wait, DispatchInline, writes.Add, static () => true);

			coordinator.ObserveNativeStart();
			var completion = coordinator.Request(desired: false, enabled: false);

			Assert.NotNull(completion);
			Assert.Equal(new[] { false }, writes);
			Assert.True(state.IsCompleting);

			nativeIdle.Complete();
			await completion!;

			Assert.False(state.IsRefreshing);
			Assert.False(state.IsCompleting);
		}
	}
}
