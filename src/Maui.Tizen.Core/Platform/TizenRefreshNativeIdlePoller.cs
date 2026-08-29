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
		bool _refreshActive;
		bool _disablePending;
		int _quietFrames;

		public bool HasPendingActivity => _touchActive || _resetting;

		public bool IsResetPending => _resetting;

		public void BeginPull()
		{
			_touchActive = true;
			_resetting = true;
			_quietFrames = 0;
		}

		public bool DeferDisable()
		{
			if (!_touchActive)
				return false;

			_disablePending = true;
			return true;
		}

		public void CancelDeferredDisable() => _disablePending = false;

		public bool ReleasePull()
		{
			_touchActive = false;
			_resetting = false;
			_quietFrames = 0;

			var applyDisable = _disablePending;
			_disablePending = false;
			return applyDisable;
		}

		public void BeginReset()
		{
			_touchActive = false;
			_resetting = true;
			_refreshActive = false;
			_quietFrames = 0;
		}

		public void CompleteReset()
		{
			_resetting = false;
			_refreshActive = false;
			_quietFrames = 0;
		}

		public void ObserveRefreshStarted()
		{
			_touchActive = false;
			_resetting = false;
			_refreshActive = true;
			_disablePending = false;
			_quietFrames = 0;
		}

		public bool IsBusy(bool isRefreshing, int requiredQuietFrames)
		{
			if (isRefreshing)
			{
				if (!_refreshActive)
					ObserveRefreshStarted();

				_quietFrames = 0;
				return true;
			}

			if (_refreshActive)
			{
				_refreshActive = false;
				_touchActive = false;
				_resetting = false;
				_quietFrames = 0;
				return false;
			}

			if (_touchActive)
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

	internal sealed class TizenRefreshTeardownObserver
	{
		bool _active;
		bool _acceptTerminal;

		public bool IsActive => _active;

		public void Begin(bool pullActive)
		{
			_active = true;
			_acceptTerminal = pullActive;
		}

		public bool ShouldForceCompletion() => _active;

		public bool CanStartOrContinue => !_active;

		public bool CanProcessTerminal => !_active || _acceptTerminal;

		public void TerminalProcessed() => _acceptTerminal = false;

		public void Complete()
		{
			_active = false;
			_acceptTerminal = false;
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
