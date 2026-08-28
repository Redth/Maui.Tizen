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
		sealed class ManualDelay
		{
			readonly List<TaskCompletionSource> _pending = new();

			public int Count => _pending.Count;

			public Task Wait(CancellationToken token)
			{
				var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				token.Register(() => completion.TrySetCanceled(token));
				_pending.Add(completion);
				return completion.Task;
			}

			public void Complete(int index) => _pending[index].TrySetResult();
		}

		static Task DispatchInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		[Fact]
		public async Task StopRestartReplaysExactlyOnceAfterCompletion()
		{
			var delay = new ManualDelay();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, delay.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(desired: true, enabled: true));
			var expiry = coordinator.Request(desired: false, enabled: true);
			Assert.NotNull(expiry);
			Assert.Null(coordinator.Request(desired: true, enabled: true));

			Assert.Equal(new[] { true, false }, writes);
			Assert.True(state.HasPendingStart);

			delay.Complete(0);
			await expiry!;

			Assert.Equal(new[] { true, false, true }, writes);
			Assert.False(state.IsCompleting);
			Assert.False(state.HasPendingStart);
		}

		[Fact]
		public async Task RepeatedStopCancelsPendingRestartWithoutRescheduling()
		{
			var delay = new ManualDelay();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, delay.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);
			Assert.Null(coordinator.Request(true, true));

			Assert.Null(coordinator.Request(false, true));
			Assert.Equal(1, delay.Count);

			delay.Complete(0);
			await expiry!;

			Assert.Equal(new[] { true, false }, writes);
			Assert.False(state.IsCompleting);
			Assert.False(state.HasPendingStart);
		}

		[Fact]
		public async Task DisableClearsDesiredStateAndSuppressesReplay()
		{
			var delay = new ManualDelay();
			var writes = new List<bool>();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, delay.Wait, DispatchInline, writes.Add, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);
			Assert.Null(coordinator.Request(true, true));
			Assert.Null(coordinator.Request(false, enabled: false));

			delay.Complete(0);
			await expiry!;

			Assert.Equal(new[] { true, false }, writes);
			Assert.False(state.IsRefreshing);
			Assert.False(state.IsCompleting);
		}

		[Fact]
		public async Task OrdinaryStopExpiresEvenWithoutRestart()
		{
			var delay = new ManualDelay();
			var state = new TizenRefreshStateMachine();
			using var coordinator = new TizenRefreshCoordinator(
				state, delay.Wait, DispatchInline, static _ => { }, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var expiry = coordinator.Request(false, true);

			Assert.True(state.IsCompleting);

			delay.Complete(0);
			await expiry!;

			Assert.False(state.IsCompleting);
		}

		[Fact]
		public async Task DisconnectCancelsReplayButRetainsPlatformUntilNativeCompletionWindow()
		{
			var delay = new ManualDelay();
			var state = new TizenRefreshStateMachine();
			var disposed = 0;
			var coordinator = new TizenRefreshCoordinator(
				state, delay.Wait, DispatchInline, static _ => { }, static () => true);

			Assert.Null(coordinator.Request(true, true));
			var replay = coordinator.Request(false, true);
			var retainedDisposal = coordinator.RetainPlatformUntilCompletionAsync(() => disposed++);

			coordinator.Dispose();
			await replay!;

			Assert.Equal(0, disposed);
			Assert.Equal(2, delay.Count);

			delay.Complete(1);
			await retainedDisposal;

			Assert.Equal(1, disposed);
		}
	}
}
