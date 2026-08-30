using System;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	public class InboundMessageLifetimeTests
	{
		[Fact]
		public async Task DrainingAdmitsCompletionForAnAlreadyAcceptedMessage()
		{
			var lifetime = new InboundMessageLifetime();
			var firstStarted = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var acknowledgement = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var first = lifetime.TryRunAsync(async () =>
			{
				firstStarted.TrySetResult(null);
				await acknowledgement.Task;
				return true;
			});
			await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var drain = lifetime.DrainAsync();
			Assert.False(drain.IsCompleted);
			var completionAccepted = await lifetime.TryRunAsync(() =>
			{
				acknowledgement.TrySetResult(null);
				return Task.FromResult(true);
			});

			Assert.True(completionAccepted);
			Assert.True(await first);
			await drain.WaitAsync(TimeSpan.FromSeconds(10));
		}

		[Fact]
		public async Task AdmissionClosesAfterTheLastActiveMessageCompletes()
		{
			var lifetime = new InboundMessageLifetime();
			await lifetime.DrainAsync();

			var invoked = false;
			var accepted = await lifetime.TryRunAsync(() =>
			{
				invoked = true;
				return Task.FromResult(true);
			});

			Assert.False(accepted);
			Assert.False(invoked);
		}

		[Fact]
		public async Task FollowUpTrafficCannotExtendAdmissionAfterTheOriginalCohortCompletes()
		{
			var lifetime = new InboundMessageLifetime();
			var originalStarted = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseOriginal = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseFollowUp = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var original = lifetime.TryRunAsync(async () =>
			{
				originalStarted.TrySetResult(null);
				await releaseOriginal.Task;
				return true;
			});
			await originalStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var drain = lifetime.DrainAsync();

			var followUp = lifetime.TryRunAsync(async () =>
			{
				await releaseFollowUp.Task;
				return true;
			});
			releaseOriginal.TrySetResult(null);
			Assert.True(await original);

			var invoked = false;
			var late = await lifetime.TryRunAsync(() =>
			{
				invoked = true;
				return Task.FromResult(true);
			});

			Assert.False(late);
			Assert.False(invoked);
			Assert.False(drain.IsCompleted);

			releaseFollowUp.TrySetResult(null);
			Assert.True(await followUp);
			await drain.WaitAsync(TimeSpan.FromSeconds(10));
		}
	}
}
