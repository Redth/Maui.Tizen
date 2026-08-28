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
		long _generation;

		public long Begin() => Interlocked.Increment(ref _generation);

		public void Invalidate() => Interlocked.Increment(ref _generation);

		public bool IsCurrent(long generation) => Volatile.Read(ref _generation) == generation;
	}

	internal static class TizenImageLoaderExtensions
	{
		public static async Task LoadPartAsync<TImage>(
			this TizenImageLoader<TImage> loader,
			IImageSourcePart part,
			TizenImageLoadEvents loadEvents,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TImage>?>> load,
			Func<Action, Task> commitOnUiThread,
			Action<TImage?> apply,
			Func<bool> isTargetCurrent)
			where TImage : class
		{
			ArgumentNullException.ThrowIfNull(loader);
			ArgumentNullException.ThrowIfNull(part);
			ArgumentNullException.ThrowIfNull(loadEvents);
			ArgumentNullException.ThrowIfNull(load);
			ArgumentNullException.ThrowIfNull(commitOnUiThread);
			ArgumentNullException.ThrowIfNull(apply);
			ArgumentNullException.ThrowIfNull(isTargetCurrent);

			var generation = loadEvents.Begin();
			var source = part.Source;
			var events = part as IImageSourcePartEvents;
			Exception? loadFailure = null;
			var reported = false;

			part.UpdateIsLoading(false);

			if (source is null)
			{
				await loader.LoadAsync(
					source,
					load,
					commitOnUiThread,
					apply,
					() => part.Source is null,
					isTargetCurrent).ConfigureAwait(false);
				return;
			}

			events?.LoadingStarted();
			part.UpdateIsLoading(true);

			try
			{
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
					apply,
					() => ReferenceEquals(part.Source, source),
					isTargetCurrent).ConfigureAwait(false);

				if (!loadEvents.IsCurrent(generation)
					|| !ReferenceEquals(part.Source, source)
					|| !isTargetCurrent())
				{
					events?.LoadingCompleted(false);
					reported = true;
					return;
				}

				if (loadFailure is not null)
					events?.LoadingFailed(loadFailure);
				else
					events?.LoadingCompleted(
						ReferenceEquals(loader.CurrentSource, source) && loader.Current is not null);

				reported = true;
			}
			catch (Exception exception)
			{
				if (!reported)
				{
					if (loadEvents.IsCurrent(generation)
						&& ReferenceEquals(part.Source, source)
						&& isTargetCurrent())
						events?.LoadingFailed(exception);
					else
						events?.LoadingCompleted(false);
				}

				throw;
			}
			finally
			{
				if (loadEvents.IsCurrent(generation)
					&& ReferenceEquals(part.Source, source)
					&& isTargetCurrent())
					part.UpdateIsLoading(false);
			}
		}
	}
}
