// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Generates and verifies <c>docs/mapper-parity-matrix.md</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The matrix is generated from the shipped mappers rather than maintained by hand, because a
	/// hand-maintained one drifts and then actively misleads: a reader checking whether a property
	/// is supported would get an answer that used to be true.
	/// </para>
	/// <para>
	/// Regenerate with <c>MAUI_TIZEN_UPDATE_PARITY_MATRIX=1 dotnet test</c>.
	/// </para>
	/// </remarks>
	public class MapperParityMatrixTests
	{
		const string EnvUpdate = "MAUI_TIZEN_UPDATE_PARITY_MATRIX";

		/// <summary>
		/// Keys that resolve through MAUI's chain here, are inert today, and whose implementation
		/// would belong to the <b>Controls</b> layer rather than to this backend.
		/// </summary>
		/// <remarks>
		/// <para>
		/// These properties live on Controls types (<c>Button.ContentLayout</c>,
		/// <c>InputView.TextTransform</c>, <c>Button.LineBreakMode</c>), not on the
		/// <c>Microsoft.Maui.*</c> interfaces this package consumes, and upstream applies them from
		/// <c>Microsoft.Maui.Controls.Platform</c> rather than from a Core handler. Implementing
		/// them here would mean referencing Controls from the product package, which this repository
		/// deliberately does not do.
		/// </para>
		/// <para>
		/// The shipping Controls project now compiles a narrow startup/mapping bridge. That bridge
		/// maps Label.LineBreakMode and accessibility, but not Button.LineBreakMode, ContentLayout,
		/// or TextTransform. The raw imported files that mention these keys remain outside its
		/// compile closure, so these Wave A keys are still reported as <c>inherited</c>.
		/// </para>
		/// </remarks>
		public static readonly IReadOnlyDictionary<string, string> ControlsLayerFollowUp =
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["TextTransform"] = "Core/Platform/Tizen/Extensions/TextExtensions.cs",
				["ContentLayout"] = "Core/Platform/Tizen/Extensions/ButtonExtensions.cs",
				["LineBreakMode"] = "Core/Button/Button.Tizen.cs",
			};

		/// <summary>
		/// The Controls-layer follow-up must match the shipping Controls compile closure.
		/// </summary>
		/// <remarks>
		/// If the bridge starts compiling or binding any of these implementations, this fails and
		/// forces the matrix to be re-measured rather than silently retaining stale <c>inherited</c>
		/// classifications.
		/// </remarks>
		[Fact]
		public void ControlsLayerFollowUpMatchesCompiledBridge()
		{
			var controlsRoot = FindRepositoryDirectory(Path.Combine("src", "Maui.Tizen.Controls"));
			var compiled = MSBuildEvaluation
				.GetItemRelativePaths("src/Maui.Tizen.Controls/Maui.Tizen.Controls.csproj", "Compile")
				.OrderBy(path => path, StringComparer.Ordinal)
				.ToArray();

			Assert.Equal(
				new[]
				{
					"src/Maui.Tizen.Controls/Platform/TizenControlsHostingExtensions.cs",
					"src/Maui.Tizen.Controls/Platform/TizenControlsMappings.cs",
				},
				compiled);

			foreach (var (key, relativePath) in ControlsLayerFollowUp)
			{
				var file = Path.Combine(controlsRoot, relativePath);
				var repositoryRelativePath = Path.GetRelativePath(TestRepositoryPaths.Root, file)
					.Replace('\\', '/');

				Assert.True(
					File.Exists(file),
					$"'{key}' is documented as Controls-layer follow-up in {relativePath}, which does not exist.");

				Assert.True(
					File.ReadAllText(file).Contains(key, StringComparison.Ordinal),
					$"'{key}' is documented as Controls-layer follow-up in {relativePath}, but that " +
					"file never mentions it - the note is stale.");

				Assert.DoesNotContain(repositoryRelativePath, compiled);
			}

			var mappings = File.ReadAllText(Path.Combine(
				controlsRoot, "Platform", "TizenControlsMappings.cs"));

			Assert.DoesNotContain("ButtonHandler.Mapper.AppendToMapping", mappings, StringComparison.Ordinal);
			Assert.DoesNotContain("ContentLayout", mappings, StringComparison.Ordinal);
			Assert.DoesNotContain("TextTransform", mappings, StringComparison.Ordinal);
		}

		[Fact]
		public void MatrixIsUpToDate()
		{
			var path = FindRepositoryFile(Path.Combine("docs", "mapper-parity-matrix.md"));
			var generated = Generate();

			if (Environment.GetEnvironmentVariable(EnvUpdate) == "1")
			{
				File.WriteAllText(path, generated);
				return;
			}

			var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

			Assert.True(
				Normalize(existing) == Normalize(generated),
				$"docs/mapper-parity-matrix.md is stale. Regenerate it with " +
				$"`{EnvUpdate}=1 dotnet test tests/Maui.Tizen.Core.UnitTests`.");
		}

		static string Normalize(string value) => value.Replace("\r\n", "\n").TrimEnd();

		/// <summary>
		/// The keys a handler implements itself, read from its own mapper initializer.
		/// </summary>
		/// <remarks>
		/// Read from source because a composed <see cref="IPropertyMapper"/> cannot say which
		/// layer a key came from, and that is precisely the distinction this table exists to
		/// report.
		/// </remarks>
		static IReadOnlySet<string> OwnKeys(TizenControlHandlers.ControlHandlerCase handler)
		{
			var path = Path.Combine(
				TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Handlers", handler.HandlerType.Name + ".cs");

			var own = new HashSet<string>(StringComparer.Ordinal);

			foreach (var line in File.ReadAllLines(path))
			{
				var m = System.Text.RegularExpressions.Regex.Match(
					line, @"\[(?:nameof\([\w.]*?(\w+)\)|""(\w+)"")\]\s*=\s*Map");

				if (!m.Success)
					continue;

				own.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
			}

			return own;
		}

		static string Generate()
		{
			// Measure post-remap, or every Controls-only key would be reported as absent.
			ControlsRemap.Force();

			var sb = new StringBuilder();

			sb.AppendLine("# Mapper parity matrix");
			sb.AppendLine();
			sb.AppendLine("<!--");
			sb.AppendLine("  GENERATED FILE - do not edit by hand.");
			sb.AppendLine();
			sb.AppendLine("  Produced from the shipped mappers by");
			sb.AppendLine("  tests/Maui.Tizen.Core.UnitTests/MapperParityMatrixTests.cs. Regenerate with:");
			sb.AppendLine();
			sb.AppendLine($"    {EnvUpdate}=1 dotnet test tests/Maui.Tizen.Core.UnitTests");
			sb.AppendLine("-->");
			sb.AppendLine();
			sb.AppendLine("Every property MAUI can push at a handler, and what this backend does with it.");
			sb.AppendLine("Generated from the real mappers, so it cannot drift from the code.");
			sb.AppendLine();
			sb.AppendLine("**Parity is measured against MAUI Controls, not Core alone.** `Microsoft.Maui.Controls`");
			sb.AppendLine("calls `RemapForControls` for each control, mutating MAUI's static handler mappers in");
			sb.AppendLine("place - adding `FormattedText`, `TextType`, `LineBreakMode`, `MaxLines`,");
			sb.AppendLine("`TextTransform`, `CheckBox.Color`, `Picker.ItemsSource`, `Stepper.Increment` and the");
			sb.AppendLine("accessibility keys.");
			sb.AppendLine();
			sb.AppendLine("Only `Label` and `CheckBox` remap from a static constructor. Every other control here");
			sb.AppendLine("is remapped by `ConfigureControls` when a `MauiApp` is **built**, so these numbers are");
			sb.AppendLine("taken after building a real Controls host (`ControlsRemap.Force`). Running class");
			sb.AppendLine("constructors alone would leave most mappers un-remapped and quietly report a");
			sb.AppendLine("Core-only subset instead of what an application sees.");
			sb.AppendLine();
			sb.AppendLine("| Legend | Meaning |");
			sb.AppendLine("|---|---|");
			sb.AppendLine("| tizen | The backend supplies a Tizen implementation. |");
			sb.AppendLine("| unsupported | The backend explicitly maps the key to a documented no-op. |");
			sb.AppendLine("| inherited | The key resolves through MAUI's chained mapper, but its body is the");
			sb.AppendLine("| | off-platform no-op - so nothing happens on Tizen. Reachable, not implemented. |");
			sb.AppendLine("| excluded | Deliberately not implemented, for a documented reason. |");
			sb.AppendLine("| **MISSING** | Not reachable at all. Nothing should be in this state. |");
			sb.AppendLine("| n/a | MAUI's handler does not define the key either. |");
			sb.AppendLine();
			sb.AppendLine("The `inherited` distinction matters and is the reason this table is generated rather");
			sb.AppendLine("than written: chaining MAUI's static mapper makes every key *resolve*, so a table that");
			sb.AppendLine("only reported presence would show total parity while most properties did nothing.");
			sb.AppendLine();
			sb.AppendLine("Two keys are `excluded` throughout, both inherited from the core slice's base mapper:");
			sb.AppendLine();
			sb.AppendLine("- `ContainerView` - `ViewHandler.ContainerView` has a `private protected` setter, so an");
			sb.AppendLine("  out-of-repo backend cannot publish a container view it constructs. Background, clip and");
			sb.AppendLine("  shadow are rendered onto the platform view instead (`NeedsContainer => false`). Tracked");
			sb.AppendLine("  upstream by dotnet/maui#37854; re-measure this key when that lands.");
			sb.AppendLine("- `Border` - the obsolete `IBorder.Border` mapping. MAUI marks the property `[Obsolete]`");
			sb.AppendLine("  and states it will be removed; border rendering is driven by the stroke and shape");
			sb.AppendLine("  properties that replaced it.");
			sb.AppendLine();
			sb.AppendLine("`TextTransform`, `ContentLayout` and `Button.LineBreakMode` remain `inherited` after");
			sb.AppendLine("re-measuring the compiled Controls bridge. They are Controls properties that upstream");
			sb.AppendLine("applies from `Microsoft.Maui.Controls.Platform`, not from the Core interfaces consumed");
			sb.AppendLine("by these handlers. The shipping `Maui.Tizen.Controls` assembly currently compiles only");
			sb.AppendLine("its startup/mapping bridge. That bridge maps **Label** `LineBreakMode` and accessibility;");
			sb.AppendLine("it does not map **Button** `LineBreakMode`, `ContentLayout`, or `TextTransform`, and the");
			sb.AppendLine("raw imported files that mention those keys remain outside the compile closure.");
			sb.AppendLine("`ControlsLayerFollowUpMatchesCompiledBridge` pins that closure so adding an implementation");
			sb.AppendLine("forces this matrix to be re-measured.");
			sb.AppendLine();

			sb.AppendLine("## Intentional no-op mappings");
			sb.AppendLine();
			sb.AppendLine("These entries are reachable, but either their mapper body or the compiled platform");
			sb.AppendLine("extension it delegates to is empty. `UnsupportedMapperClassificationTests` follows");
			sb.AppendLine("those terminal calls: adding a no-op without evidence, implementing a listed terminal,");
			sb.AppendLine("or turning a behavioral terminal into a no-op fails the test.");
			sb.AppendLine();
			sb.AppendLine("| Owner | Kind | Key | Terminal | Evidence |");
			sb.AppendLine("|---|---|---|---|---|");
			foreach (var mapping in UnsupportedMapperMappings.All
				.OrderBy(mapping => mapping.Owner, StringComparer.Ordinal)
				.ThenBy(mapping => mapping.Key, StringComparer.Ordinal))
			{
				var terminal = mapping.TerminalMethod is null
					? $"{mapping.Owner}.{mapping.Method}"
					: $"{mapping.TerminalFile}.{mapping.TerminalMethod}";
				sb.AppendLine(
					$"| `{mapping.Owner}` | {mapping.Kind} | `{mapping.Key}` | `{terminal}` | {mapping.Evidence} |");
			}
			sb.AppendLine();

			var viewKeys = TizenViewMappers.ViewMapper.GetKeys().ToHashSet(StringComparer.Ordinal);

			sb.AppendLine("## Common view properties");
			sb.AppendLine();
			sb.AppendLine("Inherited by every control below through `TizenViewMappers.ViewMapper`, the");
			sb.AppendLine("Tizen-owned base mapper. Chaining MAUI's neutral `ViewHandler.ViewMapper` instead would");
			sb.AppendLine("register every key while doing nothing, because its bodies are the off-platform no-ops.");
			sb.AppendLine();
			sb.AppendLine("| Key | Status |");
			sb.AppendLine("|---|---|");
			foreach (var key in viewKeys.Order(StringComparer.Ordinal))
			{
				var status = UnsupportedMapperMappings.IsUnsupported("TizenViewMappers", key)
					? "unsupported"
					: "mapped";
				sb.AppendLine($"| `{key}` | {status} |");
			}
			sb.AppendLine();

			foreach (var handler in TizenControlHandlers.All)
			{
				var tizenKeys = TizenControlHandlers.GetMapperKeys(handler.HandlerType);
				var neutralKeys = TizenControlHandlers.GetNeutralMapperKeys(handler.NeutralHandlerName);

				// Everything reachable purely through MAUI's chain, i.e. with MAUI's body.
				var inheritedOnly = neutralKeys
					.Except(viewKeys, StringComparer.Ordinal)
					.Except(OwnKeys(handler), StringComparer.Ordinal)
					.ToHashSet(StringComparer.Ordinal);

				var own = tizenKeys.Except(viewKeys, StringComparer.Ordinal)
					.Union(neutralKeys.Except(viewKeys, StringComparer.Ordinal), StringComparer.Ordinal)
					.Order(StringComparer.Ordinal)
					.ToList();

				sb.AppendLine($"## {handler.HandlerType.Name}");
				sb.AppendLine();
				sb.AppendLine($"Serves `{handler.VirtualViewType.Name}`; compared against MAUI's `{handler.NeutralHandlerName}`.");
				sb.AppendLine();
				sb.AppendLine("| Key | MAUI | Tizen |");
				sb.AppendLine("|---|---|---|");

				foreach (var key in own)
				{
					var inMaui = neutralKeys.Contains(key);
					var inTizen = tizenKeys.Contains(key);

					var tizenCell =
						UnsupportedMapperMappings.IsUnsupported(handler.HandlerType.Name, key) ? "unsupported"
						: ControlMapperParityTests.IsIntentionallyUnmapped(key) ? "excluded"
						: !inTizen ? "**MISSING**"
						: inheritedOnly.Contains(key) ? "inherited"
						: "tizen";

					sb.AppendLine($"| `{key}` | {(inMaui ? "mapped" : "n/a")} | {tizenCell} |");
				}

				sb.AppendLine();
			}

			return sb.ToString().TrimEnd() + Environment.NewLine;
		}

		static string FindRepositoryFile(string relativePath) =>
			Path.Combine(TestRepositoryPaths.Root, relativePath);

		static string FindRepositoryDirectory(string relativePath) =>
			Path.Combine(TestRepositoryPaths.Root, relativePath);
	}
}
