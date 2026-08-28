using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the full inventory of Tizen-prefixed public interfaces this package exports.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Split out of <c>NoParallelTizenHandlerInterfacesRemain</c>, which asserted the same exact
	/// list under a name that promised something narrower. That conflation had a real cost: a
	/// downstream wave adding a perfectly legitimate <c>ITizen*</c> <em>service</em> interface broke
	/// a test about handler parity, for a reason the test name actively misdescribed.
	/// </para>
	/// <para>
	/// The two concerns are genuinely different. Handler parity is a correctness property - a
	/// parallel <c>ITizenLabelHandler</c> beside MAUI's <c>ILabelHandler</c> forces handlers to
	/// choose and blocks Controls mapper composition. This test is a change-detector over public
	/// API surface: adding to it is normal and expected, and the entry here should be updated in
	/// the same commit that adds the interface.
	/// </para>
	/// </remarks>
	public class TizenPublicInterfaceInventoryTests
	{
		/// <summary>
		/// The inventory as the SHIPPING assembly exports it, read from the PublicAPI baseline.
		/// </summary>
		/// <remarks>
		/// Reflection over the test assembly cannot answer this. The host lane deliberately
		/// compiles only the portable and handler sources, so interfaces declared alongside
		/// TizenFX-dependent code - ITizenToolbarContainer, ITizenLifecycleBuilder - are simply
		/// absent from it. Reflecting here would report a smaller set than the package publishes
		/// and quietly under-pin the surface, which is the opposite of what a change-detector is
		/// for. The baseline is generated from the real product API by the analyzer.
		/// </remarks>
		static string[] ExportedTizenInterfaces => File
			.ReadAllLines(Path.Combine(
				MSBuildEvaluation.RepositoryRoot,
				"src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt"))
			.Select(line => Regex.Match(line, @"(?:^|\.)(ITizen[A-Za-z0-9_]*)"))
			.Where(m => m.Success)
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToArray();

		[Fact]
		public void ExportedTizenInterfacesAreTheExpectedSet()
		{
			// Add to this list when a genuinely new Tizen-owned interface ships, in the commit that
			// ships it. It is deliberately exact so that widening the public surface is a visible
			// decision rather than a side effect.
			Assert.Equal(
				// Ordinal order, matching how the inventory is read.
				new[]
				{
					// Handler contract: MAUI Core ships no IApplicationHandler to implement.
					"ITizenApplicationHandler",

					// Service contract for resolving Tizen font families and registered aliases.
					"ITizenFontManager",

					// Service contract for loading image sources into Tizen-native image values.
					"ITizenImageSourceService",

					// Lifecycle builder, so ConfigureLifecycleEvents(e => e.AddTizen(...)) has a
					// Tizen-specific builder to hang platform events off.
					"ITizenLifecycleBuilder",

					// Service seam used by picker controls until the navigation wave supplies the
					// real modal stack.
					"ITizenModalHost",

					// Handler contract: MAUI's IPlatformViewHandler exists only inside the
					// net*-tizen build, where re-declaring the name would be CS0433.
					"ITizenPlatformViewHandler",

					// Native container contract consumed by the Wave C toolbar work.
					"ITizenToolbarContainer",
				},
				ExportedTizenInterfaces);
		}

		[Fact]
		public void EveryExportedTizenInterfaceIsInTheOwnedNamespace()
		{
			// A Tizen-prefixed interface in a neutral MAUI namespace would be a CS0433 hazard for
			// anyone referencing both assemblies.
			var strays = File
				.ReadAllLines(Path.Combine(
					MSBuildEvaluation.RepositoryRoot,
					"src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt"))
				.Where(line => line.Contains(".ITizen", StringComparison.Ordinal))
				.Where(line => !line.Contains("Microsoft.Maui.Platforms.Tizen", StringComparison.Ordinal))
				.OrderBy(l => l, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(strays);
		}

		[Fact]
		public void TheInventoryIsNotEmpty()
		{
			// Guards the guard: if reflection ever stopped finding these - a renamed assembly, a
			// changed prefix - the assertions above would pass vacuously against an empty set.
			Assert.NotEmpty(ExportedTizenInterfaces);
		}
	}
}
