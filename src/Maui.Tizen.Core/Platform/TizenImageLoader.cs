// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
	/// <b>Failure clearing.</b> A failed or cancelled load must clear the image rather than leave
	/// the previous one, which would silently show stale content for the new source.
	/// </description></item>
	/// <item><description>
	/// <b>Ownership.</b> The service result holds a native handle. Whoever replaces it must
	/// dispose the one it replaced, and disconnecting must dispose the last one - otherwise every
	/// source change leaks a NUI image buffer.
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

			CancellationTokenSource cts;
			long generation;

			lock (_gate)
			{
				if (_disposed)
					return;

				// Supersede: whatever was in flight is now stale.
				_pending?.Cancel();
				_pending?.Dispose();
				_pending = cts = new CancellationTokenSource();
				_pendingSource = source;
				generation = ++_generation;
			}

			if (source is null)
			{
				await CommitAsync(
					result: null,
					requestSource: null,
					appliedSource: null,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
				return;
			}

			IImageSourceServiceResult<TImage>? loaded = null;

			try
			{
				loaded = await load(source, cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Superseded by a newer request; the newer one owns the view now.
				return;
			}
			catch (Exception)
			{
				// A failed load must not leave the previous image showing under a new source.
				await CommitAsync(
					result: null,
					requestSource: source,
					appliedSource: null,
					commitOnUiThread,
					apply,
					isSourceCurrent,
					isTargetCurrent,
					cts,
					generation).ConfigureAwait(false);
				return;
			}

			await CommitAsync(
				loaded,
				requestSource: source,
				appliedSource: source,
				commitOnUiThread,
				apply,
				isSourceCurrent,
				isTargetCurrent,
				cts,
				generation).ConfigureAwait(false);
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
			IImageSourceServiceResult<TImage>? previous = null;
			var committed = false;

			try
			{
				await commitOnUiThread(() =>
				{
					lock (_gate)
					{
						var superseded =
							_disposed ||
							_generation != generation ||
							!ReferenceEquals(_pending, cts) ||
							!ReferenceEquals(_pendingSource, requestSource) ||
							cts.IsCancellationRequested;

						// These checks run after a queued callback reaches the UI thread. Checking
						// them before dispatch leaves a window in which a newer load or disconnect
						// can invalidate the result while the callback is still waiting.
						if (superseded || !isSourceCurrent() || !isTargetCurrent())
							return;

						// Keep both results alive until the platform has accepted the new image.
						// Holding the gate across this short UI-thread write prevents a concurrent
						// supersession or Dispose from releasing the new result underneath apply.
						apply(result?.Value);

						previous = _active;
						_active = result;
						_activeSource = appliedSource;
						committed = true;
					}
				}).ConfigureAwait(false);
			}
			finally
			{
				if (committed)
				{
					if (!ReferenceEquals(previous, result))
						previous?.Dispose();
				}
				else
					result?.Dispose();
			}
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

			lock (_gate)
			{
				if (_disposed)
					return;

				_disposed = true;
				_generation++;
				active = _active;
				pending = _pending;
				_active = null;
				_activeSource = null;
				_pending = null;
				_pendingSource = null;
			}

			pending?.Cancel();
			pending?.Dispose();
			active?.Dispose();
		}
	}
}
