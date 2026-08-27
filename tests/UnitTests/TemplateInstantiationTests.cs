using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Installs the template package and instantiates it, then evaluates the generated project.
/// </summary>
/// <remarks>
/// Reading the template's files as text is not enough. Two defects in this template were invisible
/// that way and only appear once MSBuild evaluates the result:
///
///   * the Tizen package references were conditioned on <c>$(TargetPlatformIdentifier)</c>, which
///     the SDK does not set until Microsoft.NET.TargetFrameworkInference.targets is imported -
///     after the project body - so every reference was silently dropped;
///   * the application id was a fixed literal, so two applications created from the template
///     collided on a device.
///
/// The template is installed into an isolated <c>DOTNET_CLI_HOME</c> so the developer's real
/// template catalog is never touched.
/// </remarks>
[Trait("Category", "Template")]
public class TemplateInstantiationTests : TestBase
{
	private sealed record Instantiation(string ProjectDirectory, string ProjectPath);

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
	/// Installs the template from source and creates a project named <paramref name="projectName"/>.
	/// </summary>
	private Instantiation Instantiate(string projectName, params string[] extraArguments)
	{
		var cliHome = CreateTempDirectory("maui-tizen-cli-home");
		var output = CreateTempDirectory("maui-tizen-template-out");

		var templateRoot = Path.Combine(RepositoryRoot, "src", "Maui.Tizen.Templates", "templates");

		RunDotnet(cliHome, output, "new", "install", templateRoot);

		var arguments = new List<string> { "new", "maui-tizen", "--name", projectName, "--output", projectName, "--skipRestore" };
		arguments.AddRange(extraArguments);

		RunDotnet(cliHome, output, arguments.ToArray());

		var projectDirectory = Path.Combine(output, projectName);
		var projectPath = Path.Combine(projectDirectory, projectName + ".csproj");

		Assert.True(File.Exists(projectPath), $"The template did not produce '{projectPath}'.");

		return new Instantiation(projectDirectory, projectPath);
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

	[Fact]
	public void GeneratedProjectKeepsTheTizenPackageReferences()
	{
		var instantiation = Instantiate("ContosoTizenApp");

		var references = EvaluateItems(instantiation.ProjectPath, "PackageReference");

		// If the platform condition regresses, these silently disappear and the app builds without
		// the backend at all.
		Assert.Contains("Maui.Tizen", references);
		Assert.Contains("Maui.Tizen.Build.Tasks", references);
		Assert.Contains("Samsung.Tizen.Ref.API15", references);
		Assert.Contains("Microsoft.Maui.Controls", references);
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

		Assert.Equal("com.companyname.contosotizenapp1", applicationId);
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
