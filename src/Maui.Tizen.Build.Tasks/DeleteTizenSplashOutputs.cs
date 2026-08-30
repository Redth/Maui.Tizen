#nullable enable
using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>Deletes only splash outputs recorded in the backend-owned splash map.</summary>
	public sealed class DeleteTizenSplashOutputs : Task
	{
		[Required]
		public string SplashScreenMapFile { get; set; } = null!;

		[Required]
		public string IntermediateOutputPath { get; set; } = null!;

		public override bool Execute()
		{
			try
			{
				var intermediate = Path.GetFullPath(IntermediateOutputPath);
				var splash = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashDirectoryName);

				TizenSplashOutputOwnership.RejectReparsePoint(SplashScreenMapFile, "splash ownership map");
				if (Directory.Exists(splash))
				{
					TizenSplashOutputOwnership.RejectReparsePoint(splash, "splash output directory");
					TizenSplashOutputOwnership.DeletePreviouslyOwnedOutputs(
						SplashScreenMapFile,
						intermediate,
						splash);
				}
			}
			catch (Exception ex)
			{
				Log.LogErrorFromException(ex);
			}

			return !Log.HasLoggedErrors;
		}
	}

	internal static class TizenSplashOutputOwnership
	{
		internal static void EnsureOwnedOutputDirectory(string path)
		{
			if (Directory.Exists(path))
			{
				RejectReparsePoint(path, "splash output directory");
				return;
			}

			Directory.CreateDirectory(path);
			RejectReparsePoint(path, "splash output directory");
		}

		internal static void RejectReparsePoint(string path, string description)
		{
			FileAttributes attributes;
			try
			{
				// File.GetAttributes inspects the link itself, including when its target is absent.
				// File.Exists/Directory.Exists both return false for a dangling link and therefore
				// cannot distinguish it from a genuinely absent output path.
				attributes = File.GetAttributes(path);
			}
			catch (FileNotFoundException)
			{
				return;
			}
			catch (DirectoryNotFoundException)
			{
				return;
			}

			if ((attributes & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException(
					$"The Maui.Tizen {description} '{path}' is a symbolic link or reparse point. "
						+ "Refusing to follow it while replacing generated splash outputs.");
			}
		}

		internal static void DeletePreviouslyOwnedOutputs(
			string mapFile,
			string intermediatePath,
			string splashPath)
		{
			var splashPrefix = splashPath.TrimEnd(
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;

			foreach (var entry in GenerateTizenSplashScreens.ReadMap(mapFile))
			{
				var relative = entry.Source.Replace('/', Path.DirectorySeparatorChar);
				var candidate = Path.GetFullPath(Path.Combine(intermediatePath, relative));

				if (!candidate.StartsWith(splashPrefix, comparison))
					continue;

				if (File.Exists(candidate))
					File.Delete(candidate);
			}
		}
	}
}
