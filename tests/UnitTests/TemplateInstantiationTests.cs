using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Packs the template package, installs the produced nupkg, and instantiates it.
/// </summary>
/// <remarks>
/// <para>
/// Reading the template's files as text is not enough. Three defects in this template were
/// invisible that way and only appear once the package is built, installed and instantiated:
/// </para>
/// <list type="bullet">
///   <item>the Tizen package references were conditioned on <c>$(TargetPlatformIdentifier)</c>,
///     which the SDK does not set until Microsoft.NET.TargetFrameworkInference.targets is
///     imported - after the project body - so every reference was silently dropped;</item>
///   <item>the application id was a fixed literal, so two applications created from the template
///     collided on a device;</item>
///   <item><c>MauiProgram.cs</c> guarded the Tizen host-builder call with <c>#if TIZEN</c>. The
///     Template Engine reads preprocessor directives in template content as TEMPLATE
///     conditionals, so it evaluated that as false and emitted the else-branch: every generated
///     application called <c>UseMauiApp</c> instead of <c>UseMauiAppTizen</c>, with the Tizen
///     hosting <c>using</c> stripped out. The repository file read correctly the whole time.</item>
/// </list>
/// <para>
/// Installation is from the PRODUCED NUPKG rather than from the template source folder. Installing
/// a folder skips packing entirely, so it cannot see a package whose directory tree was flattened
/// - which would install cleanly and then emit package internals instead of a project.
/// </para>
/// <para>
/// The package is installed into an isolated <c>DOTNET_CLI_HOME</c>, so the template hive is
/// per-test and the developer's real template catalog is never touched.
/// </para>
/// </remarks>
[Trait("Category", "Template")]
public class TemplateInstantiationTests : TestBase
{
	private sealed record Instantiation(string ProjectDirectory, string ProjectPath, string Root);

	/// <summary>
	/// Runs dotnet and returns its combined output.
	/// </summary>
	/// <remarks>
	/// stdout and stderr are drained concurrently. Reading one to the end before touching the
	/// other deadlocks as soon as the untouched pipe's buffer fills, which is easy to hit with
	/// MSBuild's JSON output and presents as a test that simply never returns.
	/// </remarks>
	private static string RunDotnet(string cliHome, string workingDirectory, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		startInfo.Environment["DOTNET_CLI_HOME"] = cliHome;
		ConfigureIsolatedMSBuild(startInfo);

		var (exitCode, standardOutput, standardError) = Run(startInfo);

		Assert.True(
			exitCode == 0,
			$"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{standardOutput}{standardError}");

		return standardOutput + standardError;
	}

	private static (int ExitCode, string StandardOutput, string StandardError) Run(ProcessStartInfo startInfo)
	{
		using var process = Process.Start(startInfo)!;

		var standardOutput = process.StandardOutput.ReadToEndAsync();
		var standardError = process.StandardError.ReadToEndAsync();

		// A hang here should fail the test rather than stall the whole run.
		if (!process.WaitForExit(milliseconds: 10 * 60 * 1000))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException)
			{
			}

			throw new TimeoutException("dotnet did not exit within ten minutes.");
		}

		return (process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
	}

	/// <summary>
	/// Installs the template from the produced nupkg and creates a project named
	/// <paramref name="projectName"/>.
	/// </summary>
	private Instantiation Instantiate(string projectName, params string[] extraArguments)
	{
		var cliHome = CreateTempDirectory("maui-tizen-cli-home");
		var output = CreateTempDirectory("maui-tizen-template-out");

		RunDotnet(cliHome, output, "new", "install", ProducedPackages.PathOf("Maui.Tizen.Templates"));

		var arguments = new List<string> { "new", "maui-tizen", "--name", projectName, "--output", projectName, "--skipRestore" };
		arguments.AddRange(extraArguments);

		RunDotnet(cliHome, output, arguments.ToArray());

		var projectDirectory = Path.Combine(output, projectName);
		var projectPath = Path.Combine(projectDirectory, projectName + ".csproj");

		Assert.True(File.Exists(projectPath), $"The template did not produce '{projectPath}'.");

		return new Instantiation(projectDirectory, projectPath, output);
	}

	/// <summary>
	/// Evaluates the generated project and returns the requested items. Evaluation is what proves
	/// the conditions actually hold; the SDK is asked for the item directly rather than building,
	/// because a Tizen target framework cannot be restored without the Samsung workload.
	/// </summary>
	private static IReadOnlyList<string> EvaluateItems(string projectPath, string itemName)
	{
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = Path.GetDirectoryName(projectPath)!,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in new[]
		{
			"msbuild",
			projectPath,
			"-getItem:" + itemName,
			// The workload is absent, so stop the SDK from failing on the unknown platform before
			// the evaluation result can be reported.
			"-p:MauiTizenSkipHostNativeValidation=true",
			"-nologo",
		})
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var isolation in ConfigureIsolatedMSBuild(startInfo))
			startInfo.ArgumentList.Add(isolation);

		var (exitCode, output, _) = Run(startInfo);

		if (exitCode != 0)
			return Array.Empty<string>();

		var identities = new List<string>();

		using var document = System.Text.Json.JsonDocument.Parse(output);
		if (document.RootElement.TryGetProperty("Items", out var items)
			&& items.TryGetProperty(itemName, out var entries))
		{
			foreach (var entry in entries.EnumerateArray())
			{
				if (entry.TryGetProperty("Identity", out var identity))
					identities.Add(identity.GetString() ?? string.Empty);
			}
		}

		return identities;
	}

	private static string ReadProperty(string projectFile, string name)
	{
		var match = System.Text.RegularExpressions.Regex.Match(projectFile, $"<{name}>([^<]*)</{name}>");
		return match.Success ? match.Groups[1].Value : string.Empty;
	}

	// =====================================================================================
	// The emitted sources
	// =====================================================================================

	/// <summary>
	/// The generated application must register the Tizen backend.
	/// </summary>
	/// <remarks>
	/// This asserts on the INSTANTIATED file, not the template file. The template file said
	/// <c>UseMauiAppTizenControls</c> the whole time; the Template Engine consumed the surrounding
	/// <c>#if TIZEN</c> as a template conditional, evaluated it false because TIZEN is not a
	/// template symbol, and emitted the else-branch instead. Only running the generator can see
	/// that.
	/// </remarks>
	[Fact]
	public void GeneratedApplicationUsesTheTizenHostBuilderEntryPoint()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var mauiProgram = File.ReadAllText(Path.Combine(instantiation.ProjectDirectory, "MauiProgram.cs"));

		Assert.Contains("using Microsoft.Maui.Platforms.Tizen.Hosting;", mauiProgram);
		Assert.Contains(".UseMauiAppTizenControls<App>()", mauiProgram);

		// UseMauiApp is the MAUI Controls entry point and does not register this backend.
		Assert.DoesNotContain(".UseMauiApp<App>()", mauiProgram);

		// The nonexistent umbrella namespace the stripped branch used to import.
		Assert.DoesNotContain("using Maui.Tizen;", mauiProgram);
	}

	/// <summary>
	/// The Tizen entry point must derive from this backend's application class.
	/// </summary>
	/// <remarks>
	/// <c>Microsoft.Maui.MauiApplication</c> still exists in the net11.0-tizen build of
	/// Microsoft.Maui.dll, so deriving from it is a CS0433 hazard and does not run this backend's
	/// lifecycle. <c>TizenMauiApplication</c> is the non-colliding equivalent this repository owns.
	/// </remarks>
	[Fact]
	public void GeneratedApplicationDerivesFromTizenMauiApplication()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var main = File.ReadAllText(Path.Combine(instantiation.ProjectDirectory, "Platforms", "Tizen", "Main.cs"));

		Assert.Contains("using Microsoft.Maui.Platforms.Tizen;", main);
		Assert.Contains(": TizenMauiApplication", main);
		Assert.DoesNotContain(": MauiApplication", main);
	}

	/// <summary>
	/// No emitted C# file may contain preprocessor directives.
	/// </summary>
	/// <remarks>
	/// A surviving directive means the Template Engine did NOT treat it as a template conditional
	/// - which is the fragile half of the same coin. Either way, template content must not carry
	/// them: the engine's behaviour here is what silently rewrote the generated host builder, and
	/// a commented-out example is enough to make `dotnet new` fail outright with
	/// "Index was out of range" after writing a partial project.
	/// </remarks>
	[Fact]
	public void GeneratedSourcesContainNoPreprocessorDirectives()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		foreach (var file in Directory.GetFiles(instantiation.ProjectDirectory, "*.cs", SearchOption.AllDirectories))
		{
			foreach (var line in File.ReadAllLines(file))
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal))
					trimmed = trimmed.Substring(2).TrimStart();

				Assert.False(
					trimmed.StartsWith("#if", StringComparison.Ordinal)
						|| trimmed.StartsWith("#else", StringComparison.Ordinal)
						|| trimmed.StartsWith("#endif", StringComparison.Ordinal),
					$"'{Path.GetRelativePath(instantiation.ProjectDirectory, file)}' contains a preprocessor "
						+ $"directive that the Template Engine parses as a template conditional: {line.Trim()}");
			}
		}
	}

	/// <summary>
	/// Every font the generated application registers must exist in the generated project.
	/// </summary>
	/// <remarks>
	/// The template registered <c>OpenSans-Regular.ttf</c> while shipping only an instructions
	/// file in Resources/Fonts. A missing font is not a build error - it fails at runtime, when
	/// the font loader cannot resolve the name - so nothing in the pipeline noticed.
	/// </remarks>
	[Fact]
	public void EveryRegisteredFontExistsInTheGeneratedProject()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var mauiProgram = File.ReadAllText(Path.Combine(instantiation.ProjectDirectory, "MauiProgram.cs"));

		var fontDirectory = Path.Combine(instantiation.ProjectDirectory, "Resources", "Fonts");

		foreach (System.Text.RegularExpressions.Match match in
			System.Text.RegularExpressions.Regex.Matches(mauiProgram, @"^\s*fonts\.AddFont\(""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Multiline))
		{
			var fontFile = match.Groups[1].Value;

			Assert.True(
				File.Exists(Path.Combine(fontDirectory, fontFile)),
				$"MauiProgram.cs registers '{fontFile}' but Resources/Fonts does not contain it.");
		}
	}

	// =====================================================================================
	// Package references
	// =====================================================================================

	private sealed record Reference(string Id, string Version);

	private static IReadOnlyList<Reference> ReadPackageReferences(string projectPath)
		=> System.Text.RegularExpressions.Regex
			.Matches(File.ReadAllText(projectPath), @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""")
			.Select(m => new Reference(m.Groups[1].Value, m.Groups[2].Value))
			.ToList();

	/// <summary>
	/// The TizenFX reference pack must never be a PackageReference.
	/// </summary>
	/// <remarks>
	/// Samsung.Tizen.Ref.API15 carries the <c>DotnetPlatform</c> package type. Referencing it
	/// directly fails restore with NU1213, so the template used to emit a project that could not
	/// restore even WITH the Samsung workload installed - the one configuration it exists to
	/// serve. The workload resolves the reference pack from the tizen platform version in
	/// $(TargetFramework); nothing needs to reference it.
	/// </remarks>
	[Fact]
	public void GeneratedProjectNeverReferencesTheTizenReferencePackAsAPackage()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		Assert.DoesNotContain(
			ReadPackageReferences(instantiation.ProjectPath),
			r => r.Id.StartsWith("Samsung.Tizen.Ref", StringComparison.OrdinalIgnoreCase));

		Assert.DoesNotContain("Samsung.Tizen.Ref.API15\" Version", File.ReadAllText(instantiation.ProjectPath));
	}

	/// <summary>
	/// Every repository-owned package the template references must be a package this repository
	/// actually declares.
	/// </summary>
	/// <remarks>
	/// The template referenced <c>Maui.Tizen</c>, an umbrella package ID that no project in this
	/// repository declares and that has never been published under any name. Because the whole
	/// project is behind the workload gate, restore never got far enough for anyone to notice.
	/// Package IDs here follow the project names, so the check is that a matching project exists.
	/// </remarks>
	[Fact]
	public void EveryRepositoryOwnedReferenceIsAPackageThisRepositoryDeclares()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		var owned = ReadPackageReferences(instantiation.ProjectPath)
			.Where(r => r.Id.StartsWith("Maui.Tizen", StringComparison.Ordinal))
			.ToList();

		Assert.NotEmpty(owned);

		foreach (var reference in owned)
		{
			var project = Path.Combine(RepositoryRoot, "src", reference.Id, reference.Id + ".csproj");

			Assert.True(
				File.Exists(project),
				$"The template references '{reference.Id}', but no project declares that package ID. "
					+ $"Expected '{Path.GetRelativePath(RepositoryRoot, project)}'.");

			Assert.Equal(PackageVersion, reference.Version);
		}
	}

	/// <summary>
	/// Third-party references must use their own version line, and one that resolves.
	/// </summary>
	/// <remarks>
	/// The template pinned <c>Microsoft.Maui.Controls</c> to the Maui.Tizen version
	/// (<c>11.0.0-alpha</c>). No Microsoft.Maui package exists at a Maui.Tizen build number, so
	/// the generated project could never have restored - a second failure hidden behind the
	/// workload gate. The resolvable half is proven by
	/// <see cref="ExternalReferencesRestoreFromTheApprovedFeeds"/>.
	/// </remarks>
	[Fact]
	public void ThirdPartyReferencesDoNotUseTheBackendVersionLine()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		var external = ReadPackageReferences(instantiation.ProjectPath)
			.Where(r => !r.Id.StartsWith("Maui.Tizen", StringComparison.Ordinal))
			.ToList();

		Assert.NotEmpty(external);
		Assert.All(external, r => Assert.NotEqual(PackageVersion, r.Version));

		// Pinned to the same coherent MAUI package set the repository builds against.
		foreach (var id in new[] { "Microsoft.Maui.Controls", "Microsoft.Maui.Resizetizer" })
		{
			var maui = Assert.Single(external, reference => reference.Id == id);
			Assert.Equal(MauiPackageVersion, maui.Version);
		}
	}

	// =====================================================================================
	// Restore
	// =====================================================================================

	/// <summary>
	/// Writes a NuGet configuration that offers the repository's approved feeds plus the packages
	/// this test run produced.
	/// </summary>
	private static void WriteRestoreConfiguration(string directory)
		=> File.WriteAllText(Path.Combine(directory, "NuGet.config"), ReadNuGetConfigWithProducedPackages());

	private static (int ExitCode, string Output) TryRestore(string projectPath, string packagesFolder, params string[] extraArguments)
	{
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = Path.GetDirectoryName(projectPath)!,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		startInfo.ArgumentList.Add("restore");
		startInfo.ArgumentList.Add(projectPath);
		startInfo.ArgumentList.Add("--nologo");

		foreach (var argument in extraArguments)
			startInfo.ArgumentList.Add(argument);

		foreach (var isolation in ConfigureIsolatedMSBuild(startInfo))
			startInfo.ArgumentList.Add(isolation);

		// A cold cache. This is the whole point: a warm global packages folder hides every
		// resolution defect, because the package is already on disk regardless of whether any
		// configured feed still carries it.
		startInfo.Environment["NUGET_PACKAGES"] = packagesFolder;

		var (exitCode, standardOutput, standardError) = Run(startInfo);

		return (exitCode, standardOutput + standardError);
	}

	/// <summary>
	/// Restoring the generated project must fail on the WORKLOAD GATE and on nothing else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is as far as a generated project can be taken before Samsung publishes an
	/// 11.0.100-band manifest: <c>net11.0-tizen11.0</c> is not a recognized target platform
	/// without it. Depending on which workload manifests the SDK already carries, this is reported
	/// either as an unrecognized Tizen platform or as a missing Tizen workload. It must never ask for
	/// Microsoft's empty <c>maui-tizen</c> alias.
	/// </para>
	/// <para>
	/// What the test adds is the NEGATIVE half: the failure must not be, or be accompanied by, a
	/// package resolution error. A generated project that also cannot find its packages would be
	/// a repository defect wearing the gate's clothes - and it was exactly that, twice, with a
	/// nonexistent <c>Maui.Tizen</c> package and a Microsoft.Maui.Controls version that has never
	/// existed. The restore runs cold, against the approved feeds plus this run's produced
	/// packages, so nothing is masked by a warm cache.
	/// </para>
	/// </remarks>
	[Fact]
	public void RestoringTheGeneratedProjectFailsOnlyOnTheWorkloadGate()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		WriteRestoreConfiguration(instantiation.Root);

		var packages = CreateTempDirectory("maui-tizen-cold-packages");
		var (exitCode, output) = TryRestore(instantiation.ProjectPath, packages);

		Assert.True(
			exitCode != 0,
			"Restore of a net11.0-tizen11.0 project succeeded. If the Samsung workload has shipped, "
				+ "promote the Tizen CI lane and revisit this test; see docs/migration.md."
				+ Environment.NewLine + output);

		var isTizenGate =
			output.Contains("NETSDK1139", StringComparison.Ordinal)
			|| output.Contains("MAUITIZEN0001", StringComparison.Ordinal)
			|| (output.Contains("NETSDK1147", StringComparison.Ordinal)
				&& output.Contains("tizen", StringComparison.OrdinalIgnoreCase)
				&& !output.Contains("maui-tizen", StringComparison.OrdinalIgnoreCase));
		Assert.True(
			isTizenGate,
			"Restore did not fail on the Samsung Tizen platform/workload gate."
				+ Environment.NewLine + output);
		Assert.DoesNotMatch(
			new System.Text.RegularExpressions.Regex(
				@"following workloads? must be installed:[^\r\n]*\bmaui-tizen\b",
				System.Text.RegularExpressions.RegexOptions.IgnoreCase),
			output);

		// And no package resolution failure of any kind.
		foreach (var code in new[] { "NU1101", "NU1102", "NU1103", "NU1202", "NU1213" })
			Assert.DoesNotContain(code, output, StringComparison.Ordinal);
	}

	/// <summary>
	/// Every reference the template emits that is NOT gated behind the Tizen platform must resolve
	/// cold, from the approved feeds plus this run's produced packages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The gate test above proves the generated project fails for the right reason, but it can
	/// prove nothing about the package IDs and versions, because the SDK rejects the target
	/// platform before NuGet ever runs. This test closes that hole: it takes the references the
	/// template actually emitted and restores them from a neutral net11.0 project, where NuGet
	/// does run.
	/// </para>
	/// <para>
	/// The backend packages are excluded, and deliberately so rather than quietly: they cannot be
	/// packed at all while the workload gate stands, so demanding that they resolve would be
	/// demanding that the external gate be lifted. That exclusion is itself asserted - each
	/// excluded ID must be a project in this repository that is currently unpackable - so a
	/// fabricated ID cannot hide inside it.
	/// </para>
	/// </remarks>
	[Fact]
	public void ExternalReferencesRestoreFromTheApprovedFeeds()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var references = ReadPackageReferences(instantiation.ProjectPath);

		// Backend packages that the workload gate keeps unpublished, verified to be real projects.
		var gated = references
			.Where(r => r.Id.StartsWith("Maui.Tizen", StringComparison.Ordinal))
			.Where(r => !File.Exists(Path.Combine(ProducedPackages.Directory, $"{r.Id}.{r.Version}.nupkg")))
			.ToList();

		foreach (var reference in gated)
		{
			var project = File.ReadAllText(Path.Combine(RepositoryRoot, "src", reference.Id, reference.Id + ".csproj"));

			Assert.True(
				project.Contains("TizenPackage.props", StringComparison.Ordinal),
				$"'{reference.Id}' is excluded from restore as workload-gated, but its project does not "
					+ "import eng/targets/TizenPackage.props, so it is not actually gated.");
		}

		var restorable = references.Except(gated).ToList();
		Assert.NotEmpty(restorable);
		Assert.Contains(restorable, r => r.Id == "Microsoft.Maui.Controls");
		Assert.Contains(restorable, r => r.Id == "Microsoft.Maui.Resizetizer");
		Assert.Contains(restorable, r => r.Id == "Maui.Tizen.Build.Tasks");

		var probeRoot = CreateTempDirectory("maui-tizen-reference-probe");
		WriteRestoreConfiguration(probeRoot);
		File.WriteAllText(Path.Combine(probeRoot, "Directory.Build.props"), "<Project />");
		File.WriteAllText(Path.Combine(probeRoot, "Directory.Build.targets"), "<Project />");

		var probe = Path.Combine(probeRoot, "ReferenceProbe.csproj");
		File.WriteAllText(probe, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0</TargetFramework>
			    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
			    <NoWarn>$(NoWarn);NETSDK1057</NoWarn>
			  </PropertyGroup>
			  <ItemGroup>
			{string.Join(Environment.NewLine, restorable.Select(r => $"""    <PackageReference Include="{r.Id}" Version="{r.Version}" />"""))}
			  </ItemGroup>
			</Project>
			""");

		var packages = CreateTempDirectory("maui-tizen-reference-packages");
		var (exitCode, output) = TryRestore(probe, packages);

		Assert.True(
			exitCode == 0,
			"The packages the template emits do not resolve from the approved feeds:"
				+ Environment.NewLine + output);

		// Resolving is not enough: NuGet SUBSTITUTES a nearby version and only warns. The
		// template's Microsoft.Maui.Controls version was 11.0.0-alpha - this repository's own
		// version line, on which no Microsoft.Maui package has ever existed - and restore
		// "succeeded" with NU1603 after silently picking an unrelated build. The pinned version
		// has to be the version that is actually used.
		foreach (var code in new[] { "NU1602", "NU1603" })
		{
			Assert.False(
				output.Contains(code, StringComparison.Ordinal),
				$"A referenced package version does not exist and was substituted ({code}):"
					+ Environment.NewLine + output);
		}
	}

	/// <summary>
	/// The end-to-end contract: the produced nupkg installs into an isolated template hive and
	/// instantiates into a PROJECT, not into the package's own contents.
	/// </summary>
	/// <remarks>
	/// This is the assertion that a source-folder install cannot make. `dotnet new install` on a
	/// folder never exercises packing, so a package whose directory tree was flattened - which is
	/// what happens when the packed layout is left to be inferred rather than declared - installs
	/// cleanly and then emits package internals into the user's directory. The engine reports
	/// success either way, so the only way to see it is to look at what was written.
	/// </remarks>
	[Fact]
	public void TheProducedNupkgInstallsAndCreatesAProjectRatherThanPackageInternals()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		var files = Directory
			.GetFiles(instantiation.ProjectDirectory, "*", SearchOption.AllDirectories)
			.Select(f => Path.GetRelativePath(instantiation.ProjectDirectory, f).Replace('\\', '/'))
			.OrderBy(f => f, StringComparer.Ordinal)
			.ToList();

		// A project at the root, named after the project rather than after the template.
		Assert.Contains("ContosoTizenApp.csproj", files);
		Assert.DoesNotContain("MauiTizenApp.csproj", files);

		// The whole authored tree, at the depth it was authored at.
		Assert.Contains("MauiProgram.cs", files);
		Assert.Contains("Platforms/Tizen/Main.cs", files);
		Assert.Contains("Resources/AppIcon/appicon.svg", files);

		// And none of the package's own scaffolding.
		Assert.DoesNotContain(files, f => f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(files, f => f.StartsWith("_rels/", StringComparison.Ordinal));
		Assert.DoesNotContain(files, f => f.StartsWith("package/", StringComparison.Ordinal));
		Assert.DoesNotContain(files, f => f.StartsWith("content/", StringComparison.Ordinal));
		Assert.DoesNotContain(files, f => f.StartsWith("templates/", StringComparison.Ordinal));
		Assert.DoesNotContain(files, f => f.Contains(".template.config", StringComparison.Ordinal));
		Assert.DoesNotContain("[Content_Types].xml", files);
		Assert.DoesNotContain("README.md", files);
	}

	[Fact]
	public void GeneratedProjectKeepsTheTizenPackageReferences()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		var references = EvaluateItems(instantiation.ProjectPath, "PackageReference");

		// If the platform condition regresses, these silently disappear and the app builds without
		// the backend at all.
		Assert.Contains("Maui.Tizen.Core", references);
		Assert.Contains("Maui.Tizen.Controls", references);
		Assert.Contains("Maui.Tizen.Build.Tasks", references);
		Assert.Contains("Microsoft.Maui.Controls", references);
		Assert.Contains("Microsoft.Maui.Resizetizer", references);
	}

	[Fact]
	public void GeneratedProjectUsesPackageBasedMauiBuildSupport()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var project = File.ReadAllText(instantiation.ProjectPath);

		Assert.Equal(string.Empty, ReadProperty(project, "UseMaui"));
		Assert.Equal(string.Empty, ReadProperty(project, "SingleProject"));
		Assert.Equal("true", ReadProperty(project, "UseMauiTizen"));
	}

	[Fact]
	public void GeneratedProjectTargetsTheVersionedTizenFramework()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var projectFile = File.ReadAllText(instantiation.ProjectPath);

		Assert.Equal("net11.0-tizen11.0", ReadProperty(projectFile, "TargetFramework"));
	}

	[Fact]
	public void ApplicationIdIsDerivedFromTheProjectName()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var projectFile = File.ReadAllText(instantiation.ProjectPath);

		Assert.Equal("com.companyname.contosotizenapp", ReadProperty(projectFile, "ApplicationId"));
	}

	/// <summary>
	/// Two applications from the same template must not share a package id.
	/// </summary>
	[Fact]
	public void TwoGeneratedApplicationsDoNotShareAnApplicationId()
	{
		var first = File.ReadAllText(Instantiate("ContosoOne").ProjectPath);
		var second = File.ReadAllText(Instantiate("ContosoTwo").ProjectPath);

		var firstId = ReadProperty(first, "ApplicationId");
		var secondId = ReadProperty(second, "ApplicationId");

		Assert.NotEqual(firstId, secondId);
		Assert.Equal("com.companyname.contosoone", firstId);
		Assert.Equal("com.companyname.contosotwo", secondId);
	}

	[Fact]
	public void PunctuationThatIsValidInAnApplicationIdRemainsDistinct()
	{
		var dotted = File.ReadAllText(Instantiate("Contoso.Tizen").ProjectPath);
		var unseparated = File.ReadAllText(Instantiate("ContosoTizen").ProjectPath);

		var dottedId = ReadProperty(dotted, "ApplicationId");
		var unseparatedId = ReadProperty(unseparated, "ApplicationId");

		Assert.Equal("com.companyname.contoso.tizen", dottedId);
		Assert.Equal("com.companyname.contosotizen", unseparatedId);
		Assert.NotEqual(dottedId, unseparatedId);
	}

	[Fact]
	public void ApplicationIdCanBeOverridden()
	{
		var instantiation = Instantiate("ContosoTizenApp", "--ApplicationId", "org.tizen.example.custom");
		var projectFile = File.ReadAllText(instantiation.ProjectPath);

		Assert.Equal("org.tizen.example.custom", ReadProperty(projectFile, "ApplicationId"));
	}

	/// <summary>
	/// Characters Tizen does not accept in a package id must be stripped rather than emitted.
	/// </summary>
	[Fact]
	public void ApplicationIdIsSanitized()
	{
		var instantiation = Instantiate("Contoso.Tizen-App_1");
		var projectFile = File.ReadAllText(instantiation.ProjectPath);

		var applicationId = ReadProperty(projectFile, "ApplicationId");

		Assert.Equal("com.companyname.contoso.tizenapp1", applicationId);
		Assert.Matches("^[a-zA-Z0-9.]+$", applicationId);
	}

	/// <summary>
	/// The manifest carries the same id, otherwise packaging and the generated manifest disagree.
	/// </summary>
	[Fact]
	public void ManifestUsesTheSameApplicationIdPlaceholderContract()
	{
		var instantiation = Instantiate("ContosoTizenApp");
		var manifest = File.ReadAllText(Path.Combine(instantiation.ProjectDirectory, "Platforms", "Tizen", "tizen-manifest.xml"));

		// The manifest keeps the generator placeholders; GenerateTizenManifest fills them from
		// $(ApplicationId) at build time, so the id must NOT be baked in here.
		Assert.Contains("maui-application-id-placeholder", manifest);
		Assert.DoesNotContain("com.companyname", manifest);
	}
}
