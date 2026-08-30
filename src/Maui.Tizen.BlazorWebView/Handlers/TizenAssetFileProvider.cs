// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/Tizen/TizenMauiAssetFileProvider.cs.
// The Tizen resource directory lookup is factored into a constructor parameter so the provider can be
// exercised without an initialized Tizen application.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView
{
	/// <summary>
	/// A minimal <see cref="IFileProvider"/> that serves Blazor static assets from the Tizen
	/// application's resource directory.
	/// </summary>
	internal sealed class TizenAssetFileProvider : IFileProvider
	{
		private readonly string _rootDirectory;

		/// <summary>
		/// Creates a provider rooted at <paramref name="contentRootDir"/> under the Tizen application's
		/// resource directory.
		/// </summary>
		/// <param name="resourceDirectory">The Tizen application resource directory.</param>
		/// <param name="contentRootDir">The content root, relative to the resource directory (usually <c>wwwroot</c>).</param>
		public TizenAssetFileProvider(string resourceDirectory, string contentRootDir)
		{
			ArgumentNullException.ThrowIfNull(resourceDirectory);
			_rootDirectory = Path.Combine(resourceDirectory, contentRootDir ?? string.Empty);
		}

		internal string RootDirectory => _rootDirectory;

		public IDirectoryContents GetDirectoryContents(string subpath)
			=> NotFoundDirectoryContents.Singleton;

		public IFileInfo GetFileInfo(string subpath)
			=> new TizenAssetFileInfo(Path.Combine(_rootDirectory, Normalize(subpath)));

		public IChangeToken Watch(string filter)
			=> NullChangeToken.Singleton;

		private static string Normalize(string? subpath)
		{
			if (string.IsNullOrEmpty(subpath))
			{
				return string.Empty;
			}

			// WebViewManager hands out rooted paths such as "/index.html"; Path.Combine would treat those as
			// absolute and discard the resource root.
			return subpath.TrimStart('/', '\\');
		}

		private sealed class TizenAssetFileInfo : IFileInfo
		{
			private readonly string _filePath;

			public TizenAssetFileInfo(string filePath)
			{
				_filePath = filePath;
				Name = Path.GetFileName(_filePath);

				var fileInfo = new FileInfo(_filePath);
				Exists = fileInfo.Exists;
				Length = Exists ? fileInfo.Length : -1;
			}

			public bool Exists { get; }

			public long Length { get; }

			public string PhysicalPath { get; } = null!;

			public string Name { get; }

			public DateTimeOffset LastModified { get; } = DateTimeOffset.FromUnixTimeSeconds(0);

			public bool IsDirectory => false;

			public Stream CreateReadStream() => File.OpenRead(_filePath);
		}

		/// <summary>
		/// Directory enumeration is never used by <c>BlazorWebView</c> or <c>WebViewManager</c>.
		/// </summary>
		private sealed class NotFoundDirectoryContents : IDirectoryContents
		{
			public static readonly NotFoundDirectoryContents Singleton = new();

			public bool Exists => false;

			public IEnumerator<IFileInfo> GetEnumerator() => Enumerable();

			IEnumerator IEnumerable.GetEnumerator() => Enumerable();

			private static IEnumerator<IFileInfo> Enumerable()
			{
				yield break;
			}
		}
	}
}
