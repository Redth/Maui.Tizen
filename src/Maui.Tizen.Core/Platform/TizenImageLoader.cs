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
		IImageSourceServiceResult<TImage>? _active;
		IImageSource? _activeSource;
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
		/// <param name="apply">
		/// Applies the loaded image to the platform view; receives <see langword="null"/> to clear.
		/// Always invoked on the caller's continuation, which the caller is responsible for having
		/// marshalled to the UI thread.
		/// </param>
		/// <param name="isStillCurrent">
		/// Re-checked immediately before applying. Returns <see langword="false"/> when the handler
		/// has been disconnected or reconnected to a different platform view.
		/// </param>
		public async Task LoadAsync(
			IImageSource? source,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TImage>?>> load,
			Action<TImage?> apply,
			Func<bool> isStillCurrent)
		{
			ArgumentNullException.ThrowIfNull(load);
			ArgumentNullException.ThrowIfNull(apply);
			ArgumentNullException.ThrowIfNull(isStillCurrent);

			CancellationTokenSource cts;

			lock (_gate)
			{
				if (_disposed)
					return;

				// Supersede: whatever was in flight is now stale.
				_pending?.Cancel();
				_pending?.Dispose();
				_pending = cts = new CancellationTokenSource();
			}

			if (source is null)
			{
				Commit(result: null, source: null, apply, isStillCurrent, cts);
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
				Commit(result: null, source: null, apply, isStillCurrent, cts);
				return;
			}

			Commit(loaded, source, apply, isStillCurrent, cts);
		}

		/// <summary>
		/// Applies a completed load, unless it has been superseded in the meantime.
		/// </summary>
		void Commit(
			IImageSourceServiceResult<TImage>? result,
			IImageSource? source,
			Action<TImage?> apply,
			Func<bool> isStillCurrent,
			CancellationTokenSource cts)
		{
			lock (_gate)
			{
				var superseded = _disposed || !ReferenceEquals(_pending, cts) || cts.IsCancellationRequested;

				// The identity check has to be inside the lock and after the supersession check:
				// the handler could have been disconnected while the load was running.
				if (superseded || !isStillCurrent())
				{
					result?.Dispose();
					return;
				}

				// Replacing: the outgoing result owns a native handle and must be released.
				_active?.Dispose();
				_active = result;
				_activeSource = source;
			}

			apply(result?.Value);
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
				active = _active;
				pending = _pending;
				_active = null;
				_activeSource = null;
				_pending = null;
			}

			pending?.Cancel();
			pending?.Dispose();
			active?.Dispose();
		}
	}
}
