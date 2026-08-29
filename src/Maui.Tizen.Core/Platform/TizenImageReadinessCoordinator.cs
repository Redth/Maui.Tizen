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

		void StartImmediate(string url);
	}

	internal static class TizenImageReadinessCoordinator
	{
		public static async Task<bool> WaitAsync(
			ITizenImageReadinessTarget target,
			string url,
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
				target.StartImmediate(url);
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
