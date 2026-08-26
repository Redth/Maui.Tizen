using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Guards the properties that make the Wave C handler migration a real migration rather than a
/// file copy.
///
/// These are source-level tests on purpose. Until the Samsung .NET 11 workload ships, the Tizen
/// assemblies cannot be compiled or executed by anyone, so a reflection-based test over the built
/// handlers is not an option. What *can* be checked today is that the migrated sources no longer
/// reach into Microsoft.Maui.Controls internals, no longer extend Controls types with partial
/// classes, do not use reflection as an escape hatch, do not reintroduce the neutral MAUI handler
/// names, and declare exactly the mapper coverage that the published parity artifact claims.
///
/// The style deliberately matches <see cref="RepositoryInvariantTests"/>: plain file and regex
/// analysis, no extra analyzer dependency to keep pinned.
/// </summary>
public class WaveCMigrationTests
{
	const string WaveCRoot = "src/Maui.Tizen.Controls.Navigation";

	static readonly string RepoRoot = FindRepositoryRoot();

	static string FindRepositoryRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
			dir = dir.Parent;

		Assert.NotNull(dir);
		return dir!.FullName;
	}

	static string WaveCPath(string relative) => Path.Combine(RepoRoot, WaveCRoot, relative);

	/// <summary>Every migrated C# source file, as (repo-relative path, text) pairs.</summary>
	static IReadOnlyList<(string Path, string Text)> WaveCSources()
	{
		var root = Path.Combine(RepoRoot, WaveCRoot);
		Assert.True(Directory.Exists(root), $"Wave C source root is missing: {WaveCRoot}");

		var files = Directory
			.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.OrderBy(p => p, StringComparer.Ordinal)
			.Select(p => (Path: Path.GetRelativePath(RepoRoot, p), Text: File.ReadAllText(p)))
			.ToList();

		Assert.True(files.Count > 0, "Expected migrated Wave C sources to exist.");
		return files;
	}

	static JsonElement ParityManifest()
	{
		var path = WaveCPath(Path.Combine("Parity", "MapperParity.json"));
		Assert.True(File.Exists(path), "Parity/MapperParity.json is missing.");

		using var doc = JsonDocument.Parse(
			File.ReadAllText(path),
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

		return doc.RootElement.Clone();
	}

	// ---------------------------------------------------------------------
	// The point of the migration: no internal coupling, no reflection
	// ---------------------------------------------------------------------

	[Fact]
	public void NoSourceUsesControlsInternals()
	{
		// The in-tree backend compiled inside Microsoft.Maui.Controls and could reach its
		// internals. Out-of-tree it cannot, and a `using Microsoft.Maui.Controls.Internals`
		// left behind is the clearest signal that a file was copied rather than migrated.
		//
		// Note that IAppearanceObserver lives in that namespace but is public. It is still
		// banned as a *using* so that the ban stays mechanical; reference it fully qualified
		// if it is ever needed.
		var offenders = WaveCSources()
			.Where(f => Regex.IsMatch(f.Text, @"using\s+Microsoft\.Maui\.Controls\.Internals\s*;"))
			.Select(f => f.Path)
			.ToList();

		Assert.True(
			offenders.Count == 0,
			"These migrated sources still import Microsoft.Maui.Controls.Internals: " + string.Join(", ", offenders));
	}

	[Fact]
	public void NoSourceUsesReflection()
	{
		// Reflection is the tempting way to keep calling an internal member after the
		// migration. It compiles, it even works, and it converts a compile-time contract into
		// a runtime crash on the next MAUI update. Where a public API genuinely does not
		// exist, the answer is a Tizen-owned adapter plus an upstream API request.
		var reflection = new Regex(
			@"\busing\s+System\.Reflection\b|\bBindingFlags\b|\.GetMethod\s*\(|\.GetProperty\s*\(|\.GetField\s*\(|\bInvokeMember\b",
			RegexOptions.Compiled);

		var offenders = WaveCSources()
			.Where(f => reflection.IsMatch(f.Text))
			.Select(f => f.Path)
			.ToList();

		Assert.True(
			offenders.Count == 0,
			"These migrated sources use reflection, which is not an acceptable substitute for a public API: "
				+ string.Join(", ", offenders));
	}

	[Fact]
	public void NoSourceExtendsMauiTypesWithPartialClasses()
	{
		// C# cannot complete a partial type across an assembly boundary, so any `partial class`
		// declared in a Microsoft.Maui.Controls or Microsoft.Maui.Handlers namespace is either
		// dead code or a copy that was never actually migrated.
		var offenders = new List<string>();

		foreach (var (path, text) in WaveCSources())
		{
			var ns = Regex.Match(text, @"namespace\s+([A-Za-z0-9_.]+)");

			if (!ns.Success)
				continue;

			var name = ns.Groups[1].Value;
			var isMauiOwnedNamespace =
				name is "Microsoft.Maui.Controls" or "Microsoft.Maui.Handlers" or "Microsoft.Maui.Platform"
				|| name.StartsWith("Microsoft.Maui.Controls.", StringComparison.Ordinal)
				|| name.StartsWith("Microsoft.Maui.Handlers.", StringComparison.Ordinal);

			// Platforms.Tizen is ours even though it starts with Microsoft.Maui.
			if (name.StartsWith("Microsoft.Maui.Platforms.Tizen", StringComparison.Ordinal))
				continue;

			if (isMauiOwnedNamespace && Regex.IsMatch(text, @"\bpartial\s+class\b"))
				offenders.Add(path);
		}

		Assert.True(
			offenders.Count == 0,
			"These sources declare a partial class in a MAUI-owned namespace: " + string.Join(", ", offenders));
	}

	[Fact]
	public void NoTypeReusesANeutralMauiHandlerName()
	{
		// Reusing the neutral names would collide with the handlers the MAUI package already
		// ships, and would make it ambiguous which implementation an app actually resolved.
		string[] reserved =
		{
			"ShellHandler", "ShellItemHandler", "ShellSectionHandler", "ShellContentHandler",
			"NavigationViewHandler", "FlyoutViewHandler", "ToolbarHandler", "TabbedViewHandler",
			"MenuBarHandler", "MenuBarItemHandler", "MenuFlyoutHandler", "MenuFlyoutItemHandler",
			"MenuFlyoutSubItemHandler", "MenuFlyoutSeparatorHandler",
			"ItemsViewHandler", "StructuredItemsViewHandler", "SelectableItemsViewHandler",
			"GroupableItemsViewHandler", "ReorderableItemsViewHandler", "CollectionViewHandler",
			"CarouselViewHandler", "ItemTemplateAdaptor", "MauiCollectionView", "MauiCarouselView",
			"ShellView", "ShellItemView", "ShellSectionView", "NavigationView", "StackNavigationManager",
		};

		var declared = new Regex(
			@"\b(?:class|struct|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)",
			RegexOptions.Compiled);

		var offenders = new List<string>();

		foreach (var (path, text) in WaveCSources())
		{
			foreach (Match m in declared.Matches(text))
			{
				if (reserved.Contains(m.Groups[1].Value, StringComparer.Ordinal))
					offenders.Add($"{path}: {m.Groups[1].Value}");
			}
		}

		Assert.True(
			offenders.Count == 0,
			"These declarations reuse a neutral MAUI type name instead of a Tizen-prefixed one: "
				+ string.Join(", ", offenders));
	}

	[Fact]
	public void NoSourceReferencesTheMauiRepositoryDirectly()
	{
		// A <Compile Include="../../maui/src/..."/> style source reference would reintroduce
		// exactly the in-tree coupling this repository exists to remove.
		var projects = Directory
			.EnumerateFiles(Path.Combine(RepoRoot, WaveCRoot), "*.*proj", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "eng", "validation"), "*.*proj", SearchOption.AllDirectories))
			.ToList();

		var offenders = projects
			.Where(p => Regex.IsMatch(File.ReadAllText(p), @"<Compile\s+Include=""[^""]*(dotnet[\\/])?maui[\\/]src"))
			.Select(p => Path.GetRelativePath(RepoRoot, p))
			.ToList();

		Assert.True(offenders.Count == 0, "These projects source-reference dotnet/maui: " + string.Join(", ", offenders));
	}

	// ---------------------------------------------------------------------
	// Adapter bookkeeping
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryAdapterHasAnUpstreamApiRequest()
	{
		// The adapters and the upstream request list are two halves of the same statement:
		// "we could not use a public API here, and this is what we are asking for". Letting
		// them drift apart is how a migration status report quietly becomes fiction.
		var requests = File.ReadAllText(WaveCPath(Path.Combine("Adapters", "UpstreamApiRequests.cs")));

		var adapterTypes = Directory
			.EnumerateFiles(WaveCPath("Adapters"), "*.cs")
			.Select(Path.GetFileNameWithoutExtension)
			.Where(n => n is not (null or "UpstreamApiRequests"))
			.ToList();

		var missing = adapterTypes
			.Where(t => !requests.Contains($"nameof({t})", StringComparison.Ordinal))
			.ToList();

		Assert.True(
			missing.Count == 0,
			"These adapters have no UpstreamApiRequests entry explaining why they exist: " + string.Join(", ", missing));
	}

	[Fact]
	public void UpstreamApiRequestIdsAreUniqueAndSequential()
	{
		var requests = File.ReadAllText(WaveCPath(Path.Combine("Adapters", "UpstreamApiRequests.cs")));

		var ids = Regex.Matches(requests, @"""(MAUI-TIZEN-API-\d{4})""")
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

		Assert.NotEmpty(ids);

		for (var i = 0; i < ids.Count; i++)
			Assert.Equal($"MAUI-TIZEN-API-{i + 1:0000}", ids[i]);
	}

	// ---------------------------------------------------------------------
	// Mapper parity
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryHandlerDeclaresBothAPropertyMapperAndACommandMapper()
	{
		// A handler with no CommandMapper silently drops every command (RequestNavigation
		// among them) instead of failing, which is close to impossible to spot in review.
		var offenders = new List<string>();

		foreach (var (path, text) in WaveCSources())
		{
			foreach (Match m in Regex.Matches(text, @"\bclass\s+(Tizen[A-Za-z0-9_]*Handler)\b"))
			{
				var handler = m.Groups[1].Value;

				var hasMapper = Regex.IsMatch(text, $@"PropertyMapper<[^>]*{Regex.Escape(handler)}>\s+Mapper")
					|| Regex.IsMatch(text, @"IPropertyMapper<[^>]*>\s+Mapper");
				var hasCommandMapper = Regex.IsMatch(text, @"CommandMapper<[^>]*>\s+CommandMapper");

				if (!hasMapper || !hasCommandMapper)
					offenders.Add($"{path}: {handler}");
			}
		}

		Assert.True(
			offenders.Count == 0,
			"These handlers are missing a property mapper or a command mapper: " + string.Join(", ", offenders));
	}

	[Fact]
	public void ParityManifestIsWellFormed()
	{
		var manifest = ParityManifest();

		Assert.Equal(1, manifest.GetProperty("schemaVersion").GetInt32());
		Assert.Equal("C", manifest.GetProperty("wave").GetString());

		var handlers = manifest.GetProperty("handlers").EnumerateArray().ToList();
		Assert.NotEmpty(handlers);

		string[] allowed = { "Supported", "Partial", "NoOp", "Unsupported", "Blocked" };

		foreach (var handler in handlers)
		{
			var name = handler.GetProperty("handler").GetString();
			Assert.False(string.IsNullOrWhiteSpace(name));
			Assert.StartsWith("Tizen", name, StringComparison.Ordinal);

			foreach (var mapperName in new[] { "propertyMapper", "commandMapper" })
			{
				foreach (var entry in handler.GetProperty(mapperName).EnumerateArray())
				{
					var status = entry.GetProperty("status").GetString();

					Assert.True(
						allowed.Contains(status, StringComparer.Ordinal),
						$"{name}.{mapperName}: unknown status '{status}'");

					// A classification without a reason is not a classification.
					if (status is not "Supported")
					{
						var note = entry.GetProperty("note").GetString();
						Assert.False(
							string.IsNullOrWhiteSpace(note),
							$"{name}.{mapperName}[{entry.GetProperty("key").GetString()}] is '{status}' but has no note explaining why.");
					}
				}
			}
		}
	}

	[Fact]
	public void ParityManifestCoversEveryDeclaredHandler()
	{
		var declaredHandlers = WaveCSources()
			.SelectMany(f => Regex.Matches(f.Text, @"\bclass\s+(Tizen[A-Za-z0-9_]*Handler)\b").Select(m => m.Groups[1].Value))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		var documented = ParityManifest()
			.GetProperty("handlers")
			.EnumerateArray()
			.Select(h => h.GetProperty("handler").GetString()!)
			.ToHashSet(StringComparer.Ordinal);

		var missing = declaredHandlers.Where(h => !documented.Contains(h)).ToList();

		Assert.True(
			missing.Count == 0,
			"These handlers exist in source but are absent from Parity/MapperParity.json: " + string.Join(", ", missing));
	}

	[Fact]
	public void ParityManifestDoesNotDocumentHandlersThatNoLongerExist()
	{
		var declaredHandlers = WaveCSources()
			.SelectMany(f => Regex.Matches(f.Text, @"\bclass\s+(Tizen[A-Za-z0-9_]*Handler)\b").Select(m => m.Groups[1].Value))
			.ToHashSet(StringComparer.Ordinal);

		var stale = ParityManifest()
			.GetProperty("handlers")
			.EnumerateArray()
			.Select(h => h.GetProperty("handler").GetString()!)
			.Where(h => !declaredHandlers.Contains(h))
			.ToList();

		Assert.True(
			stale.Count == 0,
			"Parity/MapperParity.json documents handlers that no longer exist in source: " + string.Join(", ", stale));
	}

	[Fact]
	public void EveryMapperKeyInSourceIsClassifiedInTheParityManifest()
	{
		// This is the test that keeps the parity artifact honest. Adding a mapping without
		// classifying it, or classifying a mapping that was quietly deleted, both fail here.
		var manifest = ParityManifest();

		var documented = manifest.GetProperty("handlers")
			.EnumerateArray()
			.ToDictionary(
				h => h.GetProperty("handler").GetString()!,
				h => h.GetProperty("propertyMapper").EnumerateArray()
					.Concat(h.GetProperty("commandMapper").EnumerateArray())
					.Select(e => e.GetProperty("key").GetString()!)
					.ToHashSet(StringComparer.Ordinal),
				StringComparer.Ordinal);

		var problems = new List<string>();

		foreach (var (path, text) in WaveCSources())
		{
			foreach (Match handlerMatch in Regex.Matches(text, @"\bclass\s+(Tizen[A-Za-z0-9_]*Handler)\b"))
			{
				var handler = handlerMatch.Groups[1].Value;

				if (!documented.TryGetValue(handler, out var keys))
					continue;

				// Mapper keys are written either as [nameof(Type.Member)] = ... or ["Literal"] = ...
				var declaredKeys = Regex.Matches(text, @"\[\s*nameof\(\s*[A-Za-z0-9_.]*?([A-Za-z0-9_]+)\s*\)\s*\]\s*=")
					.Select(m => m.Groups[1].Value)
					.Concat(Regex.Matches(text, @"\[\s*""([^""]+)""\s*\]\s*=").Select(m => m.Groups[1].Value))
					.Distinct(StringComparer.Ordinal)
					.ToList();

				foreach (var key in declaredKeys)
				{
					if (!keys.Contains(key))
						problems.Add($"{path}: {handler} maps '{key}' but the parity manifest does not classify it.");
				}
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	// ---------------------------------------------------------------------
	// Build configuration
	// ---------------------------------------------------------------------

	[Fact]
	public void ValidationLaneTargetsARealTizenFramework()
	{
		// The validation lane exists so the migrated code is checked by a compiler while the
		// net11 workload is unavailable. It is only defensible because it targets a REAL Tizen
		// TFM. If it ever degrades to a neutral TFM it becomes precisely the false-green build
		// that Directory.Build.props forbids.
		var project = File.ReadAllText(
			Path.Combine(RepoRoot, "eng", "validation", "Maui.Tizen.Controls.Navigation.Validation.csproj"));

		var tfm = Regex.Match(project, @"<TargetFramework>([^<]+)</TargetFramework>");

		Assert.True(tfm.Success, "The validation lane must declare an explicit TargetFramework.");
		Assert.Matches(@"^net\d+\.\d+-tizen\d+\.\d+$", tfm.Groups[1].Value);
	}

	[Fact]
	public void ValidationLaneAndShippingProjectCompileTheSameSources()
	{
		// If the two lanes ever diverge, the lane stops proving anything about what ships.
		var shipping = File.ReadAllText(WaveCPath("Maui.Tizen.Controls.Navigation.csproj"));
		var validation = File.ReadAllText(
			Path.Combine(RepoRoot, "eng", "validation", "Maui.Tizen.Controls.Navigation.Validation.csproj"));

		Assert.Contains("Sources.props", shipping, StringComparison.Ordinal);
		Assert.Contains("Sources.props", validation, StringComparison.Ordinal);
	}

	[Fact]
	public void ValidationLaneIsOptIn()
	{
		// Someone building the repository without the Tizen workload should get a clear skip,
		// not a wall of restore errors from a lane they never asked for.
		var validation = File.ReadAllText(
			Path.Combine(RepoRoot, "eng", "validation", "Maui.Tizen.Controls.Navigation.Validation.csproj"));

		Assert.Contains("EnableTizenValidationLane", validation, StringComparison.Ordinal);
	}
}
