using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
		/// <summary>
		/// One cached evaluation per project, covering every item type and property the suite asks
		/// for.
		/// </summary>
		/// <remarks>
		/// Originally this cached per (project, item), which meant a separate `dotnet msbuild`
		/// process for each question. These projects import the whole repository's build, so a
		/// single evaluation costs tens of seconds, and the suite went from 3 seconds to over five
		/// minutes - fast enough to pass, slow enough to be a CI timeout risk and a genuine
		/// annoyance locally.
		///
		/// MSBuild accepts several -getItem/-getProperty arguments in one invocation and returns
		/// them together, so everything is fetched at once and sliced up here.
		/// </remarks>
		static readonly ConcurrentDictionary<string, Evaluation> Cache = new(StringComparer.Ordinal);

		static readonly string[] WantedItems = { "Compile", "AdditionalFiles", "ProjectReference", "PackageReference", "None" };
		static readonly string[] WantedProperties =
		{
			"TargetFramework",
			"IsTizenProject",
			"AssemblyName",
			"DefineConstants",
			"TizenManifestFile",
			"UseMaui",
			"GenerateDocumentationFile",
			"TizenUIExtensionsPackageVersion",
			"TizenUIExtensionsIsShippable",
			"TizenReferencePackId",
			"TizenReferencePackVersion",
		};

		sealed record Evaluation(
			IReadOnlyDictionary<string, string[]> Items,
			IReadOnlyDictionary<string, string> Properties);

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

		static Evaluation Evaluate(string projectRelativePath) =>
			Cache.GetOrAdd(projectRelativePath, path =>
			{
				var args = WantedItems.Select(i => $"-getItem:{i}")
					.Concat(WantedProperties.Select(p => $"-getProperty:{p}"))
					.ToArray();

				var json = Run(path, args);

				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;

				var items = new Dictionary<string, string[]>(StringComparer.Ordinal);

				if (root.TryGetProperty("Items", out var itemsElement))
				{
					foreach (var wanted in WantedItems)
					{
						items[wanted] = itemsElement.TryGetProperty(wanted, out var entries)
							? entries.EnumerateArray()
								.Select(e => e.TryGetProperty("FullPath", out var full)
									? full.GetString()
									: e.GetProperty("Identity").GetString())
								.Where(v => !string.IsNullOrEmpty(v))
								.Select(v => Path.GetFullPath(v!))
								.ToArray()
							: Array.Empty<string>();
					}
				}

				var properties = new Dictionary<string, string>(StringComparer.Ordinal);

				if (root.TryGetProperty("Properties", out var propertiesElement))
				{
					foreach (var wanted in WantedProperties)
					{
						properties[wanted] = propertiesElement.TryGetProperty(wanted, out var value)
							? value.GetString() ?? string.Empty
							: string.Empty;
					}
				}

				return new Evaluation(items, properties);
			});

		static string Run(string projectRelativePath, params string[] arguments)
		{
			var psi = new ProcessStartInfo(DotNetHost)
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			psi.ArgumentList.Add("msbuild");
			psi.ArgumentList.Add(Path.Combine(RepositoryRoot, projectRelativePath));
			foreach (var argument in arguments)
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
					$"MSBuild evaluation failed for {projectRelativePath}:{Environment.NewLine}{output}{error}");

			return output;
		}

		/// <summary>Full paths of an evaluated item type, as MSBuild resolved them.</summary>
		public static string[] GetItems(string projectRelativePath, string itemType) =>
			Evaluate(projectRelativePath).Items.TryGetValue(itemType, out var items)
				? items
				: throw new ArgumentException(
					$"'{itemType}' is not fetched. Add it to {nameof(WantedItems)}.", nameof(itemType));

		public static string GetProperty(string projectRelativePath, string property) =>
			Evaluate(projectRelativePath).Properties.TryGetValue(property, out var value)
				? value
				: throw new ArgumentException(
					$"'{property}' is not fetched. Add it to {nameof(WantedProperties)}.", nameof(property));

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
