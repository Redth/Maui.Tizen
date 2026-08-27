using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Reads <em>evaluated</em> MSBuild items and properties out of the repository's project files.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Several build invariants in this repository were previously guarded by regexing
	/// <c>eng/Maui.Tizen.Core.Sources.props</c> for a file name. That is a proxy for the real
	/// question and it failed in practice: the props file carries a supersession comment block that
	/// names every file it documents, so deleting a genuine <c>&lt;Compile&gt;</c> item left the
	/// comment behind and the guard still passed.
	/// </para>
	/// <para>
	/// Asking MSBuild what it actually evaluated removes the whole class of problem. It cannot be
	/// satisfied by a comment, it follows imports and conditions, and it reflects item removals -
	/// none of which text matching can do.
	/// </para>
	/// <para>
	/// Evaluation costs a process launch, so results are cached for the lifetime of the test run.
	/// </para>
	/// </remarks>
	public static class MSBuildEvaluation
	{
		static readonly ConcurrentDictionary<string, string[]> ItemCache = new(StringComparer.Ordinal);
		static readonly ConcurrentDictionary<string, string> PropertyCache = new(StringComparer.Ordinal);

		public static string RepositoryRoot { get; } = FindRepositoryRoot();

		static string FindRepositoryRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
				dir = dir.Parent;

			return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
		}

		/// <summary>
		/// The .NET host running these tests, so evaluation uses the same SDK the suite was
		/// launched with rather than whichever <c>dotnet</c> happens to be on PATH.
		/// </summary>
		static string DotNetHost =>
			Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
				? host
				: "dotnet";

		static string Run(string projectRelativePath, string argument)
		{
			var psi = new ProcessStartInfo(DotNetHost)
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			psi.ArgumentList.Add("msbuild");
			psi.ArgumentList.Add(Path.Combine(RepositoryRoot, projectRelativePath));
			psi.ArgumentList.Add(argument);
			psi.ArgumentList.Add("-nologo");

			// The Tizen projects refuse to evaluate without the workload (MAUITIZEN0001). The gate
			// is the subject of its own tests; here it is just in the way.
			psi.ArgumentList.Add("-p:TizenWorkloadAvailable=true");

			using var process = Process.Start(psi)
				?? throw new InvalidOperationException($"Failed to start MSBuild for {projectRelativePath}.");

			var output = process.StandardOutput.ReadToEnd();
			var error = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode != 0)
				throw new InvalidOperationException(
					$"MSBuild evaluation failed for {projectRelativePath} {argument}:{Environment.NewLine}{output}{error}");

			return output;
		}

		/// <summary>Full paths of an evaluated item type, as MSBuild resolved them.</summary>
		public static string[] GetItems(string projectRelativePath, string itemType) =>
			ItemCache.GetOrAdd($"{projectRelativePath}|{itemType}", _ =>
			{
				var json = Run(projectRelativePath, $"-getItem:{itemType}");

				using var document = JsonDocument.Parse(json);

				if (!document.RootElement.TryGetProperty("Items", out var items) ||
					!items.TryGetProperty(itemType, out var entries))
				{
					return Array.Empty<string>();
				}

				return entries
					.EnumerateArray()
					.Select(e => e.TryGetProperty("FullPath", out var full)
						? full.GetString()
						: e.GetProperty("Identity").GetString())
					.Where(p => !string.IsNullOrEmpty(p))
					.Select(p => Path.GetFullPath(p!))
					.ToArray();
			});

		public static string GetProperty(string projectRelativePath, string property) =>
			PropertyCache.GetOrAdd($"{projectRelativePath}|{property}", _ =>
				Run(projectRelativePath, $"-getProperty:{property}").Trim());

		/// <summary>File names of an evaluated item type, for readable assertions.</summary>
		public static string[] GetItemFileNames(string projectRelativePath, string itemType) =>
			GetItems(projectRelativePath, itemType)
				.Select(Path.GetFileName)
				.Where(n => !string.IsNullOrEmpty(n))
				.Select(n => n!)
				.ToArray();

		/// <summary>Repository-relative, forward-slashed paths of an evaluated item type.</summary>
		public static string[] GetItemRelativePaths(string projectRelativePath, string itemType) =>
			GetItems(projectRelativePath, itemType)
				.Select(p => Path.GetRelativePath(RepositoryRoot, p).Replace('\\', '/'))
				.ToArray();
	}
}
