using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenAppControlReplyTests
{
	[Fact]
	public void ReplyWaitIsBounded()
	{
		Assert.True(TizenAppControlReply.Timeout > TimeSpan.Zero);
		Assert.True(TizenAppControlReply.Timeout <= TimeSpan.FromMinutes(1));
		Assert.InRange(TizenAppControlReply.NativeTimeoutMilliseconds, 1u, 60_000u);
	}

	[Fact]
	public async Task ParseFailureFaultsAndUnsubscribes()
	{
		Action<string, int>? callback = null;
		var unsubscribed = 0;
		var failure = new FormatException("malformed reply");

		var operation = TizenAppControlReply.RunAsync<string, int, string>(
			handler => callback = handler,
			(_, _) => throw failure,
			TestContext.Current.CancellationToken,
			handler =>
			{
				if (callback == handler)
					callback = null;
				unsubscribed++;
			});

		callback!("reply", 0);

		Assert.Same(failure, await Assert.ThrowsAsync<FormatException>(() => operation));
		Assert.Equal(1, unsubscribed);
		Assert.Null(callback);
	}

	[Fact]
	public async Task DuplicateAndLateRepliesAreIgnored()
	{
		Action<string, int>? callback = null;
		var parseCalls = 0;

		var operation = TizenAppControlReply.RunAsync<string, int, string>(
			handler => callback = handler,
			(reply, _) =>
			{
				parseCalls++;
				return reply;
			},
			TestContext.Current.CancellationToken);

		var nativeCallback = callback!;
		nativeCallback("first", 0);
		nativeCallback("duplicate", 0);

		Assert.Equal("first", await operation);
		Assert.Equal(1, parseCalls);
	}

	[Fact]
	public async Task CancellationSettlesNeverReplyAndUnsubscribes()
	{
		using var cancellation = new CancellationTokenSource();
		Action<string, int>? callback = null;
		var unsubscribed = 0;

		var operation = TizenAppControlReply.RunAsync<string, int, string>(
			handler => callback = handler,
			static (reply, _) => reply,
			cancellation.Token,
			handler =>
			{
				if (callback == handler)
					callback = null;
				unsubscribed++;
			});

		cancellation.Cancel();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(1, unsubscribed);
		Assert.Null(callback);
	}

	[Fact]
	public async Task NeverReplyTimesOutAndUnsubscribes()
	{
		Action<string, int>? callback = null;
		var unsubscribed = 0;

		var operation = TizenAppControlReply.RunAsync<string, int, string>(
			handler => callback = handler,
			static (reply, _) => reply,
			cancellationToken: TestContext.Current.CancellationToken,
			unsubscribe: handler =>
			{
				if (callback == handler)
					callback = null;
				unsubscribed++;
			},
			timeoutOverride: TimeSpan.FromMilliseconds(25));

		await Assert.ThrowsAsync<TimeoutException>(() => operation);
		Assert.Equal(1, unsubscribed);
		Assert.Null(callback);
	}

	[Fact]
	public async Task SynchronousReplyIsHandledExactlyOnce()
	{
		var result = await TizenAppControlReply.RunAsync<string, int, string>(
			handler =>
			{
				handler("inline", 0);
				handler("duplicate", 0);
			},
			static (reply, _) => reply,
			TestContext.Current.CancellationToken);

		Assert.Equal("inline", result);
	}
}
