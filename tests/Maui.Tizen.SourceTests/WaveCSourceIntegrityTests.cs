using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards the properties that make Wave C a migration rather than a file copy.
/// </summary>
/// <remarks>
/// Wave C's defining constraint is that it must compile against the PUBLIC MAUI surface. These
/// tests check the things a compiler cannot: that no escape hatch (reflection, a stray internals
/// import, a partial class that would only work in-tree) was used to get there, and that the
/// adapters which replace internal APIs stay paired with the upstream requests that justify them.
/// </remarks>
public class WaveCSourceIntegrityTests
{
	const string AdaptersDirectory = "Adapters";
	const string ReservedNamespace = "Microsoft.Maui.Platforms.Tizen";

	[Fact]
	public void AllMigratedSourcesParseWithoutSyntaxErrors()
	{
		var failures = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var diagnostics = WaveBSource.ParseTree(file)
				.GetDiagnostics()
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.ToList();

			if (diagnostics.Count > 0)
			{
				failures.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {diagnostics[0].GetMessage()}");
			}
		}

		Assert.Empty(failures);
	}

	[Fact]
	public void MigratedSourcesDoNotImportControlsInternals()
	{
		// The in-tree backend compiled inside Microsoft.Maui.Controls and could reach its
		// internals. A leftover `using Microsoft.Maui.Controls.Internals` is the clearest sign a
		// file was copied rather than migrated.
		var offenders = WaveCSource.Files
			.Where(f => WaveBSource.ParseTree(f).GetRoot()
				.DescendantNodes()
				.OfType<UsingDirectiveSyntax>()
				.Any(u => u.Name?.ToString() == "Microsoft.Maui.Controls.Internals"))
			.Select(f => Path.GetRelativePath(RepoPaths.Root, f))
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedSourcesDoNotUsePrivateReflection()
	{
		// Reflection is the tempting way to keep calling an internal member after the migration.
		// It compiles, it even works, and it turns a compile-time contract into a runtime crash on
		// the next MAUI update. Where no public API exists, the answer is a Tizen-owned adapter
		// plus a recorded upstream request.
		var reflection = new Regex(
			@"\busing\s+System\.Reflection\b|\bBindingFlags\b|\.GetMethod\s*\(|\.GetProperty\s*\(|\.GetField\s*\(|\bInvokeMember\b|\bGetRuntimeMethod\b",
			RegexOptions.Compiled);

		var offenders = WaveCSource.Files
			.Where(f => reflection.IsMatch(File.ReadAllText(f)))
			.Select(f => Path.GetRelativePath(RepoPaths.Root, f))
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedTypesLiveInTheReservedTizenNamespace()
	{
		var offenders = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var namespaces = WaveBSource.ParseTree(file).GetRoot()
				.DescendantNodes()
				.OfType<BaseNamespaceDeclarationSyntax>()
				.Select(n => n.Name.ToString());

			foreach (var ns in namespaces)
			{
				if (!ns.StartsWith(ReservedNamespace, StringComparison.Ordinal))
				{
					offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {ns}");
				}
			}
		}

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedSourcesDeclareNoPartialClassesInMauiNamespaces()
	{
		// C# cannot complete a partial type across an assembly boundary. A partial class in a
		// MAUI-owned namespace is therefore either dead code or an unmigrated copy. Wave C's own
		// namespace is exempt: partial there is a normal implementation choice.
		var offenders = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var root = WaveBSource.ParseTree(file).GetRoot();

			foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
			{
				if (ns.Name.ToString().StartsWith(ReservedNamespace, StringComparison.Ordinal))
				{
					continue;
				}

				if (ns.DescendantNodes().OfType<ClassDeclarationSyntax>()
					.Any(c => c.Modifiers.Any(m => m.ValueText == "partial")))
				{
					offenders.Add(Path.GetRelativePath(RepoPaths.Root, file));
				}
			}
		}

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedTypeNamesDoNotCollideWithNeutralMauiTypes()
	{
		// Reusing a neutral name would collide with the handler the MAUI package already ships and
		// make it ambiguous which implementation an app resolved.
		var offenders = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			foreach (var type in WaveBSource.ParseTree(file).GetRoot()
				.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
			{
				var name = type.Identifier.Text;

				if (NeutralMaui.PublicTypeNames.Contains(name))
				{
					offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {name}");
				}
			}
		}

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedHandlersAreTizenPrefixed()
	{
		var offenders = WaveCSource.Handlers
			.Where(h => !h.TypeName.StartsWith("Tizen", StringComparison.Ordinal))
			.Select(h => h.TypeName)
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void MigratedFilesDropTheUpstreamTizenSuffix()
	{
		// A `.Tizen.cs` file is a file that still expects the upstream multi-targeting convention,
		// which does not exist here: every file in this repository is Tizen-only.
		var offenders = WaveCSource.Files
			.Where(f => Path.GetFileName(f).EndsWith(".Tizen.cs", StringComparison.Ordinal))
			.Select(f => Path.GetRelativePath(RepoPaths.Root, f))
			.ToList();

		Assert.Empty(offenders);
	}

	// ---------------------------------------------------------------------
	// Adapter bookkeeping
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryAdapterHasAnUpstreamApiRequest()
	{
		// The adapters and the upstream request list are two halves of one statement: "no public
		// API existed here, and this is what we are asking for". Letting them drift is how a
		// migration status report quietly becomes fiction.
		var adaptersRoot = RepoPaths.Combine(WaveCSource.Root.Append(AdaptersDirectory).ToArray());
		Assert.True(Directory.Exists(adaptersRoot), $"Missing Wave C adapters directory: {adaptersRoot}");

		var requests = File.ReadAllText(Path.Combine(adaptersRoot, "UpstreamApiRequests.cs"));

		var missing = Directory
			.EnumerateFiles(adaptersRoot, "*.cs")
			.Select(Path.GetFileNameWithoutExtension)
			.Where(name => name is not (null or "UpstreamApiRequests"))
			.Where(name => !requests.Contains($"nameof({name})", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(missing);
	}

	[Fact]
	public void UpstreamApiRequestIdsAreUniqueAndSequential()
	{
		var adaptersRoot = RepoPaths.Combine(WaveCSource.Root.Append(AdaptersDirectory).ToArray());
		var requests = File.ReadAllText(Path.Combine(adaptersRoot, "UpstreamApiRequests.cs"));

		var ids = Regex.Matches(requests, @"""(MAUI-TIZEN-API-\d{4})""")
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

		Assert.NotEmpty(ids);

		for (var i = 0; i < ids.Count; i++)
		{
			Assert.Equal($"MAUI-TIZEN-API-{i + 1:0000}", ids[i]);
		}
	}

	// ---------------------------------------------------------------------
	// Build configuration
	// ---------------------------------------------------------------------

	[Fact]
	public void ValidationLaneTargetsARealTizenFramework()
	{
		// The lane is only defensible because it targets a REAL Tizen TFM. If it ever degrades to
		// a neutral one it becomes exactly the false-green build Directory.Build.props forbids.
		var project = File.ReadAllText(
			RepoPaths.Combine("eng", "validation", "Maui.Tizen.Controls.Navigation.Validation.csproj"));

		var tfm = Regex.Match(project, @"<TargetFramework>([^<]+)</TargetFramework>");

		Assert.True(tfm.Success, "The validation lane must declare an explicit TargetFramework.");
		Assert.Matches(@"^net\d+\.\d+-tizen\d+\.\d+$", tfm.Groups[1].Value);
	}

	[Fact]
	public void ValidationLaneAndShippingProjectCompileTheSameSources()
	{
		// If the two lanes diverge, the lane stops proving anything about what ships.
		var shipping = File.ReadAllText(
			RepoPaths.Combine(WaveCSource.Root.Append("Maui.Tizen.Controls.Navigation.csproj").ToArray()));

		var validation = File.ReadAllText(
			RepoPaths.Combine("eng", "validation", "Maui.Tizen.Controls.Navigation.Validation.csproj"));

		Assert.Contains("Sources.props", shipping, StringComparison.Ordinal);
		Assert.Contains("Sources.props", validation, StringComparison.Ordinal);
		Assert.Contains("EnableTizenValidationLane", validation, StringComparison.Ordinal);
	}
}
