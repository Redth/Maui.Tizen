using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using TizenApplication = Tizen.Applications.Application;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IFileSystem"/>, backed by the application's
	/// <c>DirectoryInfo</c> data, cache and resource paths.
	/// </summary>
	public sealed class TizenFileSystem : IFileSystem
	{
		/// <inheritdoc/>
		public string CacheDirectory => TizenApplication.Current.DirectoryInfo.Cache;

		/// <inheritdoc/>
		public string AppDataDirectory => TizenApplication.Current.DirectoryInfo.Data;

		/// <inheritdoc/>
		public Task<Stream> OpenAppPackageFileAsync(string filename) =>
			Task.FromResult<Stream>(File.OpenRead(GetFullAppPackageFilePath(filename)));

		/// <inheritdoc/>
		public Task<bool> AppPackageFileExistsAsync(string filename) =>
			Task.FromResult(File.Exists(GetFullAppPackageFilePath(filename)));

		/// <summary>
		/// Resolves a packaged resource file name to its full on-device path.
		/// </summary>
		/// <param name="filename">The resource-relative file name.</param>
		/// <returns>The absolute path inside the application's resource directory.</returns>
		public static string GetFullAppPackageFilePath(string filename)
		{
			if (string.IsNullOrWhiteSpace(filename))
				throw new ArgumentNullException(nameof(filename));

			return Path.Combine(
				TizenApplication.Current.DirectoryInfo.Resource,
				filename.Replace('\\', Path.DirectorySeparatorChar));
		}

		/// <summary>
		/// Resolves the MIME type for a file extension using <c>Tizen.Content.MimeType</c>.
		/// </summary>
		/// <param name="extension">The file extension, with or without a leading dot.</param>
		/// <returns>The resolved MIME type.</returns>
		public static string GetContentType(string extension)
		{
			ArgumentNullException.ThrowIfNull(extension);

			return global::Tizen.Content.MimeType.MimeUtil.GetMimeType(extension.TrimStart('.'));
		}
	}
}
