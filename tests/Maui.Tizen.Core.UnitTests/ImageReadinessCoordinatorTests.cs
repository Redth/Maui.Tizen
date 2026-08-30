// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class ImageReadinessCoordinatorTests
	{
		sealed class FakeTarget : ITizenImageReadinessTarget
		{
			EventHandler? _ready;

			public List<string> Operations { get; } = new();
			public bool IsReady { get; set; }

			public event EventHandler ResourceReady
			{
				add
				{
					Operations.Add("subscribe");
					_ready += value;
				}
				remove
				{
					Operations.Add("unsubscribe");
					_ready -= value;
				}
			}

			public void Start(string url, bool immediate)
			{
				Assert.NotNull(_ready);
				Operations.Add($"start:{(immediate ? "immediate" : "target")}:{url}");
			}

			public void RaiseReady() => _ready?.Invoke(this, EventArgs.Empty);
		}

		[Fact]
		public async Task SubscribesBeforeImmediateLoadAndUnsubscribesAfterReadyStatus()
		{
			var target = new FakeTarget { IsReady = true };

			var wait = TizenImageReadinessCoordinator.WaitAsync(
				target, "https://example.test/image.png", immediate: true, CancellationToken.None);
			target.RaiseReady();

			Assert.True(await wait);
			Assert.Equal(
				new[]
				{
					"subscribe",
					"start:immediate:https://example.test/image.png",
					"unsubscribe",
				},
				target.Operations);
		}

		[Fact]
		public async Task FailedNativeStatusReturnsFalse()
		{
			var target = new FakeTarget { IsReady = false };

			var wait = TizenImageReadinessCoordinator.WaitAsync(
				target, "https://example.test/broken.png", immediate: false, CancellationToken.None);
			target.RaiseReady();

			Assert.False(await wait);
			Assert.Equal("unsubscribe", target.Operations[^1]);
		}

		[Fact]
		public async Task CancellationUnsubscribesWithoutReportingSuccess()
		{
			var target = new FakeTarget { IsReady = true };
			using var cancellation = new CancellationTokenSource();

			var wait = TizenImageReadinessCoordinator.WaitAsync(
				target, "https://example.test/slow.png", immediate: false, cancellation.Token);
			cancellation.Cancel();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
			Assert.Equal("unsubscribe", target.Operations[^1]);
		}

		[Fact]
		public async Task NativeUnsubscribeCompletesOnDispatcherBeforeDisposalCanContinue()
		{
			Action? queued = null;
			var unsubscribed = false;
			var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			Task Dispatch(Action action)
			{
				queued = () =>
				{
					action();
					dispatched.SetResult();
				};
				return dispatched.Task;
			}

			var cleanup = TizenImageReadinessCoordinator.DispatchCleanupAsync(
				Dispatch,
				() => unsubscribed = true);

			Assert.False(unsubscribed);
			Assert.NotNull(queued);
			Assert.False(cleanup.IsCompleted);

			queued!();
			await cleanup;

			Assert.True(unsubscribed);
		}
	}
}
