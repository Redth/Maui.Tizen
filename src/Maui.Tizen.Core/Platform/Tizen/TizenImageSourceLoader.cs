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
	/// <summary>
	/// Performs a native mutation on the image's platform view, if the load still owns it.
	/// </summary>
	/// <param name="mutate">The native write. Runs under the loader's lock; must not block or re-enter.</param>
	/// <returns><see langword="true"/> if the write was performed; <see langword="false"/> if the load has been superseded, cancelled or disconnected.</returns>
	public delegate bool TizenImageWrite(Action mutate);

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
			Func<TizenImageSource?, TizenImageWrite, CancellationToken, Task<TizenImageApplyResult>> applyAsync,
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
				TryMutate(generation, token, part, imageSource, clearImage);
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
					TryMutate(generation, token, part, imageSource, clearImage);
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				// The native write itself is generation-guarded, so it cannot land after this load
				// has been superseded or the handler disconnected. Refusing the write also tells
				// the apply step to stop rather than wait for a decode notification that will never
				// arrive for an image it was not allowed to assign.
				bool Write(Action mutate) => TryMutate(generation, token, part, imageSource, mutate);

				var applied = await applyAsync(result.Value, Write, token).ConfigureAwait(false);

				var stillOurs = IsCurrent(generation, token) && ReferenceEquals(imageSource, part.Source);

				if (applied != TizenImageApplyResult.Success)
				{
					// An assigned URL is not success. The platform may still have failed to decode.
					if (applied == TizenImageApplyResult.Failed)
						TryMutate(generation, token, part, imageSource, clearImage);

					result.Dispose();
					result = null;
					events?.LoadingCompleted(false);
					completed = true;
					return;
				}

				if (!stillOurs)
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
				// A failure only counts if this load still owns the part. Without this guard a slow
				// load A that throws after a later load B has already succeeded would clear B's
				// image and report B as failed, so the control would end up blank with an error
				// raised for a source it is no longer even displaying.
				if (TryMutate(generation, token, part, imageSource, clearImage))
				{
					events?.LoadingFailed(ex);
				}
				else
				{
					// Superseded: report the same non-applied outcome as the other stale paths and
					// leave the platform view to the load that replaced this one.
					events?.LoadingCompleted(false);
				}

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

		/// <summary>
		/// Performs a native mutation if and only if this load still owns the view, atomically.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the only place a load is allowed to touch the platform view. Every ownership
		/// check and every native write goes through it, under the same lock that guards the
		/// generation, so the two cannot be interleaved.
		/// </para>
		/// <para>
		/// Checking ownership and then mutating as two steps is not enough, however close together
		/// they sit: a load can be superseded — or the handler disconnected — in the window between
		/// them, and the stale write then lands on a view that now belongs to a newer load, or to
		/// nothing at all. Holding the lock across both is what closes that window, because
		/// <see cref="Cancel()"/> and <see cref="LoadAsync"/> both take the same lock to move the
		/// generation.
		/// </para>
		/// <para>
		/// <paramref name="mutate"/> therefore runs while the lock is held and <b>must not</b> call
		/// back into this loader or block. In practice it is a single property assignment on the
		/// platform view.
		/// </para>
		/// </remarks>
		/// <returns>
		/// <see langword="true"/> if this load still owns the view — in which case
		/// <paramref name="mutate"/>, when supplied, has been applied.
		/// </returns>
		/// <remarks>
		/// The return value reports <em>ownership</em>, not whether an action ran, so a caller with
		/// nothing to mutate can still use it to decide whether to report an outcome. Conflating the
		/// two silently suppressed <c>LoadingFailed</c> whenever no clear action was supplied.
		/// </remarks>
		bool TryMutate(long generation, CancellationToken token, IImageSourcePart part, IImageSource? source, Action? mutate)
		{
			lock (_sync)
			{
				if (_disposed || _generation != generation || token.IsCancellationRequested)
					return false;

				// Re-read inside the lock: a service is not obliged to honour the token, so the
				// source can change without the generation moving.
				if (!ReferenceEquals(source, part.Source))
					return false;

				mutate?.Invoke();
				return true;
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
