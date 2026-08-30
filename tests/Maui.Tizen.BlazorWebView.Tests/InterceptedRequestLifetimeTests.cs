using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	public class InterceptedRequestLifetimeTests
	{
		[Fact]
		public void RetiredLifetimeIgnoresTheRequestWithoutCallingTheProcessor()
		{
			var processed = false;
			var lifetime = new InterceptedRequestLifetime(
				_ => processed = true,
				NullLogger.Instance);
			var request = new FakeRequest();

			_ = lifetime.RetireAsync();
			lifetime.Process(request);

			Assert.False(processed);
			Assert.Equal(1, request.IgnoreCount);
		}

		[Fact]
		public async Task RetirementWaitsForAnActiveNativeCallback()
		{
			var entered = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var release = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var lifetime = new InterceptedRequestLifetime(
				_ =>
				{
					entered.TrySetResult(null);
					release.Task.GetAwaiter().GetResult();
				},
				NullLogger.Instance);

			var processing = Task.Run(() => lifetime.Process(new FakeRequest()));
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var retirement = lifetime.RetireAsync();
			Assert.False(retirement.IsCompleted);
			release.TrySetResult(null);

			await Task.WhenAll(processing, retirement).WaitAsync(TimeSpan.FromSeconds(10));
		}

		[Fact]
		public void ProcessorFailureIsContainedAndCompletesTheRequestSafely()
		{
			var lifetime = new InterceptedRequestLifetime(
				_ => throw new InvalidOperationException("request failed"),
				NullLogger.Instance);
			var request = new FakeRequest();

			var failure = Record.Exception(() => lifetime.Process(request));

			Assert.Null(failure);
			Assert.Equal(1, request.IgnoreCount);
		}

		private sealed class FakeRequest : IInterceptedRequest
		{
			public string Url => "http://0.0.0.0/";

			public string Method => "GET";

			public IDictionary<string, string> Headers { get; } =
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			public int IgnoreCount { get; private set; }

			public void Ignore() => IgnoreCount++;

			public void SetResponse(string headerBlock, byte[] body)
			{
			}
		}
	}
}
