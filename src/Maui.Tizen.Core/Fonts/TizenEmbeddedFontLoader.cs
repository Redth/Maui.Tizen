// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Kept free of Tizen.NUI so the same source compiles and RUNS on the host test lane. The platform
// pieces this needs - where the app's resource and data directories are, and how a directory is
// handed to NUI's FontClient - are behind ITizenFontDirectoryProvider, whose real implementation
// lives in TizenPlatformFontDirectoryProvider.cs.

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using IOPath = System.IO.Path;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Supplies the application directories and the font-directory registration that
	/// <see cref="TizenEmbeddedFontLoader"/> needs.
	/// </summary>
	public interface ITizenFontDirectoryProvider
	{
		/// <summary>Gets the application's read-only resource directory.</summary>
		string ResourceDirectory { get; }

		/// <summary>Gets the application's writable data directory.</summary>
		string DataDirectory { get; }

		/// <summary>Tells the platform to look for fonts in <paramref name="path"/>.</summary>
		void AddCustomFontDirectory(string path);
	}

	/// <summary>
	/// Loads fonts embedded in an assembly so <c>ConfigureFonts</c> aliases resolve on Tizen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Without this, MAUI's neutral <c>EmbeddedFontLoader</c> is used. It has no Tizen
	/// implementation, so an embedded font never reaches NUI's font client and every
	/// <c>ConfigureFonts</c> alias silently falls back to the system font - text renders, just in
	/// the wrong typeface, with nothing thrown and nothing logged.
	/// </para>
	/// <para>
	/// Fonts shipped as app resources are used in place; anything else is copied into a cache
	/// directory under the app's data directory, which is then registered with the font client.
	/// </para>
	/// </remarks>
	public class TizenEmbeddedFontLoader : IEmbeddedFontLoader
	{
		const string FontCacheFolderName = "fonts";

		readonly ITizenFontDirectoryProvider _directories;
		readonly ILogger<TizenEmbeddedFontLoader>? _logger;

		public TizenEmbeddedFontLoader(ITizenFontDirectoryProvider directories, ILogger<TizenEmbeddedFontLoader>? logger = null)
		{
			ArgumentNullException.ThrowIfNull(directories);

			_directories = directories;
			_logger = logger;
		}

		/// <summary>Gets the directory previously loaded fonts are cached in, once one is created.</summary>
		public DirectoryInfo? FontCacheDirectory { get; private set; }

		/// <inheritdoc/>
		public string? LoadFont(EmbeddedFont font)
		{
			ArgumentNullException.ThrowIfNull(font);

			if (string.IsNullOrEmpty(font.FontName))
			{
				_logger?.LogWarning("An embedded font was registered with no file name, so it cannot be loaded.");
				return null;
			}

			// A font shipped as an app resource is already on disk and already visible to the font
			// client, so it is used where it is rather than copied.
			var resourceFilePath = IOPath.Combine(_directories.ResourceDirectory, FontCacheFolderName, font.FontName);
			if (File.Exists(resourceFilePath))
				return IOPath.GetFileNameWithoutExtension(resourceFilePath);

			string filePath;

			try
			{
				if (FontCacheDirectory == null)
				{
					FontCacheDirectory = Directory.CreateDirectory(IOPath.Combine(_directories.DataDirectory, FontCacheFolderName));
					_directories.AddCustomFontDirectory(FontCacheDirectory.FullName);
				}

				filePath = IOPath.Combine(FontCacheDirectory.FullName, font.FontName);
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "Could not create the font cache directory, so {Font} was not loaded.", font.FontName);
				return null;
			}

			var name = IOPath.GetFileNameWithoutExtension(filePath);

			if (File.Exists(filePath))
				return name;

			try
			{
				if (font.ResourceStream == null)
					throw new InvalidOperationException($"The embedded font '{font.FontName}' has no resource stream.");

				using (var fileStream = File.Create(filePath))
				{
					font.ResourceStream.CopyTo(fileStream);
				}

				_directories.AddCustomFontDirectory(FontCacheDirectory!.FullName);

				return name;
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "Failed to load the embedded font {Font}.", font.FontName);

				// A half-written file would be cached and returned as a success by the File.Exists
				// check above on every subsequent call, so it has to go. Deleting can itself throw,
				// and upstream let that escape and replace the original failure.
				try
				{
					File.Delete(filePath);
				}
				catch (Exception cleanupFailure)
				{
					_logger?.LogWarning(cleanupFailure, "Could not remove the partially written font file {Path}.", filePath);
				}

				return null;
			}
		}
	}
}
