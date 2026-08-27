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
		/// Keys that resolve through MAUI's chain here, but which the <b>Controls</b> layer
		/// implements - exactly as upstream does.
		/// </summary>
		/// <remarks>
		/// <para>
		/// These properties live on Controls types (<c>Button.ContentLayout</c>,
		/// <c>InputView.TextTransform</c>), not on the <c>Microsoft.Maui.*</c> interfaces this
		/// package consumes, and upstream applies them from
		/// <c>Microsoft.Maui.Controls.Platform.TextExtensions</c> / <c>ButtonExtensions</c> rather
		/// than from a Core handler. Reporting them as plain <c>inherited</c> would read as a Wave A
		/// defect; reporting them as <c>tizen</c> would be a lie.
		/// </para>
		/// <para>
		/// <see cref="EveryControlsLayerKeyIsImplementedThere"/> checks each entry against the actual
		/// Controls sources, so this list cannot become an excuse.
		/// </para>
		/// </remarks>
		public static readonly IReadOnlyDictionary<string, string> ControlsLayerImplementations =
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["TextTransform"] = "Core/Platform/Tizen/Extensions/TextExtensions.cs",
				["ContentLayout"] = "Core/Platform/Tizen/Extensions/ButtonExtensions.cs",
				["LineBreakMode"] = "Core/Button/Button.Tizen.cs",
			};

		static readonly HashSet<string> ControlsLayerKeys =
			ControlsLayerImplementations.Keys.ToHashSet(StringComparer.Ordinal);

		/// <summary>
		/// Every key excused as `controls` really is implemented in the Controls layer.
		/// </summary>
		[Fact]
		public void EveryControlsLayerKeyIsImplementedThere()
		{
			var controlsRoot = FindRepositoryDirectory(Path.Combine("src", "Maui.Tizen.Controls"));

			foreach (var (key, relativePath) in ControlsLayerImplementations)
			{
				var file = Path.Combine(controlsRoot, relativePath);

				Assert.True(File.Exists(file), $"'{key}' is documented as implemented by {relativePath}, which does not exist.");

				Assert.True(
					File.ReadAllText(file).Contains(key, StringComparison.Ordinal),
					$"'{key}' is documented as implemented by {relativePath}, but that file never mentions it. " +
					"Either the key moved, or the matrix is excusing a gap that is not actually covered anywhere.");
			}
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
			sb.AppendLine("| controls | Implemented by the Controls layer (`src/Maui.Tizen.Controls`), which is");
			sb.AppendLine("| | where upstream implements it too - not by the Core backend. |");
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
			sb.AppendLine("  shadow are rendered onto the platform view instead (`NeedsContainer => false`).");
			sb.AppendLine("- `Border` - the obsolete `IBorder.Border` mapping. MAUI marks the property `[Obsolete]`");
			sb.AppendLine("  and states it will be removed; border rendering is driven by the stroke and shape");
			sb.AppendLine("  properties that replaced it.");
			sb.AppendLine();
			sb.AppendLine("The `controls` keys are not backend gaps. `TextTransform` and `ContentLayout` are");
			sb.AppendLine("carried on Controls types, not on the `Microsoft.Maui.*` interfaces this package");
			sb.AppendLine("consumes, and upstream applies them from `Microsoft.Maui.Controls.Platform` rather than");
			sb.AppendLine("from a Core handler. Implementing them here would mean referencing Controls from the");
			sb.AppendLine("product package, which this repository deliberately does not do. The sources that");
			sb.AppendLine("implement them already exist under `src/Maui.Tizen.Controls`, awaiting the wave that");
			sb.AppendLine("owns that project; `MapperParityMatrixTests` verifies each one is really implemented");
			sb.AppendLine("there, so this state cannot be used to wave a key away.");
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
				sb.AppendLine($"| `{key}` | mapped |");
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
						ControlMapperParityTests.IsIntentionallyUnmapped(key) ? "excluded"
						: !inTizen ? "**MISSING**"
						: !inheritedOnly.Contains(key) ? "tizen"
						: ControlsLayerKeys.Contains(key) ? "controls"
						: "inherited";

					sb.AppendLine($"| `{key}` | {(inMaui ? "mapped" : "n/a")} | {tizenCell} |");
				}

				sb.AppendLine();
			}

			return sb.ToString();
		}

		static string FindRepositoryFile(string relativePath) =>
			Path.Combine(TestRepositoryPaths.Root, relativePath);

		static string FindRepositoryDirectory(string relativePath) =>
			Path.Combine(TestRepositoryPaths.Root, relativePath);
	}
}
