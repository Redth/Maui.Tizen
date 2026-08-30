using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Packs the repository's shippable projects once per test run and hands out the resulting
/// <c>.nupkg</c> files.
/// </summary>
/// <remarks>
/// <para>
/// Everything that asserts on package shape, and everything that consumes a package (installing
/// the template, restoring against the produced build tasks), works from these files rather than
/// from the source tree. A source-tree assertion cannot see the failures that matter here -
/// packing is exactly where a template's directory structure, a task assembly's location or a
/// native binary's folder can be lost.
/// </para>
/// <para>
/// The output directory is unique per run. It used to be a fixed <c>artifacts/packages/test</c>,
/// which meant a package left behind by an earlier run - a different version, a different branch,
/// or a build that has since been fixed - could be the one the assertions read, because the
/// lookup simply takes the last matching file name. A stale pass is worse than a failure.
/// </para>
/// </remarks>
internal static class ProducedPackages
{
	private static readonly Lazy<string> PrimaryDirectory = new(() => Pack("a"));

	/// <summary>The packable projects, relative to the repository root.</summary>
	internal static readonly string[] Projects =
	{
		Path.Combine("src", "Maui.Tizen.Build.Tasks", "Maui.Tizen.Build.Tasks.csproj"),
		Path.Combine("src", "Maui.Tizen.Templates", "Maui.Tizen.Templates.csproj"),
	};

	/// <summary>The directory holding this run's packages.</summary>
	internal static string Directory => PrimaryDirectory.Value;

	/// <summary>
	/// Packs a second, independent copy into its own directory. Used to compare two packs of the
	/// same sources without either of them being able to observe the other's output.
	/// </summary>
	internal static string PackAgain() => Pack("b-" + Guid.NewGuid().ToString("N"));

	/// <summary>Resolves the single package produced for <paramref name="packageId"/>.</summary>
	internal static string PathOf(string packageId, string? directory = null)
	{
		var root = directory ?? Directory;

		var matches = System.IO.Directory
			.GetFiles(root, packageId + ".*.nupkg")
			.Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToList();

		if (matches.Count == 0)
			throw new InvalidOperationException($"No package was produced for '{packageId}' in '{root}'.");

		// More than one would mean the isolation above has been lost, so say so rather than
		// silently picking one.
		if (matches.Count > 1)
			throw new InvalidOperationException(
				$"Expected exactly one '{packageId}' package in '{root}', found: {string.Join(", ", matches.Select(Path.GetFileName))}.");

		return matches[0];
	}

	private static string Pack(string suffix)
	{
		var root = Path.Combine(TestBase.RepositoryRoot, "artifacts", "packages", "test");

		PruneOldRuns(root);

		var output = Path.Combine(root, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}-{Guid.NewGuid():N}");

		System.IO.Directory.CreateDirectory(output);

		foreach (var project in Projects)
		{
			var startInfo = new ProcessStartInfo("dotnet")
			{
				WorkingDirectory = TestBase.RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};

			startInfo.ArgumentList.Add("pack");
			startInfo.ArgumentList.Add(Path.Combine(TestBase.RepositoryRoot, project));
			startInfo.ArgumentList.Add("-p:PackageOutputPath=" + output);
			startInfo.ArgumentList.Add("--nologo");
			startInfo.ArgumentList.Add("-v:q");

			foreach (var isolation in TestBase.ConfigureIsolatedMSBuild(startInfo))
				startInfo.ArgumentList.Add(isolation);

			using var process = Process.Start(startInfo)!;
			var log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode != 0)
				throw new InvalidOperationException($"dotnet pack failed for '{project}':{Environment.NewLine}{log}");
		}

		return output;
	}

	/// <summary>
	/// Best-effort removal of earlier runs' package directories.
	/// </summary>
	/// <remarks>
	/// Per-run isolation would otherwise leave one directory per test run under artifacts/. Only
	/// directories older than an hour are removed, so a run happening concurrently on the same
	/// machine cannot have its output deleted underneath it, and every failure is swallowed:
	/// housekeeping must never be the reason a test run fails.
	/// </remarks>
	private static void PruneOldRuns(string root)
	{
		try
		{
			if (!System.IO.Directory.Exists(root))
				return;

			var cutoff = DateTime.UtcNow.AddHours(-1);

			foreach (var directory in System.IO.Directory.GetDirectories(root))
			{
				try
				{
					if (System.IO.Directory.GetLastWriteTimeUtc(directory) < cutoff)
						System.IO.Directory.Delete(directory, recursive: true);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	/// <summary>
	/// The entries of a package, excluding the OPC bookkeeping that NuGet regenerates per pack.
	/// </summary>
	internal static IReadOnlyList<string> EntryNames(string packagePath)
	{
		using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);

		return archive.Entries
			.Select(e => e.FullName.Replace('\\', '/'))
			.Where(IsMeaningfulEntry)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// True for entries that describe what the package SHIPS, as opposed to OPC plumbing whose
	/// content legitimately differs between two packs of identical sources.
	/// </summary>
	internal static bool IsMeaningfulEntry(string entryName)
		=> !entryName.StartsWith("_rels/", StringComparison.Ordinal)
			&& !entryName.StartsWith("package/", StringComparison.Ordinal)
			&& entryName != "[Content_Types].xml";
}
