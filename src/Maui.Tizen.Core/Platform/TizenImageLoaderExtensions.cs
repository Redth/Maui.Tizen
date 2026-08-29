// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class TizenImageLoadEvents
	{
		readonly object _gate = new();
		long _generation;
		IImageSourcePart? _originatingPart;
		CancellationTokenSource? _readinessCancellation;

		public (long Generation, CancellationToken Token) Begin(IImageSourcePart part)
		{
			CancellationTokenSource? previous;
			CancellationTokenSource current;
			long generation;

			lock (_gate)
			{
				_originatingPart = part;
				previous = _readinessCancellation;
				_readinessCancellation = current = new CancellationTokenSource();
				generation = ++_generation;
			}

			TizenCleanup.Run(
				() => previous?.Cancel(),
				() => previous?.Dispose());

			return (generation, current.Token);
		}

		public void Invalidate()
		{
			IImageSourcePart? part;
			CancellationTokenSource? readiness;

			lock (_gate)
			{
				_generation++;
				part = _originatingPart;
				_originatingPart = null;
				readiness = _readinessCancellation;
				_readinessCancellation = null;
			}

			TizenCleanup.Run(
				() => readiness?.Cancel(),
				() => readiness?.Dispose(),
				() => part?.UpdateIsLoading(false));
		}

		public bool IsCurrent(long generation) => Volatile.Read(ref _generation) == generation;
	}

	internal static class TizenImageLoaderExtensions
	{
		public static Task LoadPartAsync<TImage>(
			this TizenImageLoader<TImage> loader,
			IImageSourcePart part,
			TizenImageLoadEvents loadEvents,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TImage>?>> load,
			Func<Action, Task> commitOnUiThread,
			Action<TImage?> apply,
			Func<bool> isTargetCurrent)
			where TImage : class
			=> LoadPartAsync(
				loader,
				part,
				loadEvents,
				load,
				commitOnUiThread,
				(image, _) =>
				{
					apply(image);
					return Task.FromResult(true);
				},
				isTargetCurrent);

		public static async Task LoadPartAsync<TImage>(
			this TizenImageLoader<TImage> loader,
			IImageSourcePart part,
			TizenImageLoadEvents loadEvents,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TImage>?>> load,
			Func<Action, Task> commitOnUiThread,
			Func<TImage?, CancellationToken, Task<bool>> applyAndWaitForReady,
			Func<bool> isTargetCurrent)
			where TImage : class
		{
			ArgumentNullException.ThrowIfNull(loader);
			ArgumentNullException.ThrowIfNull(part);
			ArgumentNullException.ThrowIfNull(loadEvents);
			ArgumentNullException.ThrowIfNull(load);
			ArgumentNullException.ThrowIfNull(commitOnUiThread);
			ArgumentNullException.ThrowIfNull(applyAndWaitForReady);
			ArgumentNullException.ThrowIfNull(isTargetCurrent);

			var request = loadEvents.Begin(part);
			var generation = request.Generation;
			var source = part.Source;
			var events = part as IImageSourcePartEvents;
			Exception? loadFailure = null;
			var reported = false;

			part.UpdateIsLoading(false);

			if (source is null)
			{
				Task<bool>? clearReady = null;
				await loader.LoadAsync(
					source,
					load,
					commitOnUiThread,
					image => clearReady = applyAndWaitForReady(image, request.Token),
					() => part.Source is null,
					isTargetCurrent).ConfigureAwait(false);
				if (clearReady is not null)
					await clearReady.ConfigureAwait(false);
				return;
			}

			events?.LoadingStarted();
			part.UpdateIsLoading(true);

			try
			{
				Task<bool>? targetReady = null;
				await loader.LoadAsync(
					source,
					async (imageSource, token) =>
					{
						try
						{
							return await load(imageSource, token).ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception exception)
						{
							loadFailure = exception;
							throw;
						}
					},
					commitOnUiThread,
					image => targetReady = applyAndWaitForReady(image, request.Token),
					() => ReferenceEquals(part.Source, source),
					isTargetCurrent).ConfigureAwait(false);

				var readinessSucceeded = targetReady is null || await targetReady.ConfigureAwait(false);
				if (!readinessSucceeded
					&& loadEvents.IsCurrent(generation)
					&& ReferenceEquals(part.Source, source)
					&& isTargetCurrent())
				{
					Task<bool>? clearReady = null;
					await loader.LoadAsync(
						null,
						load,
						commitOnUiThread,
						image => clearReady = applyAndWaitForReady(image, request.Token),
						() => ReferenceEquals(part.Source, source),
						isTargetCurrent).ConfigureAwait(false);
					if (clearReady is not null)
						await clearReady.ConfigureAwait(false);
				}

				await commitOnUiThread(() =>
				{
					if (!loadEvents.IsCurrent(generation)
						|| !ReferenceEquals(part.Source, source)
						|| !isTargetCurrent())
					{
						reported = true;
						events?.LoadingCompleted(false);
						return;
					}

					reported = true;
					TizenCleanup.Run(
						() =>
						{
							if (loadFailure is not null)
								events?.LoadingFailed(loadFailure);
							else
								events?.LoadingCompleted(
									readinessSucceeded &&
									ReferenceEquals(loader.CurrentSource, source) &&
									loader.Current is not null);
						},
						() => part.UpdateIsLoading(false));
				}).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				if (!reported)
				{
					try
					{
						await commitOnUiThread(() =>
						{
							if (loadEvents.IsCurrent(generation)
								&& ReferenceEquals(part.Source, source)
								&& isTargetCurrent())
							{
								TizenCleanup.Run(
									() => events?.LoadingFailed(exception),
									() => part.UpdateIsLoading(false));
							}
							else
							{
								events?.LoadingCompleted(false);
							}
						}).ConfigureAwait(false);
					}
					catch
					{
						// A rejected dispatcher cannot safely run lifecycle callbacks. Disconnect
						// invalidates the originating part synchronously and clears IsLoading.
					}
				}

				throw;
			}
		}
	}
}
