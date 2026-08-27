// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deliberately free of any Tizen.NUI dependency so the host-side test project can compile and
// EXECUTE it. Cancellation ordering is the kind of logic that looks obviously correct and is
// obviously wrong at runtime, so it is pinned by tests rather than by inspection. The NUI-specific
// part - awaiting ResourceReady - is injected as a callback and lives in TizenWaveBInterop.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Loads image sources for a single handler, cancelling any load that is superseded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One instance per handler. Image loading is asynchronous and a property mapper can fire again
	/// long before the previous load finishes, so without this an older, slower load can complete
	/// last and overwrite the newer image — and can write to a platform view whose handler has
	/// already been disconnected.
	/// </para>
	/// <para>
	/// Upstream relied only on re-reading <c>IImageSourcePart.Source</c> after the await. That
	/// closes the common case but not disconnection, and it never cancels the in-flight work.
	/// </para>
	/// </remarks>
	public sealed class TizenImageSourceLoader : IDisposable
	{
		readonly object _sync = new();

		CancellationTokenSource? _cts;
		bool _disposed;

		/// <summary>Cancels any load currently in flight.</summary>
		/// <remarks>Call from <c>DisconnectHandler</c> so a pending load cannot touch a released view.</remarks>
		public void Cancel()
		{
			CancellationTokenSource? previous;

			lock (_sync)
			{
				previous = _cts;
				_cts = null;
			}

			Cancel(previous);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			lock (_sync)
			{
				if (_disposed)
					return;

				_disposed = true;
			}

			Cancel();
		}

		/// <summary>
		/// Resolves <paramref name="part"/>'s source and applies it, cancelling any previous load.
		/// </summary>
		/// <param name="part">The image source part being loaded.</param>
		/// <param name="services">The image source service provider.</param>
		/// <param name="applyAsync">
		/// Applies the resolved image to the platform view. Receives the token for this load so it
		/// can abandon a wait that has been superseded.
		/// </param>
		public async Task LoadAsync(
			IImageSourcePart part,
			IImageSourceServiceProvider services,
			Func<TizenImageSource?, CancellationToken, Task> applyAsync)
		{
			ArgumentNullException.ThrowIfNull(part);
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(applyAsync);

			CancellationTokenSource source;
			CancellationTokenSource? superseded;

			lock (_sync)
			{
				if (_disposed)
					return;

				superseded = _cts;
				source = new CancellationTokenSource();
				_cts = source;
			}

			// Cancel the previous load only after the new one is registered, so a load that observes
			// cancellation cannot conclude it is still the current one.
			Cancel(superseded);

			var token = source.Token;
			var imageSource = part.Source;
			var events = part as IImageSourcePartEvents;

			part.UpdateIsLoading(false);

			if (imageSource is null)
				return;

			events?.LoadingStarted();
			part.UpdateIsLoading(true);

			try
			{
				var result = await services.GetTizenImageAsync(imageSource, token).ConfigureAwait(false);

				// Re-check both conditions: the load may have been superseded while resolving, and
				// the virtual view's source may have been reassigned without a cancellation.
				if (token.IsCancellationRequested || !ReferenceEquals(imageSource, part.Source))
				{
					events?.LoadingCompleted(false);
					return;
				}

				await applyAsync(result?.Value, token).ConfigureAwait(false);

				// Applying can itself await (NUI decodes asynchronously), so check once more before
				// reporting success.
				var applied = result?.Value is not null && !token.IsCancellationRequested;

				events?.LoadingCompleted(applied);
			}
			catch (OperationCanceledException)
			{
				events?.LoadingCompleted(false);
			}
			catch (Exception ex)
			{
				events?.LoadingFailed(ex);
			}
			finally
			{
				// Only the load that is still current may clear the flag. A superseded load must
				// leave it set, because the load that replaced it is still running.
				if (!token.IsCancellationRequested && ReferenceEquals(imageSource, part.Source))
					part.UpdateIsLoading(false);

				lock (_sync)
				{
					if (ReferenceEquals(_cts, source))
						_cts = null;
				}

				source.Dispose();
			}
		}

		static void Cancel(CancellationTokenSource? source)
		{
			if (source is null)
				return;

			try
			{
				source.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The load it belonged to completed and disposed it first; nothing to cancel.
			}
		}
	}
}
