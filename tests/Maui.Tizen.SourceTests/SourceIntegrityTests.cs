using Microsoft.CodeAnalysis;

namespace Maui.Tizen.SourceTests;

public class SourceIntegrityTests
{
	[Fact]
	public void WaveBSourcesWereDiscovered()
	{
		Assert.NotEmpty(WaveBSource.Files);
		Assert.NotEmpty(WaveBSource.Handlers);
	}

	/// <summary>
	/// The backend cannot be compiled without Samsung's Tizen platform SDK, so parsing is the
	/// strongest syntactic guarantee available here. It still catches every malformed edit.
	/// </summary>
	[Fact]
	public void AllMigratedSourcesParseWithoutSyntaxErrors()
	{
		var failures = new List<string>();

		foreach (var file in WaveBSource.Files)
		{
			var errors = WaveBSource.ParseTree(file)
				.GetDiagnostics()
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.ToList();

			foreach (var error in errors)
			{
				failures.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {error}");
			}
		}

		Assert.Empty(failures);
	}

	/// <summary>
	/// The whole reason the migrated handlers are Tizen-prefixed: MAUI still ships the neutral
	/// handler names, and re-declaring them would be ambiguous for any consumer referencing both.
	/// </summary>
	[Fact]
	public void MigratedHandlerNamesDoNotCollideWithNeutralMauiTypes()
	{
		var collisions = WaveBSource.Handlers
			.Where(h => NeutralMaui.PublicTypeNames.Contains(h.TypeName))
			.Select(h => $"{h.TypeName} ({h.RelativePath})")
			.ToList();

		Assert.Empty(collisions);
	}

	[Fact]
	public void MigratedHandlersAreTizenPrefixed()
	{
		var offenders = WaveBSource.Handlers
			.Where(h => !h.TypeName.StartsWith("Tizen", StringComparison.Ordinal))
			.Select(h => $"{h.TypeName} ({h.RelativePath})")
			.ToList();

		Assert.Empty(offenders);
	}

	/// <summary>Private reflection into MAUI internals is banned; the backend must use public API only.</summary>
	[Fact]
	public void MigratedSourcesDoNotUsePrivateReflection()
	{
		string[] banned =
		{
			"BindingFlags.NonPublic",
			"GetRuntimeFields",
			"GetRuntimeMethods",
			"Type.GetType(",
			"Activator.CreateInstance(Type",
			"UnsafeAccessor",
		};

		var offenders = new List<string>();

		foreach (var file in WaveBSource.Files)
		{
			var text = File.ReadAllText(file);

			foreach (var token in banned.Where(text.Contains))
			{
				offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {token}");
			}
		}

		Assert.Empty(offenders);
	}

	/// <summary>
	/// Migrated files must not keep the upstream <c>.Tizen.cs</c> suffix: within this repository every
	/// file is Tizen-specific, and PROVENANCE.md assigns removing that redundancy to this workstream.
	/// </summary>
	[Fact]
	public void MigratedFilesDropTheUpstreamTizenSuffix()
	{
		var offenders = WaveBSource.Files
			.Where(p => p.EndsWith(".Tizen.cs", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(offenders);
	}
}
