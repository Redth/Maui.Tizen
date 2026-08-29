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

			// These awaits deliberately do NOT use ConfigureAwait(false). TizenImageSource
			// registers the decoded buffer with NUI, which is only legal on the main loop;
			// resuming on the captured context keeps the continuation there. The service is
			// invoked from a property mapper, so the captured context is the Tizen main loop.
			var stream = await streamImageSource.GetStreamAsync(cancellationToken);
			if (stream is null)
				return null;

			await using (stream)
			{
				var image = new TizenImageSource();
				await image.LoadSource(stream);

				if (image.ResourceUrl is null)
				{
					image.Dispose();
					return null;
				}

				return new TizenImageSourceServiceResult(image, image.Dispose);
			}
		}
	}
}
