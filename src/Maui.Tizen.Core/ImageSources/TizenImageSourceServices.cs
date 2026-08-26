// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Hosting;
using AppFW = Tizen.Applications;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The result of loading a Tizen image, owning the lifetime of the underlying resource.
	/// </summary>
	public sealed class TizenImageSourceServiceResult : IImageSourceServiceResult<TizenImageSource>
	{
		readonly Action? _dispose;

		public TizenImageSourceServiceResult(TizenImageSource value, Action? dispose = null)
		{
			Value = value;
			_dispose = dispose;
		}

		public TizenImageSource Value { get; }

		/// <remarks>
		/// Tizen resource URLs address an already-decoded buffer, so a result never has to be
		/// re-resolved when the display density changes.
		/// </remarks>
		public bool IsResolutionDependent => false;

		public bool IsDisposed { get; private set; }

		public void Dispose()
		{
			if (IsDisposed)
				return;

			IsDisposed = true;
			_dispose?.Invoke();
		}
	}

	/// <summary>
	/// Loads <see cref="IFileImageSource"/> images from the application's resource directories.
	/// </summary>
	/// <remarks>
	/// Named with a <c>Tizen</c> prefix because <c>Microsoft.Maui.FileImageSourceService</c>
	/// already exists in the neutral assembly.
	/// </remarks>
	public class TizenFileImageSourceService : ITizenImageSourceService, IImageSourceService<IFileImageSource>
	{
		public Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource is not IFileImageSource fileImageSource || fileImageSource.IsEmpty)
				return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);

			var filename = fileImageSource.File;
			if (string.IsNullOrEmpty(filename))
				return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);

			var image = new TizenImageSource { ResourceUrl = ResolvePath(filename) };
			var result = new TizenImageSourceServiceResult(image, image.Dispose);
			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(result);
		}

		/// <summary>
		/// Resolves a MAUI file image name to an absolute path.
		/// </summary>
		/// <remarks>
		/// MAUI image sources are extension-less by convention, so each resource category is
		/// probed with the common raster extensions before falling back to the application's
		/// own resource directory.
		/// </remarks>
		internal static string ResolvePath(string res)
		{
			if (Path.IsPathRooted(res))
				return res;

			foreach (AppFW.ResourceManager.Category category in Enum.GetValues<AppFW.ResourceManager.Category>())
			{
				foreach (var file in Candidates(res))
				{
					var path = AppFW.ResourceManager.TryGetPath(category, file);
					if (path is not null)
						return path;
				}
			}

			var app = AppFW.Application.Current;
			if (app is not null)
			{
				var resPath = app.DirectoryInfo.Resource + res;
				foreach (var file in Candidates(resPath))
				{
					if (File.Exists(file))
						return file;
				}
			}

			return res;
		}

		static string[] Candidates(string res) => [res, res + ".jpg", res + ".png", res + ".gif"];
	}

	/// <summary>
	/// Loads <see cref="IStreamImageSource"/> images by decoding the stream into a NUI buffer.
	/// </summary>
	public class TizenStreamImageSourceService : ITizenImageSourceService, IImageSourceService<IStreamImageSource>
	{
		public async Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource is not IStreamImageSource streamImageSource || streamImageSource.IsEmpty)
				return null;

			var stream = await streamImageSource.GetStreamAsync(cancellationToken).ConfigureAwait(false);
			if (stream is null)
				return null;

			await using (stream.ConfigureAwait(false))
			{
				var image = new TizenImageSource();
				await image.LoadSource(stream).ConfigureAwait(false);

				if (image.ResourceUrl is null)
				{
					image.Dispose();
					return null;
				}

				return new TizenImageSourceServiceResult(image, image.Dispose);
			}
		}
	}

	/// <summary>
	/// Registers the Tizen image source services.
	/// </summary>
	public static class TizenImageSourceServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the file and stream image source services.
		/// </summary>
		/// <remarks>
		/// Font and URI image sources belong to the image workstream and are deliberately absent.
		/// Registering non-functional stubs for them would turn a clear "no service registered"
		/// failure into a silently blank image.
		/// </remarks>
		/// <param name="services">The image source service collection.</param>
		/// <returns>The collection, for chaining.</returns>
		public static IImageSourceServiceCollection AddTizenImageSources(this IImageSourceServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.AddService<IFileImageSource>(static _ => new TizenFileImageSourceService());
			services.AddService<IStreamImageSource>(static _ => new TizenStreamImageSourceService());

			return services;
		}
	}
}
