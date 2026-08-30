using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Rejects two compiled sources declaring the same extension method signature in one namespace.
/// </summary>
/// <remarks>
/// <para>
/// Core and Wave B both live in <c>Microsoft.Maui.Platforms.Tizen</c>. Two extension methods with
/// the same name and parameter types in the same namespace are not an override — they are an
/// ambiguity, and every call site fails to compile with CS0121.
/// </para>
/// <para>
/// This is not hypothetical. Core's <c>TizenPlatformExtensions</c> and Wave B's interop both define
/// <c>UpdateVisibility(View, IView)</c>, <c>UpdateFlowDirection(View, IView)</c> and
/// <c>ToPlatformVisibility(Visibility)</c>. Wave B's base predates Core's, so today only one
/// definition is in the tree and everything compiles. **The moment Wave B rebases onto current
/// Core, all three collide.**
/// </para>
/// <para>
/// So this test passes now and is designed to fail loudly at exactly the moment the duplicates
/// appear, rather than leaving the integration to discover it as a wall of CS0121. Core owns the
/// common <c>IView</c> extensions; the fix at rebase is to DELETE Wave B's three and route callers
/// through Core, not to rename them and keep a parallel implementation.
/// </para>
/// </remarks>
public class DuplicateExtensionTests
{
	sealed record ExtensionMethod(string Namespace, string Name, string Signature, string File);

	/// <summary>Every source compiled into the product, read from the shared source manifest.</summary>
	/// <remarks>
	/// Read from <c>eng/Maui.Tizen.Core.Sources.props</c> rather than by globbing: the foundation's
	/// raw unmodified import shares these directories and is deliberately not compiled, so a glob
	/// would report collisions that cannot actually happen.
	/// </remarks>
	static IReadOnlyList<string> CompiledSources()
	{
		var manifest = RepoPaths.Combine("eng", "Maui.Tizen.Core.Sources.props");
		Assert.True(File.Exists(manifest), "The shared source manifest is missing.");

		var document = XDocument.Load(manifest);

		var directories = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["$(MauiTizenCoreDir)"] = RepoPaths.Combine("src", "Maui.Tizen.Core") + Path.DirectorySeparatorChar,
			["$(MauiTizenControlsDir)"] = RepoPaths.Combine("src", "Maui.Tizen.Controls") + Path.DirectorySeparatorChar,
			["$(MauiTizenSampleDir)"] = RepoPaths.Combine("samples", "Maui.Tizen.Sample") + Path.DirectorySeparatorChar,
		};

		var files = new List<string>();

		foreach (var include in document.Descendants()
			.Where(e => e.Name.LocalName.StartsWith("MauiTizen", StringComparison.Ordinal))
			.Select(e => e.Attribute("Include")?.Value)
			.Where(v => !string.IsNullOrWhiteSpace(v))
			.Select(v => v!))
		{
			var resolved = include;

			foreach (var (token, path) in directories)
				resolved = resolved.Replace(token, path, StringComparison.Ordinal);

			// Item-group references such as @(MauiTizenWaveBPortableCompile) are expanded elsewhere.
			if (resolved.StartsWith("@(", StringComparison.Ordinal))
				continue;

			resolved = Path.GetFullPath(resolved.Replace('/', Path.DirectorySeparatorChar));

			if (File.Exists(resolved))
				files.Add(resolved);
		}

		Assert.NotEmpty(files);
		return files;
	}

	static IReadOnlyList<ExtensionMethod> ExtensionMethods()
	{
		var results = new List<ExtensionMethod>();
		var sources = CompiledSources();

		// Global aliases (TizenNativeAliases.cs) apply to every file, so collect them first.
		var globalAliases = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var file in sources)
			CollectAliases(File.ReadAllText(file), globalAliases, globalOnly: true);

		foreach (var file in sources)
		{
			var text = File.ReadAllText(file);

			var root = CSharpSyntaxTree
				.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest))
				.GetRoot();

			// File-scoped aliases win over global ones.
			var aliases = new Dictionary<string, string>(globalAliases, StringComparer.Ordinal);
			CollectAliases(text, aliases, globalOnly: false);

			var ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
				.FirstOrDefault()?.Name.ToString() ?? string.Empty;

			foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
			{
				var parameters = method.ParameterList.Parameters;

				if (parameters.Count == 0 || !parameters[0].Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)))
					continue;

				if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
					continue;

				// Compare on the unqualified type names, since aliases differ between files.
				var signature = method.Identifier.Text + "(" + string.Join(
					", ",
					parameters.Select(p => Resolve(p.Type?.ToString() ?? "?", aliases))) + ")";

				results.Add(new ExtensionMethod(ns, method.Identifier.Text, signature, file));
			}
		}

		return results;
	}

	/// <summary>
	/// Collects <c>using X = Y;</c> alias directives.
	/// </summary>
	/// <param name="globalOnly">
	/// When true, only <c>global using</c> aliases are collected, because those apply to every file
	/// in the compilation.
	/// </param>
	static void CollectAliases(string text, Dictionary<string, string> aliases, bool globalOnly)
	{
		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim();
			var isGlobal = trimmed.StartsWith("global using ", StringComparison.Ordinal);

			if (globalOnly && !isGlobal)
				continue;

			if (!isGlobal && !trimmed.StartsWith("using ", StringComparison.Ordinal))
				continue;

			var body = trimmed[(trimmed.IndexOf("using ", StringComparison.Ordinal) + 6)..].TrimEnd(';', ' ');
			var equals = body.IndexOf('=');

			if (equals <= 0)
				continue;

			var alias = body[..equals].Trim();
			var target = body[(equals + 1)..].Trim();

			if (alias.Length > 0 && target.Length > 0)
				aliases[alias] = target;
		}
	}

	/// <summary>
	/// Resolves a parameter type through any aliases, then reduces it to its final identifier.
	/// </summary>
	/// <remarks>
	/// Without alias resolution the check is close to useless in this repository: Core writes
	/// <c>TizenNativeView</c> (a global alias) where Wave B writes <c>NView</c> (a file alias), and
	/// both are <c>Tizen.NUI.BaseComponents.View</c>. Comparing the written names reports two
	/// genuinely colliding signatures as distinct — which is exactly what this test missed on its
	/// first run.
	/// </remarks>
	static string Resolve(string type, IReadOnlyDictionary<string, string> aliases)
	{
		var current = type.TrimEnd('?').Trim();

		// Aliases can chain, so follow them, with a hard stop against a cycle.
		for (var i = 0; i < 8 && aliases.TryGetValue(current, out var target); i++)
			current = target.TrimEnd('?').Trim();

		current = current.Replace("global::", string.Empty, StringComparison.Ordinal);

		var index = current.LastIndexOf('.');
		return index >= 0 ? current[(index + 1)..] : current;
	}

	[Fact]
	public void NoTwoCompiledSourcesDeclareTheSameExtensionSignature()
	{
		var duplicates = ExtensionMethods()
			.GroupBy(m => (m.Namespace, m.Signature))
			.Where(g => g.Select(m => m.File).Distinct(StringComparer.Ordinal).Count() > 1)
			.Select(g => $"{g.Key.Namespace}.{g.Key.Signature} is declared in: " + string.Join(
				", ",
				g.Select(m => Path.GetFileName(m.File)).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal)))
			.OrderBy(m => m, StringComparer.Ordinal)
			.ToList();

		Assert.Empty(duplicates);
	}

	/// <summary>
	/// The three signatures Core owns must have exactly one declaration once Core is in the tree.
	/// </summary>
	/// <remarks>
	/// Named explicitly so that the failure message points straight at the ownership decision
	/// instead of at a generic duplicate report.
	/// </remarks>
	[Theory]
	[InlineData("UpdateVisibility")]
	[InlineData("UpdateFlowDirection")]
	[InlineData("ToPlatformVisibility")]
	public void CoreOwnedViewExtensionsAreDeclaredOnce(string name)
	{
		var declarations = ExtensionMethods()
			.Where(m => m.Namespace == "Microsoft.Maui.Platforms.Tizen")
			.Where(m => m.Name == name)
			.GroupBy(m => m.Signature)
			.Where(g => g.Select(m => m.File).Distinct(StringComparer.Ordinal).Count() > 1)
			.Select(g => $"{name}{g.Key} is declared in: " + string.Join(
				", ",
				g.Select(m => Path.GetFileName(m.File)).Distinct(StringComparer.Ordinal)))
			.ToList();

		Assert.Empty(declarations);
	}
}
