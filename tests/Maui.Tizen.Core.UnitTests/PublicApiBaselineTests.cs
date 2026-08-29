using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the product's declared public API baseline.
	/// </summary>
	/// <remarks>
	/// The analyzer already enforces that the baseline matches the compiled assembly (RS0016 for a
	/// symbol missing from the baseline, RS0017 for a baseline entry missing from the assembly).
	/// These tests guard the things the analyzer cannot see: that the baseline is actually
	/// populated, that it describes <em>this</em> package rather than the inherited MAUI surface,
	/// and that the sample's API has not leaked into the product's.
	/// </remarks>
	[Collection(StaticMapperCollection.Name)]
	public class PublicApiBaselineTests
	{
		static string RepositoryRoot
		{
			get
			{
				var dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
					dir = dir.Parent;

				return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
			}
		}

		static string[] ProductBaseline => File.ReadAllLines(
			Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt"));

		[Fact]
		public void ProductBaselineIsPopulated()
		{
			// An empty baseline plus a suppressed RS0016 is how this check gets silently defeated.
			var entries = ProductBaseline.Where(l => !l.StartsWith('#') && l.Length > 0).ToArray();

			Assert.True(entries.Length > 300, $"Expected a fully populated baseline, found {entries.Length} entries.");
		}

		[Fact]
		public void ProductBaselineDescribesThisPackageNotMauis()
		{
			var entries = ProductBaseline.Where(l => !l.StartsWith('#') && l.Length > 0).ToArray();

			Assert.All(entries, e =>
				Assert.True(
					e.Contains("Microsoft.Maui.Platforms.Tizen", StringComparison.Ordinal),
					$"Baseline entry does not belong to this package: {e}"));
		}

		[Fact]
		public void ProductBaselineDoesNotContainSampleTypes()
		{
			// The sample is an app head, not part of the shipped package; its API lives beside it.
			Assert.DoesNotContain(
				ProductBaseline,
				e => e.Contains("Maui.Tizen.Sample", StringComparison.Ordinal));
		}

		[Fact]
		public void Rs0016IsNotSuppressedAnywhere()
		{
			// Suppressing RS0016 would let a new public API land with no baseline entry, which is
			// exactly what this whole mechanism exists to prevent.
			foreach (var project in new[]
			{
				"src/Maui.Tizen.Core/Maui.Tizen.Core.csproj",
				"tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj",
			})
			{
				var text = File.ReadAllText(Path.Combine(RepositoryRoot, project));

				// Only actual suppression counts - RS0016 appears in explanatory comments, and
				// matching those would make this test fire on documentation changes.
				var suppressions = System.Text.RegularExpressions.Regex.Matches(
					text,
					@"<(NoWarn|WarningsNotAsErrors)>[^<]*RS0016[^<]*</(NoWarn|WarningsNotAsErrors)>");

				Assert.Empty(suppressions);

				var perItem = System.Text.RegularExpressions.Regex.Matches(
					text, @"NoWarn\s*=\s*""[^""]*RS0016");

				Assert.Empty(perItem);
			}
		}

		[Fact]
		public void InheritedMauiBaselineIsPreservedButDetached()
		{
			var inherited = Path.Combine(
				RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/net-tizen/PublicAPI.Shipped.txt");

			Assert.True(File.Exists(inherited), "The imported MAUI baseline should be preserved on disk.");

			var project = File.ReadAllText(
				Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/Maui.Tizen.Core.csproj"));

			Assert.Contains("AdditionalFiles Remove=", project, StringComparison.Ordinal);
		}

		[Theory]
		[InlineData("Microsoft.Maui.Platforms.Tizen.Handlers.TizenLabelHandler")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.Handlers.TizenLayoutHandler")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.Handlers.TizenViewMappers")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenDispatcher")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenWindowLifecycleBridge")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.Hosting.TizenMauiAppBuilderExtensions")]
		public void KeyPublicTypeIsDeclaredInTheBaseline(string typeName) =>
			Assert.Contains(ProductBaseline, e => e.Contains(typeName, StringComparison.Ordinal));
	}
}
