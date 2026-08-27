using Xunit;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Central place for dynamic test skips.
/// </summary>
/// <remarks>
/// Every skip in this repository must carry a reason that names (a) what was missing and
/// (b) which lane is responsible for covering it. Silent or reason-free skips are how a
/// validation suite quietly stops validating anything.
/// </remarks>
public static class ValidationSkip
{
    /// <summary>Skips the current test with the supplied reason.</summary>
    public static void Because(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        Assert.Skip(reason);
    }

    /// <summary>Skips the current test when <paramref name="condition"/> is <see langword="true"/>.</summary>
    public static void When(bool condition, string reason)
    {
        if (condition)
            Because(reason);
    }

    /// <summary>
    /// Skips when a repository path that a later PR in the stack is expected to create is absent.
    /// </summary>
    /// <param name="absolutePath">Path that must exist for the test to be meaningful.</param>
    /// <param name="owner">The PR/branch expected to introduce it, e.g. "core vertical slice".</param>
    public static void WhenPathMissing(string absolutePath, string owner)
    {
        if (Directory.Exists(absolutePath) || File.Exists(absolutePath))
            return;

        Because(
            $"'{RepoLayout.Relative(absolutePath)}' does not exist yet. It is introduced by the " +
            $"{owner}; this suite becomes active automatically once that lands.");
    }

    /// <summary>Skips when the Tizen workload lane is not available on this machine.</summary>
    public static void WhenNoTizenWorkload()
    {
        if (TizenWorkload.IsAvailable)
            return;

        Because(
            "The Samsung Tizen workload is not installed on this runner. Tizen target-framework " +
            "builds, TPK creation and deploy/run are covered by the device lane " +
            "(.github/workflows/tizen-device-validation.yml).");
    }
}
