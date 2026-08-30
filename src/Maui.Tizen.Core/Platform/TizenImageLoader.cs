// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Owns the lifetime of one view's asynchronously loaded image.
	/// </summary>
	/// <typeparam name="TImage">The loaded platform image type.</typeparam>
	/// <remarks>
	/// <para>
	/// Image loading is the one place in a handler where an operation outlives the state that
	/// started it, so every one of the following has to be handled explicitly. Getting any of them
	/// wrong produces a bug that only appears under fast source changes or teardown:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>Supersession.</b> A second load starting before the first finishes must cancel it,
	/// otherwise the slower earlier load wins and the view shows the wrong image.
	/// </description></item>
	/// <item><description>
	/// <b>Source identity.</b> Even with cancellation, a load can complete just as the source
	/// changes. The result is only applied if the source it was started for is still current.
	/// </description></item>
	/// <item><description>
	/// <b>View identity.</b> A handler can be reconnected to a different platform view while a
	/// load is in flight; applying then would write an image onto a view that has moved on.
	/// </description></item>
	/// <item><description>
	/// <b>Failure clearing.</b> A failed load, including cancellation initiated by the current
	/// source service, must clear the image. Cancellation from supersession or disposal stays
	/// silent so it cannot clear the newer image.
	/// </description></item>
	/// <item><description>
	/// <b>Ownership.</b> The service result holds a native handle. Whoever replaces it must
	/// dispose the one it replaced, and disconnecting must dispose the last one - otherwise every
	/// source change leaks a NUI image buffer. Result disposal runs on the same captured dispatcher
	/// as apply because <c>TizenImageSource.Dispose</c> releases its NUI <c>ImageUrl</c>.
	/// </description></item>
	/// </list>
	/// <para>
	/// The type is generic over the image so this logic is platform-independent and can actually be
	/// executed by the host-side tests, rather than being asserted only in review.
	/// </para>
	/// </remarks>
	public sealed class TizenImageLoader<TImage> : IDisposable
		where TImage : class
	{
		readonly object _gate = new();
		readonly List<UncommittedResult> _uncommitted = new();

		CancellationTokenSource? _pending;
		IImageSource? _pendingSource;
		IImageSourceServiceResult<TImage>? _active;
		IImageSource? _activeSource;
		long _generation;
		bool _disposed;

		/// <summary>The image currently applied, if any.</summary>
		public TImage? Current => _active?.Value;

		/// <summary>The source the current image was loaded from, if any.</summary>
		public IImageSource? CurrentSource => _activeSource;

		/// <summary>
		/// Loads <paramref name="source"/> and applies it, superseding any load in flight.
		/// </summary>
		/// <param name="source">The image source, or <see langword="null"/> to clear.</param>
		/// <param name="load">Resolves the source to a service result.</param>
		/// <param name="commitOnUiThread">
		/// Dispatches and awaits the supplied commit callback on the UI thread. The callback itself,
		/// rather than only the final platform-view write, must be dispatched so supersession and
		/// lifetime are rechecked after queued work reaches the UI thread.
		/// </param>
		/// <param name="apply">
		/// Applies the loaded image to the platform view; receives <see langword="null"/> to clear.
		/// Invoked by the awaited UI-thread commit while the service result is still owned and alive.
		/// </param>
		/// <param name="isSourceCurrent">
		/// Re-checks that the originating virtual view still exposes the exact source instance.
		/// </param>
		/// <param name="isTargetCurrent">
		/// Re-checks that the originating virtual and platform views are still attached.
		/// </param>
		public async Task LoadAsync(
			IImageSource? source,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TImage>?>> load,
			Func<Action, Task> commitOnUiThread,
			Action<TImage?> apply,
			Func<bool> isSourceCurrent,
			Func<bool> isTargetCurrent)
		{
			ArgumentNullException.ThrowIfNull(load);
			ArgumentNullException.ThrowIfNull(commitOnUiThread);
			ArgumentNullException.ThrowIfNull(apply);
			ArgumentNullException.ThrowIfNull(isSourceCurrent);
			ArgumentNullException.ThrowIfNull(isTargetCurrent);

			var errors = new List<Exception>();

			foreach (var error in await DrainFailedResultsAsync(commitOnUiThread).ConfigureAwait(false))
				TizenCleanup.Add(errors, error);

			CancellationTokenSource cts;
			CancellationTokenSource? supersededPending;
			long generation;

			lock (_gate)
			{
				if (_disposed)
				{
					TizenCleanup.ThrowIfAny(errors);
					return;
				}

				// Supersede: whatever was in flight is now stale.
				supersededPending = _pending;
				_pending = cts = new CancellationTokenSource();
				_pendingSource = source;
				generation = ++_generation;
			}

			if (supersededPending is not null)
			{
				TryCleanup(errors, supersededPending.Cancel);
				TryCleanup(errors, supersededPending.Dispose);
			}

			if (source is null)
			{
				await CaptureCommitAsync(
					errors,
					result: null,
					requestSource: null,
					appliedSource: null,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
				TizenCleanup.ThrowIfAny(errors);
				return;
			}

			IImageSourceServiceResult<TImage>? loaded = null;

			try
			{
				loaded = await load(source, cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cts.IsCancellationRequested)
			{
				// Superseded by a newer request; the newer one owns the view now.
				TizenCleanup.ThrowIfAny(errors);
				return;
			}
			catch (OperationCanceledException)
			{
				// The current service cancelled for its own reason. This is a current-source
				// failure, not supersession, so clear the image exactly like any other failure.
				await CaptureCommitAsync(
					errors,
					result: null,
					requestSource: source,
					appliedSource: null,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
				TizenCleanup.ThrowIfAny(errors);
				return;
			}
			catch (Exception)
			{
				// A failed load must not leave the previous image showing under a new source.
				await CaptureCommitAsync(
					errors,
					result: null,
					requestSource: source,
					appliedSource: null,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
				TizenCleanup.ThrowIfAny(errors);
				return;
			}

			await CaptureCommitAsync(
				errors,
				loaded,
				requestSource: source,
				appliedSource: source,
				commitOnUiThread,
				apply,
				isSourceCurrent,
				isTargetCurrent,
				cts,
				generation).ConfigureAwait(false);
			TizenCleanup.ThrowIfAny(errors);
		}

		async Task CaptureCommitAsync(
			ICollection<Exception> errors,
			IImageSourceServiceResult<TImage>? result,
			IImageSource? requestSource,
			IImageSource? appliedSource,
			Func<Action, Task> commitOnUiThread,
			Action<TImage?> apply,
			Func<bool> isSourceCurrent,
			Func<bool> isTargetCurrent,
			CancellationTokenSource cts,
			long generation)
		{
			try
			{
				await CommitAsync(
					result,
					requestSource,
					appliedSource,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				TizenCleanup.Add(errors, exception);
			}
		}

		/// <summary>
		/// Applies a completed load on the UI thread, unless it has been superseded.
		/// </summary>
		async Task CommitAsync(
			IImageSourceServiceResult<TImage>? result,
			IImageSource? requestSource,
			IImageSource? appliedSource,
			Func<Action, Task> commitOnUiThread,
			Action<TImage?> apply,
			Func<bool> isSourceCurrent,
			Func<bool> isTargetCurrent,
			CancellationTokenSource cts,
			long generation)
		{
			var uncommitted = result is null ? null : new UncommittedResult(result);
			var callbackInvoked = 0;

			if (uncommitted is not null)
			{
				lock (_gate)
					_uncommitted.Add(uncommitted);
			}

			try
			{
				await commitOnUiThread(() =>
				{
					Interlocked.Exchange(ref callbackInvoked, 1);

					if (uncommitted is not null && !uncommitted.TryBeginCallback())
					{
						RemoveUncommitted(uncommitted);
						return;
					}

					IImageSourceServiceResult<TImage>? disposePrevious = null;
					var disposeUncommitted = false;
					Exception? applyError = null;

					try
					{
						lock (_gate)
						{
							var superseded =
								_disposed ||
								_generation != generation ||
								!ReferenceEquals(_pending, cts) ||
								!ReferenceEquals(_pendingSource, requestSource) ||
								cts.IsCancellationRequested;

							// These checks run after a queued callback reaches the UI thread.
							if (superseded || !isSourceCurrent() || !isTargetCurrent())
							{
								disposeUncommitted = true;
							}
							else
							{
								try
								{
									apply(result?.Value);
								}
								catch (Exception exception)
								{
									applyError = exception;
									disposeUncommitted = true;
								}

								if (applyError is null)
								{
									disposePrevious = ReferenceEquals(_active, result) ? null : _active;
									_active = result;
									_activeSource = appliedSource;
									uncommitted?.Transfer();
								}
							}
						}

						if (applyError is not null)
						{
							TizenCleanup.Run(
								() => throw applyError,
								() => uncommitted?.DisposeOnUiThread());
						}
						else
						{
							if (disposeUncommitted)
								uncommitted?.DisposeOnUiThread();

							disposePrevious?.Dispose();
						}
					}
					finally
					{
						if (uncommitted?.IsCompleted == true)
							RemoveUncommitted(uncommitted);
					}
				}).ConfigureAwait(false);
			}
			catch
			{
				if (Volatile.Read(ref callbackInvoked) == 0)
					uncommitted?.MarkDispatchFailed();

				throw;
			}
		}

		async Task<IReadOnlyList<Exception>> DrainFailedResultsAsync(Func<Action, Task> dispatchOnUiThread)
		{
			UncommittedResult[] pending;

			lock (_gate)
				pending = _uncommitted.Where(result => result.IsDispatchFailed).ToArray();

			var errors = new List<Exception>();

			foreach (var result in pending)
			{
				try
				{
					await dispatchOnUiThread(() =>
					{
						if (!result.TryBeginCallback())
						{
							RemoveUncommitted(result);
							return;
						}

						try
						{
							result.DisposeOnUiThread();
						}
						finally
						{
							RemoveUncommitted(result);
						}
					}).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					TizenCleanup.Add(errors, exception);
				}
			}

			return errors;
		}

		void RemoveUncommitted(UncommittedResult result)
		{
			lock (_gate)
				_uncommitted.Remove(result);
		}

		/// <summary>
		/// Cancels any load in flight and releases the applied image.
		/// </summary>
		/// <remarks>
		/// Called from the handler's disconnect path. Without it, a handler torn down mid-load
		/// leaks both the token source and the loaded native image.
		/// </remarks>
		public void Dispose()
		{
			IImageSourceServiceResult<TImage>? active;
			CancellationTokenSource? pending;
			UncommittedResult[] uncommitted;
			var firstDispose = false;

			lock (_gate)
			{
				firstDispose = !_disposed;
				active = firstDispose ? _active : null;
				pending = firstDispose ? _pending : null;
				uncommitted = _uncommitted.ToArray();
				_uncommitted.Clear();

				if (firstDispose)
				{
					_disposed = true;
					_generation++;
					_active = null;
					_activeSource = null;
					_pending = null;
					_pendingSource = null;
				}
			}

			if (!firstDispose && uncommitted.Length == 0)
				return;

			var cleanup = new List<Action>
			{
				() => pending?.Cancel(),
				() => pending?.Dispose(),
			};

			foreach (var result in uncommitted)
				cleanup.Add(result.DisposeOnUiThread);
			cleanup.Add(() => active?.Dispose());

			TizenCleanup.Run(cleanup.ToArray());
		}

		static void TryCleanup(ICollection<Exception> errors, Action action)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				TizenCleanup.Add(errors, exception);
			}
		}

		sealed class UncommittedResult
		{
			const int PendingDispatch = 0;
			const int CallbackRunning = 1;
			const int DispatchFailed = 2;
			const int Completed = 3;

			readonly IImageSourceServiceResult<TImage> _result;
			int _state;

			public UncommittedResult(IImageSourceServiceResult<TImage> result) => _result = result;

			public bool IsDispatchFailed => Volatile.Read(ref _state) == DispatchFailed;

			public bool IsCompleted => Volatile.Read(ref _state) == Completed;

			public bool TryBeginCallback()
			{
				while (true)
				{
					var state = Volatile.Read(ref _state);

					if (state is CallbackRunning or Completed)
						return false;

					if (Interlocked.CompareExchange(ref _state, CallbackRunning, state) == state)
						return true;
				}
			}

			public void MarkDispatchFailed() =>
				Interlocked.CompareExchange(ref _state, DispatchFailed, PendingDispatch);

			public void Transfer() => Interlocked.Exchange(ref _state, Completed);

			public void DisposeOnUiThread()
			{
				if (Interlocked.Exchange(ref _state, Completed) == Completed)
					return;

				_result.Dispose();
			}
		}
	}
}
