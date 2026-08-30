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
	}
}
