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

		static string Generate()
		{
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
			sb.AppendLine("| Legend | Meaning |");
			sb.AppendLine("|---|---|");
			sb.AppendLine("| mapped | The Tizen handler maps the key. |");
			sb.AppendLine("| excluded | Deliberately not mapped, for a documented reason - see the note below the table. |");
			sb.AppendLine("| **MISSING** | MAUI maps it and this backend does not. Nothing should be in this state. |");
			sb.AppendLine("| n/a | MAUI's neutral handler does not define the key either. |");
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

					var tizenCell = inTizen
						? "mapped"
						: ControlMapperParityTests.IsIntentionallyUnmapped(key) ? "excluded" : "**MISSING**";

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
