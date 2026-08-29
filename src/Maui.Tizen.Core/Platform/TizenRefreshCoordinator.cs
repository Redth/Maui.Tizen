// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class TizenRefreshCoordinator : IDisposable
	{
		readonly object _gate = new();
		readonly TizenRefreshStateMachine _state;
		readonly Func<CancellationToken, Task<bool>> _waitForNativeIdle;
		readonly Func<Action, Task> _dispatch;
		readonly Action<bool> _applyNative;
		readonly Func<bool> _canApply;

		CancellationTokenSource? _completionCancellation;
		bool _desired;
		bool _enabled = true;
		bool _disposed;

		public TizenRefreshCoordinator(
			TizenRefreshStateMachine state,
			Func<CancellationToken, Task<bool>> waitForNativeIdle,
			Func<Action, Task> dispatch,
			Action<bool> applyNative,
			Func<bool> canApply)
		{
			_state = state ?? throw new ArgumentNullException(nameof(state));
			_waitForNativeIdle = waitForNativeIdle ?? throw new ArgumentNullException(nameof(waitForNativeIdle));
			_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
			_applyNative = applyNative ?? throw new ArgumentNullException(nameof(applyNative));
			_canApply = canApply ?? throw new ArgumentNullException(nameof(canApply));
		}

		public bool IsCompleting
		{
			get
			{
				lock (_gate)
					return _state.IsCompleting;
			}
		}

		public Task? Request(bool desired, bool enabled)
		{
			bool? apply = null;
			CancellationToken? completionToken = null;

			lock (_gate)
			{
				if (_disposed)
					return null;

				_enabled = enabled;
				_desired = enabled && desired;
				var wasCompleting = _state.IsCompleting;
				var action = _state.Request(_desired);

				if (action == TizenRefreshAction.Apply)
					apply = _state.IsRefreshing;

				if (!wasCompleting && _state.IsCompleting)
				{
					_completionCancellation?.Cancel();
					_completionCancellation?.Dispose();
					_completionCancellation = new CancellationTokenSource();
					completionToken = _completionCancellation.Token;
				}
			}

			if (apply.HasValue && _canApply())
				_applyNative(apply.Value);

			return completionToken.HasValue
				? CompleteWhenNativeIdleAsync(completionToken.Value)
				: null;
		}

		public void ObserveNativeStart()
		{
			lock (_gate)
			{
				if (!_disposed)
					_state.ObserveNativeStart();
			}
		}

		public Func<Task> PreparePlatformDisposal(Action dispose, bool retainForNativeActivity = false)
		{
			ArgumentNullException.ThrowIfNull(dispose);
			return PreparePlatformDisposal(
				() =>
				{
					dispose();
					return true;
				},
				retainForNativeActivity);
		}

		public Func<Task> PreparePlatformDisposal(
			Func<bool> tryDispose,
			bool retainForNativeActivity = false)
		{
			ArgumentNullException.ThrowIfNull(tryDispose);

			bool retain;

			lock (_gate)
				retain = _state.IsCompleting || retainForNativeActivity;

			return () => DisposeWhenNativeIdleAsync(tryDispose, retain);
		}

		async Task CompleteWhenNativeIdleAsync(CancellationToken token)
		{
			try
			{
				while (!await _waitForNativeIdle(token).ConfigureAwait(false))
					token.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}

			await _dispatch(() =>
			{
				bool apply;

				lock (_gate)
				{
					if (_disposed || token.IsCancellationRequested)
						return;

					apply =
						_state.CompletionElapsed() == TizenRefreshAction.Apply &&
						_enabled &&
						_desired;
				}

				if (apply && _canApply())
					_applyNative(true);
			}).ConfigureAwait(false);
		}

		async Task DisposeWhenNativeIdleAsync(Func<bool> tryDispose, bool waitBeforeFirstAttempt)
		{
			while (true)
			{
				if (waitBeforeFirstAttempt)
				{
					while (!await _waitForNativeIdle(CancellationToken.None).ConfigureAwait(false))
					{
						// A bounded interval is diagnostic, not terminal. Keeping this async method
						// alive retains the captured platform owner until native idle is observed.
					}
				}

				var disposed = false;
				await _dispatch(() => disposed = tryDispose()).ConfigureAwait(false);
				if (disposed)
					return;

				// The UI-thread disposal callback observed renewed activity. Poll again rather than
				// trusting the stale idle result that preceded the callback.
				waitBeforeFirstAttempt = true;
			}
		}

		public void Dispose()
		{
			CancellationTokenSource? completion;

			lock (_gate)
			{
				if (_disposed)
					return;

				_disposed = true;
				_desired = false;
				_enabled = false;
				_state.Reset();
				completion = _completionCancellation;
				_completionCancellation = null;
			}

			TizenCleanup.Run(
				() => completion?.Cancel(),
				() => completion?.Dispose());
		}
	}
}
