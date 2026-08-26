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
			sb.AppendLine("calls `RemapForControls` from each control's static constructor, mutating MAUI's static");
			sb.AppendLine("handler mappers in place - adding `FormattedText`, `TextType`, `LineBreakMode`,");
			sb.AppendLine("`MaxLines`, `TextTransform`, `CheckBox.Color` and the accessibility keys. Those");
			sb.AppendLine("constructors are forced before these numbers are taken, so the table reflects what an");
			sb.AppendLine("application actually sees rather than a Core-only subset.");
			sb.AppendLine();
			sb.AppendLine("| Legend | Meaning |");
			sb.AppendLine("|---|---|");
			sb.AppendLine("| tizen | The backend supplies a Tizen implementation. |");
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
						: inheritedOnly.Contains(key) ? "inherited"
						: "tizen";

					sb.AppendLine($"| `{key}` | {(inMaui ? "mapped" : "n/a")} | {tizenCell} |");
				}

				sb.AppendLine();
			}

			return sb.ToString();
		}

		static string FindRepositoryFile(string relativePath) =>
			Path.Combine(TestRepositoryPaths.Root, relativePath);
	}
}
