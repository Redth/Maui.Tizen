using System;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	public class AsyncOperationLifetimeTests
	{
		[Fact]
		public async Task RetirementWaitsForAnAcceptedDispatch()
		{
			var entered = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var release = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var lifetime = new AsyncOperationLifetime();
			var operation = lifetime.TryRunAsync(async () =>
			{
				entered.TrySetResult(null);
				await release.Task;
				return true;
			});
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var retirement = lifetime.RetireAsync();
			Assert.False(retirement.IsCompleted);
			release.TrySetResult(null);

			Assert.True(await operation);
			await retirement.WaitAsync(TimeSpan.FromSeconds(10));
		}

		[Fact]
		public async Task DispatchAfterRetirementIsRejected()
		{
			var lifetime = new AsyncOperationLifetime();
			await lifetime.RetireAsync();

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
