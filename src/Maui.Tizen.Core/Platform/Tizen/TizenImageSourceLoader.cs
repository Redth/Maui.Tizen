// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deliberately free of any Tizen.NUI dependency so the host-side test project can compile and
// EXECUTE it. Load ordering and disposal are the kind of logic that looks obviously correct and is
// obviously wrong at runtime, so they are pinned by tests rather than by inspection. The
// NUI-specific part - awaiting ResourceReady - is injected as a callback and lives in
// TizenWaveBInterop.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>The outcome of applying a resolved image to a platform view.</summary>
	public enum TizenImageApplyResult
	{
		/// <summary>The platform reported the image ready.</summary>
		Success,

		/// <summary>The platform reported the image failed to load or decode.</summary>
		Failed,

		/// <summary>The apply was abandoned because the load was superseded or the handler torn down.</summary>
		Cancelled,
	}

	/// <summary>
	/// Loads image sources for a single handler, cancelling any load that is superseded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One instance per handler. Image loading is asynchronous and a property mapper can fire again
	/// long before the previous load finishes, so without this an older, slower load can complete
	/// last and overwrite the newer image — or write to a platform view whose handler has already
	/// been disconnected.
	/// </para>
	/// <para>
	/// Ownership is tracked by a monotonic generation as well as by a token. A token alone is not
	/// enough: <see cref="Cancel"/> replaces the token source, so a handler that is disconnected and
	/// then reconnected would otherwise let a load started before the disconnect complete into the
	/// new view. The generation makes that impossible, because it never goes backwards.
	/// </para>
	/// </remarks>
	public sealed class TizenImageSourceLoader : IDisposable
	{
		readonly object _sync = new();

		CancellationTokenSource? _cts;
		IDisposable? _currentResult;
		long _generation;
		bool _disposed;

		/// <summary>Gets the number of loads that have been started.</summary>
		/// <remarks>Exposed for tests; a caller has no reason to depend on it.</remarks>
		public long Generation
		{
			get
			{
				lock (_sync)
				{
					return _generation;
				}
			}
		}

		/// <summary>Cancels any load currently in flight and releases the applied image.</summary>
		/// <remarks>
		/// Call from <c>DisconnectHandler</c>. Bumping the generation here is what prevents a load
		/// that started before a disconnect from completing into a reconnected view.
		/// </remarks>
		public void Cancel()
		{
			CancellationTokenSource? previous;
			IDisposable? result;

			lock (_sync)
			{
				previous = _cts;
				_cts = null;

				result = _currentResult;
				_currentResult = null;

				_generation++;
			}

			Cancel(previous);
			result?.Dispose();
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
		/// Applies the resolved image to the platform view and reports what the platform actually
		/// did. Receives the token for this load so it can abandon a superseded wait.
		/// </param>
		/// <param name="clearImage">
		/// Clears the platform view's image. Invoked when the source becomes null, resolves to
		/// nothing, or fails — otherwise the previous image stays on screen and the control appears
		/// not to have changed at all.
		/// </param>
		public async Task LoadAsync(
			IImageSourcePart part,
			IImageSourceServiceProvider services,
			Func<TizenImageSource?, CancellationToken, Task<TizenImageApplyResult>> applyAsync,
			Action? clearImage = null)
		{
			ArgumentNullException.ThrowIfNull(part);
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(applyAsync);

			CancellationTokenSource source;
			CancellationTokenSource? superseded;
			IDisposable? supersededResult;
			long generation;

			lock (_sync)
			{
				if (_disposed)
					return;

				superseded = _cts;
				supersededResult = _currentResult;
				_currentResult = null;

				source = new CancellationTokenSource();
				_cts = source;
				generation = ++_generation;
			}

			// Cancel the previous load only after the new one is registered, so a load that observes
			// cancellation cannot conclude it is still the current one.
			Cancel(superseded);
			supersededResult?.Dispose();

			var token = source.Token;
			var imageSource = part.Source;
			var events = part as IImageSourcePartEvents;

			part.UpdateIsLoading(false);

			if (imageSource is null)
			{
				// A null source is a real state change: whatever was showing must be taken down.
				clearImage?.Invoke();
				return;
			}

			events?.LoadingStarted();
			part.UpdateIsLoading(true);

			IImageSourceServiceResult<TizenImageSource>? result = null;
			var completed = false;

			try
			{
				result = await services.GetTizenImageAsync(imageSource, token).ConfigureAwait(false);

				// Re-check both conditions. The load may have been superseded while resolving, and a
				// service is not obliged to honour the token, so the source is re-read as well.
				if (!IsCurrent(generation, token) || !ReferenceEquals(imageSource, part.Source))
				{
					result?.Dispose();
					result = null;
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				if (result?.Value is null)
				{
					clearImage?.Invoke();
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				var applied = await applyAsync(result.Value, token).ConfigureAwait(false);

				if (applied != TizenImageApplyResult.Success)
				{
					// An assigned URL is not success. The platform may still have failed to decode.
					if (applied == TizenImageApplyResult.Failed)
						clearImage?.Invoke();

					result.Dispose();
					result = null;
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				// Applying can await, so ownership must be re-checked before taking the result.
				if (!IsCurrent(generation, token) || !ReferenceEquals(imageSource, part.Source))
				{
					result.Dispose();
					result = null;
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				Adopt(generation, result);
				result = null;

				events?.LoadingCompleted(true);
				completed = true;
			}
			catch (OperationCanceledException)
			{
				events?.LoadingCompleted(false);
				completed = true;
			}
			catch (Exception ex)
			{
				clearImage?.Invoke();
				events?.LoadingFailed(ex);
				completed = true;
			}
			finally
			{
				if (!completed)
					events?.LoadingCompleted(false);

				result?.Dispose();

				// Only the load that is still current may clear the flag. A superseded load must
				// leave it set, because the load that replaced it is still running.
				if (IsCurrent(generation, token) && ReferenceEquals(imageSource, part.Source))
					part.UpdateIsLoading(false);

				lock (_sync)
				{
					if (ReferenceEquals(_cts, source))
						_cts = null;
				}

				source.Dispose();
			}
		}

		bool IsCurrent(long generation, CancellationToken token)
		{
			if (token.IsCancellationRequested)
				return false;

			lock (_sync)
			{
				return !_disposed && _generation == generation;
			}
		}

		/// <summary>Takes ownership of the applied result, disposing whatever it replaces.</summary>
		void Adopt(long generation, IDisposable result)
		{
			IDisposable? replaced;

			lock (_sync)
			{
				if (_disposed || _generation != generation)
				{
					// Ownership moved on while we were applying; this result is already stale.
					replaced = result;
				}
				else
				{
					replaced = _currentResult;
					_currentResult = result;
				}
			}

			replaced?.Dispose();
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
