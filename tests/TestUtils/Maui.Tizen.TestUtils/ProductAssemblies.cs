using System.Reflection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Locates the built Tizen backend assemblies so convention tests can bind to the real product
/// when it is available, and skip with a precise reason when it is not.
/// </summary>
/// <remarks>
/// <para>
/// The Tizen backend is compiled for a Tizen target framework and links against Tizen.NET. It can
/// only be <em>loaded and executed</em> on a Tizen host, so the convention suites that need live
/// mapper dictionaries (handler property/command parity) bind to the device lane. On a hosted
/// runner the same suites skip with an explicit reason rather than silently passing.
/// </para>
/// <para>
/// This deliberately does not attempt an in-process <c>Assembly.Load</c> of a Tizen-targeted
/// assembly on a non-Tizen host. Doing so appears to work until a type initializer touches a
/// Tizen native binding, at which point failures surface as unrelated
/// <see cref="TypeInitializationException"/>s. See docs/validation/hosted-lane.md.
/// </para>
/// </remarks>
public static class ProductAssemblies
{
    /// <summary>
    /// True when the current process is running on a Tizen host and can therefore execute the
    /// Tizen backend in-process.
    /// </summary>
    public static bool RunningOnTizen { get; } = DetectTizen();

    /// <summary>
    /// Attempts to load a product assembly in the current process.
    /// </summary>
    /// <returns><see langword="null"/> when the assembly is not present or not loadable here.</returns>
    public static Assembly? TryLoad(string simpleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(simpleName);

        var already = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.Ordinal));

        if (already is not null)
            return already;

        try
        {
            return Assembly.Load(new AssemblyName(simpleName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a product assembly or skips the calling test with a reason that states exactly what
    /// is missing and which lane is expected to cover it.
    /// </summary>
    public static Assembly LoadOrSkip(string simpleName)
    {
        var assembly = TryLoad(simpleName);
        if (assembly is not null)
            return assembly;

        ValidationSkip.Because(
            $"'{simpleName}' is not loadable in this process. Live-mapper and live-DI conventions " +
            $"require the Tizen backend to be executing on a Tizen host, so they run in the device " +
            $"lane (see docs/validation/device-lane.md). RunningOnTizen={RunningOnTizen}.");

        throw new UnreachableException();
    }

    static bool DetectTizen()
    {
        if (OperatingSystem.IsLinux() && Directory.Exists("/etc/tizen-release"))
            return true;

        if (File.Exists("/etc/tizen-release"))
            return true;

        // The Tizen TFM sets this via the runtime identifier graph.
        return RuntimeInformationHelper.RuntimeIdentifierContains("tizen");
    }
}

static class RuntimeInformationHelper
{
    internal static bool RuntimeIdentifierContains(string fragment) =>
        (AppContext.GetData("RUNTIME_IDENTIFIER") as string ?? string.Empty)
            .Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

sealed class UnreachableException() : InvalidOperationException("Unreachable.");
