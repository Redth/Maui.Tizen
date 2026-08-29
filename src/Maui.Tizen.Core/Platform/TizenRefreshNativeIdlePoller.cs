// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class TizenRefreshNativeActivity
	{
		bool _touchActive;
		bool _resetting;
		int _quietFrames;

		public bool HasPendingActivity => _touchActive || _resetting;

		public void BeginPull()
		{
			_touchActive = true;
			_resetting = true;
			_quietFrames = 0;
		}

		public void ReleasePull()
		{
			_touchActive = false;
			_resetting = true;
			_quietFrames = 0;
		}

		public void ObserveRefreshStarted()
		{
			_touchActive = false;
			_resetting = false;
			_quietFrames = 0;
		}

		public bool IsBusy(bool isRefreshing, int requiredQuietFrames)
		{
			if (_touchActive || isRefreshing)
			{
				_quietFrames = 0;
				return true;
			}

			if (!_resetting)
				return false;

			if (++_quietFrames < requiredQuietFrames)
				return true;

			_resetting = false;
			return false;
		}
	}

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
