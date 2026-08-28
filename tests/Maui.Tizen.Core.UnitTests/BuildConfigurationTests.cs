using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Build-configuration invariants that no compile or unit test can observe.
	/// </summary>
	/// <remarks>
	/// Every case here is a defect that produces no error and no warning - the build stays green
	/// and the wrong thing ships. They were found by an MSBuild review rather than by a failure.
	/// </remarks>
	public class BuildConfigurationTests
	{
		const string Sample = "samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj";
		const string Core = "src/Maui.Tizen.Core/Maui.Tizen.Core.csproj";
		const string Controls = "src/Maui.Tizen.Controls/Maui.Tizen.Controls.csproj";

		static string RepositoryRoot => MSBuildEvaluation.RepositoryRoot;

		[Fact]
		public void TheSampleDoesNotRequireTheMicrosoftMauiWorkload()
		{
			// The blocker, and it could only ever have failed in the future.
			//
			// UseMaui imports Microsoft.Maui.Sdk, which needs the Microsoft MAUI workload. The real
			// Tizen lane installs only Samsung's `tizen` workload, by policy: this package exists so
			// an external backend and its sample build from PACKAGES with the Samsung workload
			// alone. With UseMaui set, restore would have failed NETSDK1147 the moment the gate
			// opened - and not one moment before, because restore currently stops earlier at
			// NETSDK1139. A break that first appears on the day everyone is waiting for.
			Assert.Equal(string.Empty, MSBuildEvaluation.GetProperty(Sample, "UseMaui"));
		}

		[Fact]
		public void TheSampleStillTargetsTizenAndKeepsItsManifest()
		{
			// Guards the fix above from being "achieved" by breaking the sample: dropping UseMaui
			// must not disturb the TFM or the manifest, which come from TizenPackage.props and the
			// project itself rather than from the MAUI SDK.
			Assert.Equal("net11.0-tizen11.0", MSBuildEvaluation.GetProperty(Sample, "TargetFramework"));
			Assert.Equal(
				"Platforms/Tizen/tizen-manifest.xml",
				MSBuildEvaluation.GetProperty(Sample, "TizenManifestFile"));
		}

		[Fact]
		public void TheManifestIconExists()
		{
			// The manifest declares <icon>appicon.png</icon>. An unresolvable icon reference is not
			// a build error, so the .tpk would have shipped without one and nobody would have known
			// until they looked at a device.
			var manifest = File.ReadAllText(Path.Combine(
				RepositoryRoot, "samples/Maui.Tizen.Sample/Platforms/Tizen/tizen-manifest.xml"));

			var icon = Regex.Match(manifest, @"<icon>([^<]+)</icon>");

			Assert.True(icon.Success, "The manifest declares no icon.");

			var iconPath = Path.Combine(
				RepositoryRoot, "samples/Maui.Tizen.Sample/Platforms/Tizen", icon.Groups[1].Value);

			Assert.True(File.Exists(iconPath), $"The manifest references '{icon.Groups[1].Value}', which does not exist.");
		}

		[Fact]
		public void TheManifestIconIsARealPng()
		{
			// A zero-byte placeholder would satisfy the existence check and still ship a broken
			// icon, so the header and dimensions are verified.
			var bytes = File.ReadAllBytes(Path.Combine(
				RepositoryRoot, "samples/Maui.Tizen.Sample/Platforms/Tizen/appicon.png"));

			Assert.True(bytes.Length > 100, "The icon is implausibly small.");
			Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8));

			// IHDR width/height are big-endian at offsets 16 and 20.
			var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
			var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

			Assert.Equal(117, width);
			Assert.Equal(117, height);
		}

		[Fact]
		public void TheIconIsCopiedToTheOutputForPackaging()
		{
			var none = MSBuildEvaluation.GetItemRelativePaths(Sample, "None");

			Assert.Contains(none, p => p.EndsWith("Platforms/Tizen/appicon.png", StringComparison.Ordinal));
		}

		[Theory]
		[InlineData(Core)]
		[InlineData(Controls)]
		public void ShippingAssembliesGenerateDocumentation(string project)
		{
			// The heuristic keyed off EnableDefaultCompileItems, which these two turn OFF - not
			// because they have no sources, but because the raw imported tree must not be compiled
			// and their shipping sources are listed explicitly. They are the two projects that
			// export a public API and most need documentation, and docs were silently disabled for
			// exactly them.
			Assert.Equal("true", MSBuildEvaluation.GetProperty(project, "GenerateDocumentationFile"));
		}

		[Fact]
		public void ProjectsThatCompileNothingDoNotGenerateDocumentation()
		{
			// The other half: the default must still hold for the un-ported projects, or the build
			// fills with CS1591 for assemblies that have no API at all.
			Assert.Equal(
				"false",
				MSBuildEvaluation.GetProperty("src/Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj", "GenerateDocumentationFile"));
		}

		[Fact]
		public void TizenPackageVersionsAgreeBetweenMauiPropsAndCentralPackageManagement()
		{
			// Two files carry the same versions with nothing keeping them in step. They drift the
			// first time someone bumps one, and the symptom is a lane compiling against different
			// reference assemblies than the product restores - which looks like a mysterious
			// type-resolution failure rather than a version mismatch.
			//
			// Fail-closed: an unmatched property is a failure, not a skip, so renaming one without
			// the other cannot quietly disable this.
			var mauiProps = File.ReadAllText(Path.Combine(RepositoryRoot, "eng/Maui.props"));
			var central = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Packages.props"));

			foreach (var (property, package) in new[]
			{
				("TizenReferencePackVersion", "Samsung.Tizen.Ref.API15"),
				("TizenUIExtensionsPackageVersion", "Tizen.UIExtensions.NUI"),
			})
			{
				var declared = Regex.Match(mauiProps, $@"<{property}[^>]*>([^<]+)</{property}>");
				Assert.True(declared.Success, $"eng/Maui.props no longer declares {property}.");

				var pinned = Regex.Match(central, $@"<PackageVersion\s+Include=""{Regex.Escape(package)}""\s+Version=""([^""]+)""");
				Assert.True(pinned.Success, $"Directory.Packages.props no longer pins {package}.");

				Assert.Equal(pinned.Groups[1].Value, declared.Groups[1].Value);
			}
		}

		[Fact]
		public void TheAnalyzerAssetSetMatchesBetweenProductAndVerificationLanes()
		{
			// The product consumed a narrower asset set than the lanes that verify it, so a
			// diagnostic surfaced through buildtransitive would appear in one and not the other.
			// The lane is only worth having while it sees what the product sees.
			var shared = File.ReadAllText(Path.Combine(RepositoryRoot, "eng/targets/TizenPackage.props"));

			var productAssets = Regex.Match(
				shared,
				@"PublicApiAnalyzers""[^/]*IncludeAssets=""([^""]+)""");

			Assert.True(productAssets.Success, "The shared analyzer reference was not found.");
			Assert.Contains("buildtransitive", productAssets.Groups[1].Value, StringComparison.Ordinal);

			foreach (var lane in new[]
			{
				"tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj",
				"tests/Maui.Tizen.Controls.RefPackCompile/Maui.Tizen.Controls.RefPackCompile.csproj",
			})
			{
				var text = File.ReadAllText(Path.Combine(RepositoryRoot, lane));

				Assert.Contains("buildtransitive", text, StringComparison.Ordinal);
			}
		}
	}
}
