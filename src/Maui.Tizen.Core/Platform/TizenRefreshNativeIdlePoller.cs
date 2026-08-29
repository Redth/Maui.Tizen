// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenRefreshNativeIdlePoller
	{
		public static async Task<bool> WaitAsync(
			Func<bool> isRefreshing,
			Func<Action, Task> dispatch,
			Func<CancellationToken, Task> nextFrame,
			int maximumFrames,
			CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(isRefreshing);
			ArgumentNullException.ThrowIfNull(dispatch);
			ArgumentNullException.ThrowIfNull(nextFrame);
			ArgumentOutOfRangeException.ThrowIfLessThan(maximumFrames, 1);

			for (var frame = 0; frame < maximumFrames; frame++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var refreshing = true;
				await dispatch(() => refreshing = isRefreshing()).ConfigureAwait(false);

				if (!refreshing)
					return true;

				if (frame + 1 < maximumFrames)
					await nextFrame(cancellationToken).ConfigureAwait(false);
			}

			return false;
		}
	}
}
