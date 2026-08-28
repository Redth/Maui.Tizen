using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>The result of dumping MSBuild state from a generated test project.</summary>
public sealed class BuildResult
{
	public required bool Success { get; init; }

	public required string Output { get; init; }

	public required IReadOnlyList<DumpedItem> Items { get; init; }

	public required IReadOnlyDictionary<string, string> Properties { get; init; }

	public IEnumerable<DumpedItem> ItemsOf(string itemName)
		=> Items.Where(i => string.Equals(i.ItemName, itemName, StringComparison.Ordinal));

	public IEnumerable<string> FileNamesOf(string itemName)
		=> ItemsOf(itemName).Select(i => Path.GetFileName(i.Identity));

	public string Property(string name)
		=> Properties.TryGetValue(name, out var value) ? value : string.Empty;
}

public sealed record DumpedItem(string ItemName, string Identity, string Metadata1, string Metadata2);

/// <summary>
/// Generates and builds small MSBuild projects that opt into the Resizetizer external backend
/// contract, then reports the resulting items and properties.
/// </summary>
/// <remarks>
/// The generated projects deliberately use a plain <c>net11.0</c> target framework rather than
/// <c>net11.0-tizen11.0</c>. That is not a workaround: it is the configuration in which
/// <c>_ResizetizerIsTizenApp</c> is false, so the Resizetizer's built-in Tizen branches are
/// inactive and this package's externalized generators are the code under test. It is also the
/// only configuration that can be built without the Samsung workload, which is not installable
/// from a public feed.
/// </remarks>
public sealed class MSBuildProjectBuilder
{
	private readonly string _root;
	private readonly List<string> _itemGroups = new();
	private readonly List<string> _properties = new();
	private readonly List<string> _projectReferences = new();
	private readonly List<string> _packageReferences = new();
	private readonly List<string> _imports = new();
	private readonly List<string> _rawContent = new();

	public MSBuildProjectBuilder(string root, string projectName = "TizenApp")
	{
		_root = root;
		ProjectName = projectName;
		ProjectDirectory = Path.Combine(root, projectName);
		Directory.CreateDirectory(ProjectDirectory);
	}

	public string ProjectName { get; }

	public string ProjectDirectory { get; }

	public string ProjectPath => Path.Combine(ProjectDirectory, ProjectName + ".csproj");

	/// <summary>
	/// When true, <c>ResizetizerPlatformType</c> and the package targets are introduced after the
	/// Resizetizer targets have already been evaluated, exercising the late opt-in path.
	/// </summary>
	public bool LateOptIn { get; set; }

	/// <summary>
	/// When true, the backend arrives as a real <c>PackageReference</c> to the nupkg this test run
	/// produced, rather than through explicit imports of the props and targets in the source tree.
	/// </summary>
	/// <remarks>
	/// The explicit-import projects are the right shape for testing the build LOGIC: they are fast,
	/// they need no pack, and they isolate the targets from packaging concerns. What they cannot
	/// see is whether an application that merely references the package gets any of it. That
	/// depends on NuGet's automatic import of <c>buildTransitive/*.props</c> and
	/// <c>buildTransitive/*.targets</c>, on the file names matching the package id exactly, and on
	/// the task assembly and its native dependencies being laid out where the packaged targets
	/// look for them - none of which the source-tree imports exercise, because they hand the tasks
	/// an explicit assembly path.
	/// </remarks>
	public bool ConsumeProducedPackage { get; set; }

	/// <summary>
	/// An isolated NuGet global-packages folder for the build, used by package-consuming projects.
	/// </summary>
	/// <remarks>
	/// The produced package always carries the same version, so restoring it into the developer's
	/// real global packages folder would install it once and then reuse that first extraction
	/// forever - a later run would silently validate a package built from older sources. A stale
	/// pass is worse than a failure, so these builds get their own folder.
	/// </remarks>
	public string? PackagesFolder { get; set; }

	public MSBuildProjectBuilder WithProperty(string name, string value)
	{
		_properties.Add($"    <{name}>{value}</{name}>");
		return this;
	}

	public MSBuildProjectBuilder WithItem(string itemName, string include, params (string Name, string Value)[] metadata)
	{
		var attributes = string.Concat(metadata.Select(m => $" {m.Name}=\"{m.Value}\""));
		_itemGroups.Add($"    <{itemName} Include=\"{include}\"{attributes} />");
		return this;
	}

	public MSBuildProjectBuilder WithProjectReference(string relativePath)
	{
		_projectReferences.Add($"    <ProjectReference Include=\"{relativePath}\" />");
		return this;
	}

	/// <summary>The project SDK. Blazor scenarios need Microsoft.NET.Sdk.Razor.</summary>
	public string ProjectSdk { get; set; } = "Microsoft.NET.Sdk";

	public MSBuildProjectBuilder WithPackageReference(string id, string version)
	{
		_packageReferences.Add($"    <PackageReference Include=\"{id}\" Version=\"{version}\" />");
		return this;
	}

	/// <summary>
	/// Imports a targets file. The optional alias only affects the generated comment, and exists so
	/// a test can import the same file twice to simulate two packages contributing one provider.
	/// </summary>
	public MSBuildProjectBuilder WithImport(string path, string? alias = null)
	{
		var comment = alias is null ? string.Empty : $"  <!-- {alias} -->" + Environment.NewLine;
		_imports.Add($"{comment}  <Import Project=\"{TestBase.Escape(path)}\" />");
		return this;
	}

	/// <summary>Raw XML appended to the project body, for declaring test-only targets.</summary>
	public MSBuildProjectBuilder WithRawProjectContent(string xml)
	{
		_rawContent.Add(xml);
		return this;
	}

	/// <summary>Writes a placeholder SVG that the Resizetizer can rasterize.</summary>
	public string WriteSvg(string relativePath, string fill = "#512BD4")
	{
		var path = Path.Combine(ProjectDirectory, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, $"""
			<?xml version="1.0" encoding="UTF-8" standalone="no"?>
			<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128">
			  <rect width="128" height="128" fill="{fill}" />
			</svg>
			""");
		return path;
	}

	public string WriteText(string relativePath, string contents)
	{
		var path = Path.Combine(ProjectDirectory, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
		return path;
	}

	public string WriteTizenManifest(string relativePath = "Platforms/Tizen/tizen-manifest.xml")
		=> WriteText(relativePath, """
			<?xml version="1.0" encoding="utf-8"?>
			<manifest package="maui-application-id-placeholder" version="0.0.0" api-version="11" xmlns="http://tizen.org/ns/packages">
			  <profile name="common" />
			  <ui-application appid="maui-application-id-placeholder" exec="TizenApp.dll" multiple="false" nodisplay="false" taskmanage="true" type="dotnet" launch_mode="single">
			    <label>maui-application-title-placeholder</label>
			    <icon>maui-appicon-placeholder</icon>
			  </ui-application>
			</manifest>
			""");

	public void Generate()
	{
		// Isolate the generated project from any Directory.Build.* above the temp folder.
		File.WriteAllText(Path.Combine(_root, "Directory.Build.props"), "<Project />");
		File.WriteAllText(Path.Combine(_root, "Directory.Build.targets"), "<Project />");
		File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
		File.WriteAllText(
			Path.Combine(_root, "NuGet.config"),
			ConsumeProducedPackage ? TestBase.ReadNuGetConfigWithProducedPackages() : TestBase.ReadRepositoryNuGetConfig());

		var propsImport = TestBase.Escape(Path.Combine(TestBase.BuildTransitiveDirectory, "Maui.Tizen.Build.Tasks.props"));
		var targetsImport = TestBase.Escape(Path.Combine(TestBase.BuildTransitiveDirectory, "Maui.Tizen.Build.Tasks.targets"));

		var builder = new StringBuilder();
		builder.AppendLine($"""<Project Sdk="{ProjectSdk}">""");
		builder.AppendLine("  <PropertyGroup>");
		builder.AppendLine("    <TargetFramework>net11.0</TargetFramework>");
		builder.AppendLine("    <OutputType>Exe</OutputType>");
		builder.AppendLine("    <Nullable>disable</Nullable>");
		builder.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");

		// A package-consuming project must NOT redirect the task assembly: the point is to load
		// the one the package laid out, from the path the packaged targets compute themselves.
		if (!ConsumeProducedPackage)
			builder.AppendLine($"    <_MauiTizenBuildTasksAssembly>{TestBase.Escape(TestBase.BuildTasksAssemblyPath)}</_MauiTizenBuildTasksAssembly>");

		if (!LateOptIn)
			builder.AppendLine("    <ResizetizerPlatformType>tizen</ResizetizerPlatformType>");

		foreach (var property in _properties)
			builder.AppendLine(property);

		builder.AppendLine("  </PropertyGroup>");

		if (!LateOptIn && !ConsumeProducedPackage)
			builder.AppendLine($"""  <Import Project="{propsImport}" />""");

		builder.AppendLine("  <ItemGroup>");
		if (ConsumeProducedPackage)
		{
			// No Import anywhere for this package: NuGet's own buildTransitive auto-import is the
			// mechanism under test.
			builder.AppendLine($"""    <PackageReference Include="Maui.Tizen.Build.Tasks" Version="{TestBase.PackageVersion}" />""");
		}

		if (LateOptIn)
		{
			// Suppress NuGet's automatic import so the test controls the order explicitly.
			builder.AppendLine($"""    <PackageReference Include="Microsoft.Maui.Resizetizer" Version="{TestBase.ResizetizerPackageVersion}" ExcludeAssets="build;buildTransitive" GeneratePathProperty="true" />""");
		}
		else
		{
			builder.AppendLine($"""    <PackageReference Include="Microsoft.Maui.Resizetizer" Version="{TestBase.ResizetizerPackageVersion}" />""");
		}
		builder.AppendLine("  </ItemGroup>");

		if (LateOptIn)
		{
			// The Resizetizer package hooks its own After.targets onto AfterMicrosoftNETSdkTargets,
			// which is the last import the SDK performs. A backend imported through ordinary NuGet
			// build assets is therefore always evaluated BEFORE it. To reach the late opt-in path
			// the order is reconstructed here explicitly: the Resizetizer first, this package
			// second, so ResizetizerPlatformType only becomes visible at execution time and the
			// _PrepareExternalMaui* fallbacks are what must do the work.
			builder.AppendLine("  <PropertyGroup>");
			builder.AppendLine("""    <_ResizetizerPackageTargets>$(PkgMicrosoft_Maui_Resizetizer)/buildTransitive/</_ResizetizerPackageTargets>""");
			builder.AppendLine("""    <AfterMicrosoftNETSdkTargets>$(AfterMicrosoftNETSdkTargets);$(_ResizetizerPackageTargets)Microsoft.Maui.Resizetizer.After.targets;$(MSBuildProjectDirectory)/late-opt-in.targets</AfterMicrosoftNETSdkTargets>""");
			builder.AppendLine("  </PropertyGroup>");
			builder.AppendLine("""  <Import Project="$(_ResizetizerPackageTargets)Microsoft.Maui.Resizetizer.Before.targets" />""");
		}

		if (_packageReferences.Count > 0)
		{
			builder.AppendLine("  <ItemGroup>");
			foreach (var reference in _packageReferences)
				builder.AppendLine(reference);
			builder.AppendLine("  </ItemGroup>");
		}

		if (_itemGroups.Count > 0 || _projectReferences.Count > 0)
		{
			builder.AppendLine("  <ItemGroup>");
			foreach (var item in _itemGroups)
				builder.AppendLine(item);
			foreach (var reference in _projectReferences)
				builder.AppendLine(reference);
			builder.AppendLine("  </ItemGroup>");
		}

		if (!LateOptIn && !ConsumeProducedPackage)
			builder.AppendLine($"""  <Import Project="{targetsImport}" />""");

		// After this package's targets, so a provider can append to MauiTizenAssetProviderTargets
		// and so the import order matches a real app: package targets first, app content second.
		foreach (var import in _imports)
			builder.AppendLine(import);

		foreach (var raw in _rawContent)
			builder.AppendLine(raw);

		builder.AppendLine(DumpTarget);
		builder.AppendLine("</Project>");

		File.WriteAllText(ProjectPath, builder.ToString());
		File.WriteAllText(Path.Combine(ProjectDirectory, "Program.cs"), "internal class Program { private static void Main() { } }");

		if (LateOptIn)
		{
			File.WriteAllText(Path.Combine(ProjectDirectory, "late-opt-in.targets"), $"""
				<Project>
				  <PropertyGroup>
				    <ResizetizerPlatformType>tizen</ResizetizerPlatformType>
				  </PropertyGroup>
				  <Import Project="{propsImport}" />
				  <Import Project="{targetsImport}" />
				</Project>
				""");
		}
	}

	private const string DumpTarget = """
		  <Target Name="DumpTizenState" AfterTargets="Build">
		    <ItemGroup>
		      <_Dump Include="@(TizenTpkUserIncludeFiles->'TizenTpkUserIncludeFiles%09%(Identity)%09%(TizenTpkSubDir)%09')" />
		      <_Dump Include="@(TizenResource->'TizenResource%09%(Identity)%09%(TizenTpkFileName)%09%(AssetRole)')" />
		      <_Dump Include="@(MauiProcessedImage->'MauiProcessedImage%09%(Identity)%09%09')" />
		      <_Dump Include="@(MauiProcessedFont->'MauiProcessedFont%09%(Identity)%09%09')" />
		      <_Dump Include="@(MauiProcessedAsset->'MauiProcessedAsset%09%(Identity)%09%(Link)%09%(AssetRole)')" />
		      <_Dump Include="@(MauiImage->'MauiImage%09%(Identity)%09%09')" />
		      <_Dump Include="@(MauiPlatformSpecificFolder->'MauiPlatformSpecificFolder%09%(Identity)%09%(TargetPlatformIdentifiers)%09')" />
		    </ItemGroup>
		    <WriteLinesToFile File="$(MSBuildProjectDirectory)/dump-items.txt" Lines="@(_Dump)" Overwrite="true" WriteOnlyWhenDifferent="false" />
		    <ItemGroup>
		      <_DumpProperty Include="_ResizetizerIsCompatibleApp=$(_ResizetizerIsCompatibleApp)" />
		      <_DumpProperty Include="_ResizetizerIsTizenApp=$(_ResizetizerIsTizenApp)" />
		      <_DumpProperty Include="ResizetizerPlatformType=$(ResizetizerPlatformType)" />
		      <_DumpProperty Include="MauiTizenUseBuiltInResizetizerSupport=$(MauiTizenUseBuiltInResizetizerSupport)" />
		      <_DumpProperty Include="TizenManifestFile=$(TizenManifestFile)" />
		      <_DumpProperty Include="MauiTizenIntermediateOutputPath=$(MauiTizenIntermediateOutputPath)" />
		      <_DumpProperty Include="_MauiTizenBuildTasksAssembly=$(_MauiTizenBuildTasksAssembly)" />
		    </ItemGroup>
		    <WriteLinesToFile File="$(MSBuildProjectDirectory)/dump-properties.txt" Lines="@(_DumpProperty)" Overwrite="true" WriteOnlyWhenDifferent="false" />
		  </Target>
		""";

	public BuildResult Build(params string[] extraArguments)
	{
		var arguments = new List<string> { "build", ProjectPath, "-v:n", "--nologo" };

		arguments.AddRange(extraArguments);

		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = ProjectDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		// Keep the build hermetic, quiet, and off the machine-wide MSBuild node/server pool.
		foreach (var isolation in TestBase.ConfigureIsolatedMSBuild(startInfo))
			startInfo.ArgumentList.Add(isolation);

		if (!string.IsNullOrEmpty(PackagesFolder))
			startInfo.Environment["NUGET_PACKAGES"] = PackagesFolder;

		using var process = Process.Start(startInfo)!;
		var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();

		return new BuildResult
		{
			Success = process.ExitCode == 0,
			Output = output,
			Items = ReadItems(Path.Combine(ProjectDirectory, "dump-items.txt")),
			Properties = ReadProperties(Path.Combine(ProjectDirectory, "dump-properties.txt")),
		};
	}

	private static IReadOnlyList<DumpedItem> ReadItems(string path)
	{
		if (!File.Exists(path))
			return Array.Empty<DumpedItem>();

		return File.ReadAllLines(path)
			.Where(l => !string.IsNullOrWhiteSpace(l))
			.Select(l => l.Split('\t'))
			.Where(p => p.Length >= 3)
			.Select(p => new DumpedItem(p[0], p[1], p[2], p.Length > 3 ? p[3] : string.Empty))
			.ToList();
	}

	private static IReadOnlyDictionary<string, string> ReadProperties(string path)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);

		if (!File.Exists(path))
			return result;

		foreach (var line in File.ReadAllLines(path))
		{
			var index = line.IndexOf('=');
			if (index > 0)
				result[line.Substring(0, index)] = line.Substring(index + 1);
		}

		return result;
	}
}
