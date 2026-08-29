using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maui.Tizen.SourceTests;

/// <summary>A mapper entry extracted from a migrated handler's source.</summary>
public sealed record MapperEntry(string Key, string Method, bool IsNoOp, string? Reason)
{
	public string Status => IsNoOp ? "NoOp" : "Supported";
}

/// <summary>A migrated Wave B handler, parsed from source.</summary>
public sealed record HandlerSource(
	string TypeName,
	string BaseType,
	string Namespace,
	string RelativePath,
	IReadOnlyList<MapperEntry> PropertyMappers,
	IReadOnlyList<MapperEntry> CommandMappers);

/// <summary>
/// Parses the migrated Wave B sources with Roslyn.
/// </summary>
/// <remarks>
/// Wave B owns these paths. Files still carrying the upstream <c>.Tizen.cs</c> suffix have not been
/// migrated yet and belong to another wave, so they are deliberately not asserted on here.
/// </remarks>
public static class WaveBSource
{
	public static IReadOnlyList<string> Files { get; } = Discover();

	public static IReadOnlyList<HandlerSource> Handlers { get; } = Files
		.SelectMany(Parse)
		.OrderBy(h => h.TypeName, StringComparer.Ordinal)
		.ToList();

	public static HandlerSource SharedViewMapper { get; } = Parse(
		RepoPaths.Combine("src", "Maui.Tizen.Core", "Handlers", "TizenViewMappers.cs"))
		.Single(handler => handler.TypeName == "TizenViewMappers");

	public static SyntaxTree ParseTree(string path) =>
		CSharpSyntaxTree.ParseText(
			File.ReadAllText(path),
			new CSharpParseOptions(
				LanguageVersion.Latest,
				preprocessorSymbols: ["TIZEN"]),
			path);

	static IReadOnlyList<string> Discover()
	{
		// Wave B handlers live one level down, under a per-control folder inherited from the
		// upstream layout (Handlers/ScrollView/...). The core vertical slice puts its own handlers
		// directly in Handlers/, so requiring a subfolder keeps these tests off core-owned files.
		var coreHandlers = RepoPaths.Combine("src", "Maui.Tizen.Core", "Handlers");

		var waveB = Directory.Exists(coreHandlers)
			? Directory.EnumerateDirectories(coreHandlers)
				.SelectMany(d => Directory.EnumerateFiles(d, "Tizen*.cs", SearchOption.AllDirectories))
			: Enumerable.Empty<string>();

		string[] wholeRoots =
		{
			Path.Combine("src", "Maui.Tizen.Core", "ImageSources"),
			Path.Combine("src", "Maui.Tizen.Controls", "Core", "Handlers", "Shapes"),
		};

		return waveB
			.Concat(wholeRoots
				.Select(r => RepoPaths.Combine(r))
				.Where(Directory.Exists)
				.SelectMany(r => Directory.EnumerateFiles(r, "*Tizen*.cs", SearchOption.AllDirectories)))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// Parses every handler declared in <paramref name="path"/>.
	/// </summary>
	/// <remarks>
	/// Public so that Wave C can reuse it instead of duplicating the parser. The extraction rules
	/// (mapper field names, <c>nameof</c> keys, empty-body no-ops) are wave-independent.
	/// </remarks>
	public static IEnumerable<HandlerSource> Parse(string path)
	{
		var root = ParseTree(path).GetRoot();

		foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
		{
			// Constants used as mapper keys, e.g. [StrokeDashArrayKey] = MapStrokeDashArray.
			var constants = type.Members
				.OfType<FieldDeclarationSyntax>()
				.Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)))
				.SelectMany(f => f.Declaration.Variables)
				.Where(v => v.Initializer?.Value is LiteralExpressionSyntax)
				.ToDictionary(
					v => v.Identifier.Text,
					v => ((LiteralExpressionSyntax)v.Initializer!.Value).Token.ValueText,
					StringComparer.Ordinal);

			var methods = type.Members
				.OfType<MethodDeclarationSyntax>()
				.GroupBy(m => m.Identifier.Text, StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

			var property = new List<MapperEntry>();
			var command = new List<MapperEntry>();

			foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
			{
				foreach (var variable in field.Declaration.Variables)
				{
					// Suffix rather than exact match: handlers that shadow a generic base mapper
					// have to give the field a distinct name (CarouselViewMapper,
					// ItemsViewCommandMapper, ...), and an exact "Mapper" match silently reported
					// those handlers as having no mapper coverage at all - which then showed up as
					// dozens of fictitious parity gaps. CommandMapper is tested first because a
					// name ending in "CommandMapper" also ends in "Mapper".
					var name = variable.Identifier.Text;
					var target =
						name.EndsWith("CommandMapper", StringComparison.Ordinal) ? command
						: name.EndsWith("Mapper", StringComparison.Ordinal) ? property
						: null;

					if (target is null || variable.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation)
					{
						continue;
					}

					foreach (var expr in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
						?? Enumerable.Empty<AssignmentExpressionSyntax>())
					{
						var key = ExtractKey(expr.Left, constants);
						var method = expr.Right.ToString();

						if (key is null)
						{
							continue;
						}

						methods.TryGetValue(method, out var decl);
						target.Add(new MapperEntry(key, method, IsNoOp(decl), Reason(decl)));
					}
				}
			}

			if (property.Count > 0 || command.Count > 0 || type.Identifier.Text.StartsWith("Tizen", StringComparison.Ordinal))
			{
				yield return new HandlerSource(
					type.Identifier.Text,
					type.BaseList?.Types.FirstOrDefault()?.Type.ToString() ?? string.Empty,
					root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
						.FirstOrDefault()?.Name.ToString() ?? string.Empty,
					Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'),
					property,
					command);
			}
		}
	}

	/// <summary>
	/// Reads the mapper key out of <c>[nameof(IFoo.Bar)]</c>, <c>["Literal"]</c> or <c>[SomeConst]</c>.
	/// </summary>
	/// <remarks>
	/// The constant form matters: keys contributed by Microsoft.Maui.Controls have no corresponding
	/// property on the Core interface, so they cannot be written with <c>nameof</c> and are declared
	/// as named constants instead. An extractor that only understood <c>nameof</c> reported those
	/// keys as unmapped even though they were mapped.
	/// </remarks>
	static string? ExtractKey(ExpressionSyntax left, IReadOnlyDictionary<string, string> constants)
	{
		if (left is not ImplicitElementAccessSyntax access)
		{
			return null;
		}

		var argument = access.ArgumentList.Arguments.FirstOrDefault()?.Expression;

		if (argument is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } invocation)
		{
			var operand = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString();
			return operand?.Split('.').Last();
		}

		if (argument is LiteralExpressionSyntax literal)
			return literal.Token.ValueText;

		if (argument is IdentifierNameSyntax identifier && constants.TryGetValue(identifier.Identifier.Text, out var constant))
			return constant;

		if (argument is MemberAccessExpressionSyntax member)
		{
			if (member.Name.Identifier.Text == "PropertyName"
				&& member.Expression is MemberAccessExpressionSyntax property)
			{
				var name = property.Name.Identifier.Text;
				return name.EndsWith("Property", StringComparison.Ordinal)
					? name[..^"Property".Length]
					: name;
			}

			if (constants.TryGetValue(member.Name.Identifier.Text, out var memberConstant))
				return memberConstant;
		}

		return null;
	}

	/// <summary>A mapper target with an empty body is an intentional no-op.</summary>
	static bool IsNoOp(MethodDeclarationSyntax? method)
	{
		if (method is null)
		{
			return false;
		}

		if (method.ExpressionBody is not null)
		{
			return false;
		}

		return method.Body is not null && method.Body.Statements.Count == 0;
	}

	/// <summary>Pulls the documented justification out of the method's XML doc comment.</summary>
	static string? Reason(MethodDeclarationSyntax? method)
	{
		if (method is null)
		{
			return null;
		}

		var trivia = method.GetLeadingTrivia().ToFullString();
		var lines = trivia
			.Split('\n')
			.Select(l => l.Trim().TrimStart('/').Trim())
			.Where(l => l.Length > 0 && !l.StartsWith('<'))
			.ToList();

		var text = string.Join(" ", lines).Trim();
		return text.Length == 0 ? null : text;
	}
}
