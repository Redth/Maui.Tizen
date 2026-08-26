using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Shared helpers: repository locations resolved from build-time metadata, throwaway working
/// directories, and small factories for MSBuild task items.
/// </summary>
public abstract class TestBase : IDisposable
{
	private readonly List<string> _tempDirectories = new();

	public static string RepositoryRoot { get; } = ReadMetadata("RepositoryRoot");

	public static string ResizetizerPackageVersion { get; } = ReadMetadata("MauiResizetizerPackageVersion");

	public static string PackageVersion { get; } = ReadMetadata("PackageVersion");

	/// <summary>The pinned Microsoft.AspNetCore.Components.WebView version, for Blazor scenarios.</summary>
	public static string WebViewPackageVersion { get; } = ReadMetadata("WebViewPackageVersion");

	public static string BuildTasksProjectDirectory { get; } =
		Path.Combine(RepositoryRoot, "src", "Maui.Tizen.Build.Tasks");

	public static string BuildTransitiveDirectory { get; } =
		Path.Combine(BuildTasksProjectDirectory, "buildTransitive");

	public static string BuildTasksAssemblyPath { get; } =
		typeof(GenerateTizenManifest).Assembly.Location;

	/// <summary>Escapes a path for use inside a generated MSBuild attribute.</summary>
	public static string Escape(string path)
		=> path.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

	private static string ReadMetadata(string key)
	{
		var value = typeof(TestBase).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(a => a.Key == key)?.Value;

		if (string.IsNullOrEmpty(value))
			throw new InvalidOperationException($"Assembly metadata '{key}' was not provided by the build.");

		return value!;
	}

	protected string CreateTempDirectory(string prefix = "maui-tizen-test")
	{
		var path = Path.Combine(ResolveRealPath(Path.GetTempPath()), $"{prefix}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		_tempDirectories.Add(path);
		return path;
	}

	/// <summary>
	/// Canonicalizes a directory path by resolving symlinks in every segment.
	/// </summary>
	/// <remarks>
	/// This is not cosmetic. The Resizetizer compares a wildcard glob of its intermediate
	/// image folder (which follows the project path exactly as written) against the paths its
	/// own task returns (which the runtime canonicalizes), and deletes anything present in the
	/// first but not the second. When the two spellings differ it therefore deletes every image
	/// it has just written.
	///
	/// On macOS this is not hypothetical: the temp root is /var/folders/... where /var is a
	/// symlink to private/var, so a test run from an uncanonicalized temp path exercises a
	/// pipeline whose images have all been deleted. Resolving one segment is not enough either,
	/// because the symlink is an ancestor rather than the leaf.
	/// </remarks>
	protected static string ResolveRealPath(string path)
	{
		var full = Path.GetFullPath(path);
		var root = Path.GetPathRoot(full) ?? string.Empty;
		var segments = full.Substring(root.Length)
			.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

		var current = root;
		foreach (var segment in segments)
		{
			current = Path.Combine(current, segment);

			var target = Directory.ResolveLinkTarget(current, returnFinalTarget: true)?.FullName;
			if (!string.IsNullOrEmpty(target))
				current = target!;
		}

		return current;
	}

	protected static ITaskItem Item(string spec, params (string Name, string Value)[] metadata)
	{
		var item = new TaskItem(spec);
		foreach (var (name, value) in metadata)
			item.SetMetadata(name, value);
		return item;
	}

	/// <summary>Writes a tiny valid PNG of the requested size.</summary>
	protected static string WritePng(string path, int width, int height)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		using var bitmap = new SkiaSharp.SKBitmap(width, height);
		using (var canvas = new SkiaSharp.SKCanvas(bitmap))
		{
			canvas.Clear(SkiaSharp.SKColors.Red);
		}

		using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(path);
		data.SaveTo(stream);

		return path;
	}

	public void Dispose()
	{
		foreach (var directory in _tempDirectories)
		{
			try
			{
				if (Directory.Exists(directory))
					Directory.Delete(directory, recursive: true);
			}
			catch (IOException)
			{
				// Best effort cleanup.
			}
		}

		GC.SuppressFinalize(this);
	}
}
