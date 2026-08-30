using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal readonly record struct TizenPermissionResponse(
		global::Tizen.Security.CallCause Cause,
		global::Tizen.Security.RequestResult Result,
		string Privilege);

	internal interface ITizenPermissionRequestSource
	{
		event Action<TizenPermissionResponse>? Response;

		void Request();
	}

	internal static class TizenPermissionRequestCoordinator
	{
		public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

		public static async Task<bool> RunAsync(
			string privilege,
			ITizenPermissionRequestSource source,
			CancellationToken cancellationToken = default,
			TimeSpan? timeoutOverride = null)
		{
			ArgumentException.ThrowIfNullOrEmpty(privilege);
			ArgumentNullException.ThrowIfNull(source);
			cancellationToken.ThrowIfCancellationRequested();

			var completion = new TaskCompletionSource<bool>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var settled = 0;

			void OnResponse(TizenPermissionResponse response)
			{
				if (Interlocked.Exchange(ref settled, 1) != 0)
					return;

				try
				{
					completion.TrySetResult(TizenPermissions.InterpretRequestResponse(
						privilege,
						response.Cause,
						response.Result,
						response.Privilege));
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			}

			var effectiveTimeout = timeoutOverride ?? Timeout;
			using var timeout = new CancellationTokenSource(effectiveTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				timeout.Token);
			using var registration = linked.Token.Register(() =>
			{
				if (Interlocked.Exchange(ref settled, 1) != 0)
					return;

				if (cancellationToken.IsCancellationRequested)
					completion.TrySetCanceled(cancellationToken);
				else
					completion.TrySetException(
						new TimeoutException(
							$"Tizen did not answer the '{privilege}' permission request within {effectiveTimeout}."));
			});

			try
			{
				source.Response += OnResponse;
				source.Request();
				return await completion.Task.ConfigureAwait(false);
			}
			finally
			{
				source.Response -= OnResponse;
			}
		}
	}
}
