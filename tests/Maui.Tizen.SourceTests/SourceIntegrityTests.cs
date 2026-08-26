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

	/// <summary>
	/// docs/architecture.md reserves <c>Microsoft.Maui.Platforms.Tizen</c> for types written here
	/// rather than inherited, precisely because that namespace is unused throughout dotnet/maui and
	/// therefore cannot collide now or later. Every rebuilt Wave B type must live in it.
	/// </summary>
	[Fact]
	public void MigratedTypesLiveInTheReservedTizenNamespace()
	{
		var offenders = WaveBSource.Handlers
			.Where(h => !h.Namespace.StartsWith("Microsoft.Maui.Platforms.Tizen", StringComparison.Ordinal))
			.Select(h => $"{h.TypeName} is in '{h.Namespace}' ({h.RelativePath})")
			.ToList();

		Assert.Empty(offenders);
	}

	/// <summary>
	/// Wave B handlers must build on the core vertical slice's base rather than deriving straight
	/// from MAUI's generic handler, so focus handling, measurement, arrangement and disposal stay in
	/// one place.
	/// </summary>
	[Fact]
	public void ViewHandlersDeriveFromTheBackendBase()
	{
		var known = WaveBSource.Handlers.Select(h => h.TypeName).ToHashSet(StringComparer.Ordinal);

		var offenders = WaveBSource.Handlers
			.Where(h => h.TypeName.EndsWith("Handler", StringComparison.Ordinal))
			.Where(h => !h.BaseType.StartsWith("TizenViewHandler", StringComparison.Ordinal))
			.Where(h => !h.BaseType.StartsWith("ElementHandler", StringComparison.Ordinal))
			.Where(h => !known.Contains(h.BaseType))
			.Select(h => $"{h.TypeName} derives from '{h.BaseType}' ({h.RelativePath})")
			.ToList();

		Assert.Empty(offenders);
	}

	/// <summary>
	/// Container policy belongs to TizenViewHandler, which pins NeedsContainer to false because MAUI
	/// exposes no settable container hook to an out-of-repo backend. Individual handlers must not
	/// re-litigate it.
	/// </summary>
	[Fact]
	public void HandlersDoNotOverrideContainerPolicy()
	{
		var offenders = new List<string>();

		foreach (var file in WaveBSource.Files)
		{
			var text = File.ReadAllText(file);

			foreach (var member in new[] { "NeedsContainer", "SetupContainer", "RemoveContainer" })
			{
				if (text.Contains($"override bool {member}", StringComparison.Ordinal) ||
					text.Contains($"override void {member}(", StringComparison.Ordinal))
				{
					offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)} overrides {member}.");
				}
			}
		}

		Assert.Empty(offenders);
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
