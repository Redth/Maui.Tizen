using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenPermissionRequestCoordinatorTests
{
	const string Privilege = "http://tizen.org/privilege/location";

	[Fact]
	public async Task SynchronousAnswerCompletesAndUnsubscribes()
	{
		var source = new FakePermissionRequestSource
		{
			OnRequest = self => self.Reply(
				global::Tizen.Security.CallCause.Answer,
				global::Tizen.Security.RequestResult.AllowForever,
				Privilege),
		};

		Assert.True(await RunAsync(source, TestContext.Current.CancellationToken));
		Assert.Equal(0, source.SubscriberCount);
	}

	[Fact]
	public async Task NoResponseTimesOutAndUnsubscribes()
	{
		var source = new FakePermissionRequestSource();

		await Assert.ThrowsAsync<TimeoutException>(() =>
			RunAsync(
				source,
				TestContext.Current.CancellationToken,
				TimeSpan.FromMilliseconds(25)));
		Assert.Equal(0, source.SubscriberCount);
	}

	[Fact]
	public async Task CallerCancellationUsesItsTokenAndUnsubscribes()
	{
		var source = new FakePermissionRequestSource();
		using var cancellation = new CancellationTokenSource();

		var request = RunAsync(source, cancellation.Token);
		cancellation.Cancel();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(0, source.SubscriberCount);
	}

	[Fact]
	public async Task DuplicateAndLateAnswersAreIgnored()
	{
		var source = new FakePermissionRequestSource();
		var request = RunAsync(source, TestContext.Current.CancellationToken);

		source.Reply(
			global::Tizen.Security.CallCause.Answer,
			global::Tizen.Security.RequestResult.AllowForever,
			Privilege);
		source.Reply(
			global::Tizen.Security.CallCause.Answer,
			global::Tizen.Security.RequestResult.DenyForever,
			Privilege);

		var granted = await request;
		Assert.True(granted);
		Assert.Equal(0, source.SubscriberCount);
		source.Reply(
			global::Tizen.Security.CallCause.Answer,
			global::Tizen.Security.RequestResult.DenyForever,
			Privilege);
		Assert.True(granted);
	}

	[Theory]
	[InlineData(global::Tizen.Security.CallCause.Error, Privilege)]
	[InlineData(global::Tizen.Security.CallCause.Answer, "http://tizen.org/privilege/camera")]
	public async Task ErrorOrMismatchedAnswerFailsClosed(
		global::Tizen.Security.CallCause cause,
		string responsePrivilege)
	{
		var source = new FakePermissionRequestSource();
		var request = RunAsync(source, TestContext.Current.CancellationToken);

		source.Reply(
			cause,
			global::Tizen.Security.RequestResult.AllowForever,
			responsePrivilege);

		await Assert.ThrowsAsync<InvalidOperationException>(() => request);
		Assert.Equal(0, source.SubscriberCount);
	}

	static Task<bool> RunAsync(
		FakePermissionRequestSource source,
		CancellationToken cancellationToken,
		TimeSpan? timeout = null) =>
		TizenPermissionRequestCoordinator.RunAsync(
			Privilege,
			source,
			cancellationToken,
			timeout ?? TimeSpan.FromSeconds(5));

	sealed class FakePermissionRequestSource : ITizenPermissionRequestSource
	{
		Action<TizenPermissionResponse>? _response;

		public Action<FakePermissionRequestSource>? OnRequest { get; init; }

		public int SubscriberCount => _response?.GetInvocationList().Length ?? 0;

		public event Action<TizenPermissionResponse>? Response
		{
			add => _response += value;
			remove => _response -= value;
		}

		public void Request() => OnRequest?.Invoke(this);

		public void Reply(
			global::Tizen.Security.CallCause cause,
			global::Tizen.Security.RequestResult result,
			string privilege) =>
			_response?.Invoke(new(cause, result, privilege));
	}
}
