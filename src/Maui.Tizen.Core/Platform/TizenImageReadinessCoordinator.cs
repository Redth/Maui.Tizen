// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal interface ITizenImageReadinessTarget
	{
		event EventHandler ResourceReady;

		bool IsReady { get; }

		void Start(string url, bool immediate);
	}

	internal static class TizenImageReadinessCoordinator
	{
		public static Task DispatchCleanupAsync(Func<Action, Task> dispatch, Action cleanup)
		{
			ArgumentNullException.ThrowIfNull(dispatch);
			ArgumentNullException.ThrowIfNull(cleanup);
			return dispatch(cleanup);
		}

		public static async Task<bool> WaitAsync(
			ITizenImageReadinessTarget target,
			string url,
			bool immediate,
			CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(target);
			ArgumentException.ThrowIfNullOrEmpty(url);
			cancellationToken.ThrowIfCancellationRequested();

			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			void OnResourceReady(object? sender, EventArgs args) => completion.TrySetResult();

			target.ResourceReady += OnResourceReady;
			using var registration = cancellationToken.Register(
				static state => ((TaskCompletionSource)state!).TrySetCanceled(),
				completion);

			try
			{
				target.Start(url, immediate);
				await completion.Task;
				cancellationToken.ThrowIfCancellationRequested();
				return target.IsReady;
			}
			finally
			{
				target.ResourceReady -= OnResourceReady;
			}
		}
	}
}
