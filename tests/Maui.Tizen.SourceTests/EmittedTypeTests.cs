using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards the compiled output of the ref-pack lane, which is where the Wave B sources are actually
/// type-checked against real TizenFX.
/// </summary>
/// <remarks>
/// <para>
/// These assert on EMITTED metadata rather than on source text. A source-level check can be
/// satisfied by a file that is simply never compiled; only the produced metadata proves that no
/// colliding type reaches consumers.
/// </para>
/// <para>
/// Metadata is read with <see cref="MetadataReader"/> rather than <c>Assembly.Load</c> on purpose:
/// loading would require resolving Tizen.NUI, which cannot be loaded on a host TFM. Reading
/// metadata needs no references at all.
/// </para>
/// </remarks>
public class EmittedTypeTests
{
	const string RefPackAssemblyName = "Maui.Tizen.Core.RefPackCompile";

	static (IReadOnlyList<string> Defined, IReadOnlyList<string> Referenced) Metadata { get; } = ReadMetadata();

	static string FindRefPackAssembly()
	{
		var candidates = new[] { "Release", "Debug" }
			.Select(c => RepoPaths.Combine("artifacts", "bin", RefPackAssemblyName, c, "net11.0", RefPackAssemblyName + ".dll"))
			.Where(File.Exists)
			.ToList();

		Assert.True(
			candidates.Count > 0,
			$"{RefPackAssemblyName} has not been built. Run: dotnet build tests/{RefPackAssemblyName}");

		return candidates[0];
	}

	static (IReadOnlyList<string>, IReadOnlyList<string>) ReadMetadata()
	{
		using var stream = File.OpenRead(FindRefPackAssembly());
		using var pe = new PEReader(stream);

		var reader = pe.GetMetadataReader();

		var defined = new List<string>();
		foreach (var handle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(handle);
			var ns = reader.GetString(type.Namespace);
			var name = reader.GetString(type.Name);
			defined.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
		}

		var referenced = new List<string>();
		foreach (var handle in reader.TypeReferences)
		{
			var type = reader.GetTypeReference(handle);
			var ns = reader.GetString(type.Namespace);
			var name = reader.GetString(type.Name);
			referenced.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
		}

		return (defined, referenced);
	}

	/// <summary>
	/// <c>Microsoft.Maui.Platform.WrapperView</c> also exists in the neutral Microsoft.Maui.Core
	/// assembly. Emitting it here would give two loaded assemblies the same full type name, which is
	/// a CS0433 ambiguity for anyone referencing both. The wrapper compiles as
	/// <c>Microsoft.Maui.Platforms.Tizen.TizenWrapperView</c> instead.
	/// </summary>
	[Fact]
	public void DoesNotEmitCollidingWrapperView()
	{
		Assert.DoesNotContain("Microsoft.Maui.Platform.WrapperView", Metadata.Defined);
		Assert.Contains("Microsoft.Maui.Platforms.Tizen.TizenWrapperView", Metadata.Defined);
	}

	/// <summary>
	/// <c>IPlatformViewHandler</c> only exists inside MAUI's own Tizen build. This backend must
	/// neither declare it nor bind to it; the core slice supplies <c>ITizenPlatformViewHandler</c>.
	/// </summary>
	[Fact]
	public void NeitherDeclaresNorReferencesPlatformViewHandler()
	{
		string[] banned =
		{
			"Microsoft.Maui.IPlatformViewHandler",
			"Microsoft.Maui.Handlers.IPlatformViewHandler",
		};

		foreach (var name in banned)
		{
			Assert.DoesNotContain(name, Metadata.Defined);
			Assert.DoesNotContain(name, Metadata.Referenced);
		}
		Assert.Contains("Microsoft.Maui.Platforms.Tizen.ITizenPlatformViewHandler", Metadata.Referenced.Concat(Metadata.Defined));
	}

	/// <summary>
	/// No emitted type may sit in <c>Microsoft.Maui.Platform</c>: that namespace belongs to the
	/// neutral assembly. Everything compiled here belongs under <c>Microsoft.Maui.Platforms.Tizen</c>.
	/// </summary>
	[Fact]
	public void EmitsNothingIntoTheNeutralPlatformNamespace()
	{
		var offenders = Metadata.Defined
			.Where(n => n.StartsWith("Microsoft.Maui.Platform.", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(offenders);
	}

	/// <summary>
	/// The Wave B handlers must actually be in the compiled output. Without this the checks above
	/// would pass trivially if the sources were dropped from the compile lane.
	/// </summary>
	[Fact]
	public void EmitsTheWaveBHandlers()
	{
		string[] expected =
		{
			"TizenScrollViewHandler", "TizenBorderHandler", "TizenImageHandler", "TizenImageButtonHandler",
			"TizenGraphicsViewHandler", "TizenShapeViewHandler", "TizenRefreshViewHandler",
			"TizenSwipeViewHandler", "TizenSwipeItemViewHandler", "TizenSwipeItemMenuItemHandler",
			"TizenIndicatorViewHandler", "TizenBoxViewHandler", "TizenLineHandler", "TizenPathHandler",
			"TizenPolygonHandler", "TizenPolylineHandler", "TizenRectangleHandler", "TizenRoundRectangleHandler",
		};

		var emitted = Metadata.Defined
			.Select(n => n.Contains('.', StringComparison.Ordinal) ? n[(n.LastIndexOf('.') + 1)..] : n)
			.ToHashSet(StringComparer.Ordinal);

		foreach (var name in expected)
		{
			Assert.Contains(name, emitted);
		}
	}

	/// <summary>
	/// The migrated Wave B platform views must be emitted under Tizen-owned names.
	/// </summary>
	[Fact]
	public void EmitsTheWaveBPlatformViewsUnderTizenNames()
	{
		string[] expected =
		{
			"Microsoft.Maui.Platforms.Tizen.TizenWrapperView",
			"Microsoft.Maui.Platforms.Tizen.TizenScrollViewGroup",
			"Microsoft.Maui.Platforms.Tizen.TizenShapeView",
			"Microsoft.Maui.Platforms.Tizen.TizenSwipeViewGroup",
			"Microsoft.Maui.Platforms.Tizen.TizenPageControl",
			"Microsoft.Maui.Platforms.Tizen.TizenRefreshLayout",
			"Microsoft.Maui.Platforms.Tizen.TizenImageButtonView",
			"Microsoft.Maui.Platforms.Tizen.TizenTouchGraphicsView",
		};

		foreach (var name in expected)
		{
			Assert.Contains(name, Metadata.Defined);
		}
	}
}
