using System.Diagnostics;
using System.Text;

namespace Maui.Tizen.TestUtils;

/// <summary>Result of a <c>dotnet</c> invocation.</summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
/// <param name="CommandLine">The command line that produced this result, for failure messages.</param>
public sealed record CliResult(int ExitCode, string StandardOutput, string StandardError, string CommandLine)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>All output, interleaved for diagnostics.</summary>
    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;

    /// <summary>Throws with the full transcript when the command failed.</summary>
    public CliResult EnsureSucceeded()
    {
        if (Succeeded)
            return this;

        throw new InvalidOperationException(
            $"Command failed with exit code {ExitCode}.{Environment.NewLine}" +
            $"  {CommandLine}{Environment.NewLine}" +
            $"--- stdout ---{Environment.NewLine}{StandardOutput}{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{StandardError}");
    }

    /// <summary>True when stdout or stderr contains <paramref name="value"/>.</summary>
    public bool OutputContains(string value) =>
        CombinedOutput.Contains(value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs the <c>dotnet</c> CLI in a hermetic way so build/restore/pack assertions are reproducible
/// between a developer machine and CI.
/// </summary>
public static class DotNetCli
{
    /// <summary>
    /// Environment variables applied to every invocation. These remove the ambient state that most
    /// often makes MSBuild assertions flaky: telemetry banners, the first-run experience, locale
    /// dependent message text, and inherited NuGet/MSBuild configuration from the outer build.
    /// </summary>
    static readonly (string Key, string? Value)[] HermeticEnvironment =
    [
        ("DOTNET_CLI_TELEMETRY_OPTOUT", "1"),
        ("DOTNET_NOLOGO", "1"),
        ("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"),
        ("DOTNET_CLI_UI_LANGUAGE", "en"),
        ("MSBUILDDISABLENODEREUSE", "1"),
        ("MSBUILDTERMINALLOGGER", "off"),
        // Prevents the outer `dotnet test` run from leaking its own build context into the inner build.
        ("MSBuildSDKsPath", null),
        ("MSBuildExtensionsPath", null),
        ("MSBuildLoadMicrosoftTargetsReadOnly", null),
        ("MSBuildProjectExtensionsPath", null),
    ];

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(10);

    public static Task<CliResult> RestoreAsync(string projectOrSolution, params string[] extraArgs) =>
        RunAsync(["restore", projectOrSolution, .. extraArgs]);

    public static Task<CliResult> BuildAsync(string projectOrSolution, params string[] extraArgs) =>
        RunAsync(["build", projectOrSolution, "--nologo", .. extraArgs]);

    public static Task<CliResult> PackAsync(string project, string outputDirectory, params string[] extraArgs) =>
        RunAsync(["pack", project, "--nologo", "--output", outputDirectory, .. extraArgs]);

    /// <summary>Runs <c>dotnet</c> with <paramref name="arguments"/> and captures all output.</summary>
    public static async Task<CliResult> RunAsync(
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var args = arguments.ToList();

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory ?? RepoLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in HermeticEnvironment)
        {
            if (value is null)
                psi.Environment.Remove(key);
            else
                psi.Environment[key] = value;
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                    psi.Environment.Remove(key);
                else
                    psi.Environment[key] = value;
            }
        }

        var commandLine = "dotnet " + string.Join(' ', args.Select(Quote));

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException(
                $"'{commandLine}' did not complete within {(timeout ?? DefaultTimeout).TotalMinutes:0.#} minutes." +
                $"{Environment.NewLine}--- partial stdout ---{Environment.NewLine}{stdout}");
        }

        return new CliResult(process.ExitCode, stdout.ToString(), stderr.ToString(), commandLine);
    }

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
    }

    static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
