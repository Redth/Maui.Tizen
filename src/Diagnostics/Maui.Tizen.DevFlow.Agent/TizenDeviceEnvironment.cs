using global::Tizen.System;
using Information = global::Tizen.System.Information;
using NUIWindow = global::Tizen.NUI.Window;
using PackageManager = global::Tizen.Applications.PackageManager;
using TizenApplication = global::Tizen.Applications.Application;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Reads the device facts the agent's capability decisions depend on.
/// </summary>
/// <remarks>
/// The only place that touches Tizen device APIs for environment discovery. Everything that
/// consumes the result works against the platform-neutral <see cref="TizenAgentEnvironment"/>,
/// which is what allows the capability policy to be tested on a hosted runner.
/// </remarks>
public static class TizenDeviceEnvironment
{
    const string ProfileFeatureKey = "http://tizen.org/feature/profile";
    const string ScreenFeatureKey = "http://tizen.org/feature/screen";

    /// <summary>
    /// Probes the current device.
    /// </summary>
    /// <remarks>
    /// Window-dependent facts are read through <see cref="TizenAgentEnvironment"/>'s live probes
    /// rather than captured here, because the agent can start before the first window exists. See
    /// <see cref="TizenAgentEnvironment.WindowProbe"/>.
    /// </remarks>
    public static TizenAgentEnvironment Detect()
    {
        var profile = DetectProfile();

        return new TizenAgentEnvironment
        {
            Profile = profile,
            GrantedPrivileges = DetectPrivileges(),

            // Evaluated on every read, so a capability that becomes available once the window is
            // created is not reported as permanently unsupported.
            WindowProbe = HasWindow,
            CaptureProbe = () => HasWindow() && HasScreen(),

            // TV windows are locked to the panel resolution by the window manager.
            WindowResizeProbe = () => HasWindow() && profile != TizenDeviceProfiles.Tv,
        };
    }

    /// <summary>The application's private data directory.</summary>
    public static string GetAppDataPath() => TizenApplication.Current.DirectoryInfo.Data;

    /// <summary>Window size and display density.</summary>
    public static (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex)
    {
        var window = TryGetWindow();
        if (window is null)
            return (0, 0, 1);

        var size = window.WindowSize;

        // DevFlow coordinates are logical while NUI reports physical pixels. Getting this wrong
        // makes every coordinate-based tap land in the wrong place on non-mdpi devices.
        return (size.Width, size.Height, global::Tizen.NUI.GraphicsTypeManager.Instance.Density);
    }

    static string DetectProfile()
    {
        if (!Information.TryGetValue<string>(ProfileFeatureKey, out var profile) || string.IsNullOrEmpty(profile))
            return TizenDeviceProfiles.Mobile;

        return profile.ToLowerInvariant() switch
        {
            "tv" => TizenDeviceProfiles.Tv,
            "wearable" => TizenDeviceProfiles.Wearable,
            _ => TizenDeviceProfiles.Mobile,
        };
    }

    static bool HasScreen() =>
        !Information.TryGetValue<bool>(ScreenFeatureKey, out var hasScreen) || hasScreen;

    static bool HasWindow() => TryGetWindow() is not null;

    /// <summary>
    /// Privileges the application actually holds.
    /// </summary>
    /// <remarks>
    /// Read from the installed package rather than through <c>PrivacyPrivilegeManager</c>, which is
    /// <c>[Obsolete]</c> in API15 and would fail the build under warnings-as-errors. It is also the
    /// more accurate source here: <c>inputgenerator</c> is an install-time privilege, so what
    /// matters is whether the package was installed with it declared, which is exactly what
    /// <see cref="Package.Privileges"/> reports.
    /// </remarks>
    static IReadOnlyCollection<string> DetectPrivileges()
    {
        try
        {
            var applicationId = TizenApplication.Current.ApplicationInfo.ApplicationId;
            var packageId = PackageManager.GetPackageIdByApplicationId(applicationId);

            if (string.IsNullOrEmpty(packageId))
                return [];

            var package = PackageManager.GetPackage(packageId);
            return package?.Privileges is { } privileges ? [.. privileges] : [];
        }
        catch (InvalidOperationException)
        {
            // Package metadata is unavailable outside a packaged app, e.g. under a test host.
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    static NUIWindow? TryGetWindow()
    {
        try
        {
            return NUIWindow.Default;
        }
        catch (InvalidOperationException)
        {
            // The agent can start before the first window exists.
            return null;
        }
    }
}
