using System.Diagnostics;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Behavioural coverage of <c>eng/check-package-inputs-clean.sh</c>, the gate that decides whether
/// the workload-free lane may claim that the packages it produced came from HEAD.
/// </summary>
/// <remarks>
/// <para>
/// The lane stamps every package with a <c>&lt;repository commit="..."&gt;</c> and then asserts
/// it. That assertion is a claim about which sources a binary came from, and it was previously
/// made unconditionally - so an uncommitted edit under <c>src/Maui.Tizen.Build.Tasks</c> produced
/// a package built from the working tree and labelled with a commit that does not contain it. A
/// package that points confidently at the wrong sources is worse than an unstamped one, because
/// nothing about it looks wrong.
/// </para>
/// <para>
/// The decision lives in its own script rather than inline in the lane precisely so it can be
/// executed here against purpose-built repositories, instead of being asserted by reading the
/// lane's text. Each test below builds a real git repository, puts it in a specific state, and
/// runs the real script.
/// </para>
/// </remarks>
[Trait("Category", "Provenance")]
public class PackageInputCleanlinessTests : TestBase
{
	private static string ScriptPath => Path.Combine(RepositoryRoot, "eng", "check-package-inputs-clean.sh");

	/// <summary>Exit codes the script contracts on. 'cannot tell' is deliberately not 'dirty'.</summary>
	private const int Clean = 0;
	private const int Dirty = 1;
	private const int Indeterminate = 2;

	private sealed record ScriptResult(int ExitCode, string Output);

	private static ScriptResult RunCheck(string repository)
	{
		var startInfo = new ProcessStartInfo("bash")
		{
			WorkingDirectory = repository,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		startInfo.ArgumentList.Add(ScriptPath);
		startInfo.ArgumentList.Add(repository);

		using var process = Process.Start(startInfo)!;
		var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();

		return new ScriptResult(process.ExitCode, output);
	}

	private static void Git(string repository, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo("git")
		{
			WorkingDirectory = repository,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		// Never read the developer's own git identity or hooks: the fixture must behave the same
		// on every machine and must not run anything the machine has configured.
		startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
		startInfo.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
		startInfo.Environment["GIT_AUTHOR_NAME"] = "Maui.Tizen Tests";
		startInfo.Environment["GIT_AUTHOR_EMAIL"] = "tests@example.invalid";
		startInfo.Environment["GIT_COMMITTER_NAME"] = "Maui.Tizen Tests";
		startInfo.Environment["GIT_COMMITTER_EMAIL"] = "tests@example.invalid";

		using var process = Process.Start(startInfo)!;
		var log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();

		Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed:{Environment.NewLine}{log}");
	}

	private static void Write(string repository, string relativePath, string contents)
	{
		var path = Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
	}

	/// <summary>
	/// A repository shaped like this one: package inputs in the scoped locations, plus content
	/// that is deliberately out of scope.
	/// </summary>
	private string CreateCommittedFixture()
	{
		var repository = CreateTempDirectory("maui-tizen-clean-fixture");

		Write(repository, ".gitignore", "artifacts/\n");
		Write(repository, "README.md", "# fixture\n");
		Write(repository, "Directory.Build.props", "<Project />\n");
		Write(repository, "Directory.Build.targets", "<Project />\n");
		Write(repository, "Directory.Packages.props", "<Project />\n");
		Write(repository, "global.json", "{}\n");
		Write(repository, "nuget.config", "<configuration />\n");
		Write(repository, "eng/Maui.props", "<Project />\n");
		Write(repository, "eng/targets/TizenPackage.props", "<Project />\n");
		Write(repository, "src/Maui.Tizen.Build.Tasks/Task.cs", "// task\n");
		Write(repository, "src/Maui.Tizen.Templates/templates/maui-tizen/template.json", "{}\n");
		Write(repository, "eng/tests/PackReadmeProbe/Probe.cs", "// probe\n");

		// Out of scope: documentation and tests cannot change a package's bytes.
		Write(repository, "docs/migration.md", "# migration\n");
		Write(repository, "tests/UnitTests/Some.cs", "// test\n");

		Git(repository, "init", "-q", "-b", "main");
		Git(repository, "add", "-A");
		Git(repository, "commit", "-q", "-m", "fixture");

		return repository;
	}

	[Fact]
	public void ACommittedTreePasses()
	{
		var repository = CreateCommittedFixture();

		var result = RunCheck(repository);

		Assert.True(result.ExitCode == Clean, $"Expected a clean verdict, got {result.ExitCode}:{Environment.NewLine}{result.Output}");
	}

	/// <summary>
	/// A modified tracked package input must block the provenance claim.
	/// </summary>
	[Fact]
	public void AModifiedTrackedPackageInputIsRejected()
	{
		var repository = CreateCommittedFixture();

		Write(repository, "src/Maui.Tizen.Build.Tasks/Task.cs", "// task, edited\n");

		var result = RunCheck(repository);

		Assert.Equal(Dirty, result.ExitCode);
		Assert.Contains("src/Maui.Tizen.Build.Tasks/Task.cs", result.Output);
	}

	/// <summary>
	/// A NEW file that was never committed must block it too.
	/// </summary>
	/// <remarks>
	/// An untracked source file changes the compiled task exactly as much as an edit to a tracked
	/// one, and it is the case a `git diff` based check misses entirely.
	/// </remarks>
	[Fact]
	public void AnUntrackedPackageInputIsRejected()
	{
		var repository = CreateCommittedFixture();

		Write(repository, "src/Maui.Tizen.Templates/templates/maui-tizen/NewFile.cs", "// new\n");

		var result = RunCheck(repository);

		Assert.Equal(Dirty, result.ExitCode);
		Assert.Contains("NewFile.cs", result.Output);
	}

	/// <summary>A staged but uncommitted change is not committed, so it is rejected.</summary>
	[Fact]
	public void AStagedPackageInputIsRejected()
	{
		var repository = CreateCommittedFixture();

		Write(repository, "eng/tests/PackReadmeProbe/Probe.cs", "// probe, edited\n");
		Git(repository, "add", "eng/tests/PackReadmeProbe/Probe.cs");

		Assert.Equal(Dirty, RunCheck(repository).ExitCode);
	}

	/// <summary>
	/// Build output must never make the check fail.
	/// </summary>
	/// <remarks>
	/// A fail-closed check that fires on the artifacts the lane itself produces would be
	/// unusable - it would fail on its own second run - and people would reach for the override
	/// permanently, which is how a gate stops being one.
	/// </remarks>
	[Fact]
	public void GeneratedArtifactsDoNotFailTheCheck()
	{
		var repository = CreateCommittedFixture();

		Write(repository, "artifacts/packages/workload-free/Maui.Tizen.Build.Tasks.11.0.0-alpha.nupkg", "not really a package");
		Write(repository, "artifacts/bin/Maui.Tizen.Build.Tasks/Release/netstandard2.0/Maui.Tizen.Build.Tasks.dll", "not really an assembly");

		var result = RunCheck(repository);

		Assert.True(result.ExitCode == Clean, $"Generated output was treated as a dirty package input:{Environment.NewLine}{result.Output}");
	}

	/// <summary>
	/// Editing something that cannot change a package must not block the claim.
	/// </summary>
	[Fact]
	public void ChangesOutsideThePackageInputsDoNotFailTheCheck()
	{
		var repository = CreateCommittedFixture();

		Write(repository, "docs/migration.md", "# migration, edited\n");
		Write(repository, "tests/UnitTests/Some.cs", "// test, edited\n");
		Write(repository, "tests/UnitTests/New.cs", "// new test\n");

		var result = RunCheck(repository);

		Assert.True(result.ExitCode == Clean, $"An out-of-scope edit blocked the provenance claim:{Environment.NewLine}{result.Output}");
	}

	/// <summary>
	/// "Cannot tell" must be reported distinctly from "dirty".
	/// </summary>
	/// <remarks>
	/// The container lane has no git metadata at all, and collapsing that into "dirty" would
	/// either block every clean container run or, if collapsed the other way, accept an
	/// unverified one. The distinct exit code is what lets the caller pass a revision verified on
	/// the host instead.
	/// </remarks>
	[Fact]
	public void ADirectoryWithoutGitMetadataIsIndeterminateRatherThanDirty()
	{
		var directory = CreateTempDirectory("maui-tizen-no-git");
		Write(directory, "src/Maui.Tizen.Build.Tasks/Task.cs", "// task\n");

		var result = RunCheck(directory);

		Assert.Equal(Indeterminate, result.ExitCode);
		Assert.Contains("cannot be determined", result.Output);
	}
}
