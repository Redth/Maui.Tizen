// Ported from dotnet/maui as part of the Maui.Tizen extraction.
// Standalone Tizen service; see ITizenImageSourceService.cs for why the neutral type is not extended.
#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using AppFW = Tizen.Applications;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Loads an <see cref="IFileImageSource"/> from the application's resource directories.</summary>
	public class TizenFileImageSourceService : TizenImageSourceService, ITizenImageSourceService<IFileImageSource>
	{
		public TizenFileImageSourceService()
			: this(null)
		{
		}

		public TizenFileImageSourceService(ILogger<TizenFileImageSourceService>? logger = null)
			: base(logger)
		{
		}

		public override Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default) =>
			GetImageAsync((IFileImageSource)imageSource, cancellationToken);

		public Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IFileImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return FromResult(null);

			var filename = imageSource.File;
			try
			{
				if (!string.IsNullOrEmpty(filename))
				{
					var image = new MauiImageSource
					{
						ResourceUrl = GetPath(filename)
					};
					var result = new ImageSourceServiceResult(image, () => image.Dispose());
					return FromResult(result);
				}
				else
				{
					throw new InvalidOperationException("Unable to load image file.");
				}
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to load image file '{File}'.", filename);
				throw;
			}
		}

		static string GetPath(string res)
		{
			if (Path.IsPathRooted(res))
			{
				return res;
			}

			foreach (AppFW.ResourceManager.Category category in Enum.GetValues<AppFW.ResourceManager.Category>())
			{
				foreach (var file in new[] { res, res + ".jpg", res + ".png", res + ".gif" })
				{
					var path = AppFW.ResourceManager.TryGetPath(category, file);

					if (path != null)
					{
						return path;
					}
				}
			}

			AppFW.Application app = AppFW.Application.Current;
			if (app != null)
			{
				string resPath = app.DirectoryInfo.Resource + res;

				foreach (var file in new[] { resPath, resPath + ".jpg", resPath + ".png", resPath + ".gif" })
				{
					if (File.Exists(file))
					{
						return file;
					}
				}
			}

			return res;
		}
	}
}
