using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards against the mapper-dispatch crash pattern Wave A hit.
/// </summary>
/// <remarks>
/// <para>
/// MAUI's <c>PropertyMapper&lt;TVirtualView, TViewHandler&gt;</c> casts the handler to
/// <c>TViewHandler</c> when it dispatches a mapping. Wave A found that an interface-declared MAUI
/// mapper can end up instantiated over a <em>concrete built-in</em> handler, at which point any
/// chained-only key whose delegate hard-casts to a Tizen handler throws at runtime rather than at
/// build time.
/// </para>
/// <para>
/// Wave C is structurally exposed to this: all 86 of its mapper delegates take a concrete
/// <c>Tizen*Handler</c> as their first parameter. These tests pin the two source-level invariants
/// that keep that safe. They are necessary but <b>not sufficient</b> - see
/// <see cref="MapperDispatchRequiresARealControlsHostAfterTheRebase"/> for the runtime half, which
/// cannot run until the predecessor stack lands.
/// </para>
/// </remarks>
public class WaveCMapperDispatchTests
{
	/// <summary>
	/// Every mapping's delegate must accept exactly the handler type its mapper is declared over.
	/// </summary>
	/// <remarks>
	/// If a mapper declared as <c>PropertyMapper&lt;TVirtual, THandlerA&gt;</c> stores a delegate
	/// whose parameter is <c>THandlerB</c>, dispatch casts to <c>THandlerA</c> and hands it to a
	/// method expecting <c>THandlerB</c>. The compiler accepts that when the types are related, and
	/// it fails only when the mapping is actually invoked - which nothing in a type-check lane does.
	/// </remarks>
	[Fact]
	public void EveryMappingDelegateMatchesItsMapperHandlerType()
	{
		var failures = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var root = WaveBSource.ParseTree(file).GetRoot();

			foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			{
				// Scope per class: several Tizen handlers share a file and reuse method names such
				// as MapText, so a file-wide lookup silently compares the wrong pair.
				var receivers = type.Members
					.OfType<MethodDeclarationSyntax>()
					.Where(m => m.Identifier.Text.StartsWith("Map", StringComparison.Ordinal))
					.Where(m => m.ParameterList.Parameters.Count > 0)
					.GroupBy(m => m.Identifier.Text, StringComparer.Ordinal)
					.ToDictionary(
						g => g.Key,
						g => g.First().ParameterList.Parameters[0].Type?.ToString() ?? string.Empty,
						StringComparer.Ordinal);

				foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
				{
					var declaredType = field.Declaration.Type.ToString();

					var handlerTypeArgument = ExtractHandlerTypeArgument(declaredType);

					if (handlerTypeArgument is null)
					{
						continue;
					}

					foreach (var variable in field.Declaration.Variables)
					{
						if (variable.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation)
						{
							continue;
						}

						var assignments = creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
							?? Enumerable.Empty<AssignmentExpressionSyntax>();

						foreach (var assignment in assignments)
						{
							var target = assignment.Right.ToString();

							if (!receivers.TryGetValue(target, out var receiver))
							{
								continue;
							}

							if (!string.Equals(receiver, handlerTypeArgument, StringComparison.Ordinal))
							{
								failures.Add(
									$"{Path.GetRelativePath(RepoPaths.Root, file)}: {type.Identifier.Text}."
									+ $"{variable.Identifier.Text} is declared over '{handlerTypeArgument}' but "
									+ $"'{target}' takes '{receiver}'. Dispatch would cast to the former and "
									+ "invoke the latter.");
							}
						}
					}
				}
			}
		}

		Assert.Empty(failures);
	}

	/// <summary>
	/// No Wave C mapper may be declared over a MAUI handler <em>interface</em>.
	/// </summary>
	/// <remarks>
	/// This is the shape Wave A's crash came from. A mapper declared over, say,
	/// <c>IToolbarHandler</c> can legitimately be instantiated over MAUI's own built-in handler,
	/// because that also implements the interface - and then a chained Tizen mapping casts a
	/// built-in handler to a Tizen one and throws. Declaring over the concrete
	/// <c>Tizen*Handler</c> makes the mismatch impossible to construct in the first place.
	/// </remarks>
	[Fact]
	public void NoMapperIsDeclaredOverAHandlerInterface()
	{
		var failures = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var root = WaveBSource.ParseTree(file).GetRoot();

			foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
			{
				var handlerTypeArgument = ExtractHandlerTypeArgument(field.Declaration.Type.ToString());

				if (handlerTypeArgument is null)
				{
					continue;
				}

				// I<Name>Handler, but not the Tizen-owned ITizen* contracts.
				if (Regex.IsMatch(handlerTypeArgument, @"^I[A-Z][A-Za-z0-9_]*Handler$")
					&& !handlerTypeArgument.StartsWith("ITizen", StringComparison.Ordinal))
				{
					failures.Add(
						$"{Path.GetRelativePath(RepoPaths.Root, file)}: mapper declared over interface "
						+ $"'{handlerTypeArgument}'. Declare it over the concrete Tizen handler so a "
						+ "built-in handler cannot be substituted at dispatch time.");
				}
			}
		}

		Assert.Empty(failures);
	}

	/// <summary>
	/// The finalized API15 lane compiles every Wave C mapper into the shipping assembly boundary.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The source checks prove delegates and mapper declarations agree. The API15 lane then binds
	/// every mapper body against the shipping assembly boundary, and the Core host suite proves the
	/// one Controls production path resolves all concrete Wave C registrations.
	/// </para>
	/// <para>
	/// The remaining limitation is native execution: NUI views cannot be instantiated on this host,
	/// so actual native property application remains part of the device lane.
	/// </para>
	/// </remarks>
	[Fact]
	public void MapperDispatchIsInTheUnconditionalControlsLane()
	{
		var props = File.ReadAllText(RepoPaths.Combine("eng", "Maui.Tizen.WaveC.Sources.props"));
		var lane = File.ReadAllText(
			RepoPaths.Combine("tests", "Maui.Tizen.Controls.RefPackCompile", "Maui.Tizen.Controls.RefPackCompile.csproj"));

		Assert.DoesNotContain("MauiTizenWaveCAcceptance", props, StringComparison.Ordinal);
		Assert.Contains("@(MauiTizenWaveCAdapterCompile)", lane, StringComparison.Ordinal);
		Assert.Contains("@(MauiTizenWaveCPlatformCompile)", lane, StringComparison.Ordinal);
		Assert.Contains("@(MauiTizenWaveCCompile)", lane, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenItemsViewHandler")]
	[InlineData("TizenShellHandler")]
	[InlineData("TizenFlyoutViewHandler")]
	[InlineData("TizenNavigationViewHandler")]
	[InlineData("TizenTabbedPageHandler")]
	public void RootViewHandlersUseTheTizenBaseMapperComposition(string handlerName)
	{
		var handler = WaveCSource.Handlers.Single(source => source.TypeName == handlerName);
		var source = File.ReadAllText(RepoPaths.Combine(handler.RelativePath.Split('/')));

		Assert.StartsWith("TizenViewHandler<", handler.BaseType, StringComparison.Ordinal);
		Assert.Contains("TizenViewMappers.ViewMapper", source, StringComparison.Ordinal);
		Assert.Contains("TizenViewMappers.ViewCommandMapper", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// Returns the handler type argument of a mapper type, or <see langword="null"/> if the type is
	/// not a mapper.
	/// </summary>
	static string? ExtractHandlerTypeArgument(string declaredType)
	{
		var match = Regex.Match(
			declaredType,
			@"^(?:IPropertyMapper|PropertyMapper|CommandMapper)<\s*(.+)\s*>$");

		if (!match.Success)
		{
			return null;
		}

		// Split on the top-level comma only; either argument may itself be generic.
		var arguments = match.Groups[1].Value;
		var depth = 0;

		for (var i = 0; i < arguments.Length; i++)
		{
			switch (arguments[i])
			{
				case '<':
					depth++;
					break;
				case '>':
					depth--;
					break;
				case ',' when depth == 0:
					return arguments[(i + 1)..].Trim();
			}
		}

		return null;
	}
}
