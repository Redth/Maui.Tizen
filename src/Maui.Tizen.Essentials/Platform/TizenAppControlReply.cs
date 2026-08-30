using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal static class TizenAppControlReply
	{
		public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

		public static async Task<T> RunAsync<TReply, TResult, T>(
			Action<Action<TReply, TResult>> start,
			Func<TReply, TResult, T> parse,
			CancellationToken cancellationToken = default,
			Action<Action<TReply, TResult>>? unsubscribe = null,
			TimeSpan? timeoutOverride = null)
		{
			ArgumentNullException.ThrowIfNull(start);
			ArgumentNullException.ThrowIfNull(parse);
			cancellationToken.ThrowIfCancellationRequested();

			var completion = new TaskCompletionSource<T>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var settled = 0;

			void Complete(TReply reply, TResult result)
			{
				if (Interlocked.Exchange(ref settled, 1) != 0)
					return;

				try
				{
					completion.TrySetResult(parse(reply, result));
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
						new TimeoutException($"Tizen AppControl did not reply within {effectiveTimeout}."));
			});

			try
			{
				start(Complete);
				return await completion.Task.ConfigureAwait(false);
			}
			finally
			{
				unsubscribe?.Invoke(Complete);
			}
		}

		public static uint NativeTimeoutMilliseconds =>
			checked((uint)Math.Clamp(Math.Ceiling(Timeout.TotalMilliseconds), 1, uint.MaxValue));
	}
}
