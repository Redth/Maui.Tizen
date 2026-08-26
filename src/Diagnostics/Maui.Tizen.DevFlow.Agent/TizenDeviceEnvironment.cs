using Tizen.Applications;
using Tizen.NUI;
using Tizen.Security;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Reads the device facts the agent's capability decisions depend on.
/// </summary>
/// <remarks>
/// This is the only place that touches Tizen device APIs for environment discovery. Everything that
/// consumes the result works against the platform-neutral <see cref="TizenAgentEnvironment"/>, which
/// is what allows the capability policy to be tested on a hosted runner.
/// </remarks>
public static class TizenDeviceEnvironment
{
    const string ProfileFeatureKey = "http://tizen.org/feature/profile";
    const string ScreenFeatureKey = "http://tizen.org/feature/screen";

    /// <summary>Probes the current device.</summary>
    public static TizenAgentEnvironment Detect()
    {
        var profile = DetectProfile();

        return new TizenAgentEnvironment
        {
            Profile = profile,
            GrantedPrivileges = DetectPrivileges(),
            HasWindow = TryGetWindow() is not null,
            SupportsCapture = HasScreen(),

            // TV windows are locked to the panel resolution by the window manager.
            SupportsWindowResize = profile != TizenDeviceProfiles.Tv,
        };
    }

    /// <summary>The application's private data directory.</summary>
    public static string GetAppDataPath() => Application.Current.DirectoryInfo.Data;

    /// <summary>Window size and display density.</summary>
    public static (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex)
    {
        var window = TryGetWindow();
        if (window is null)
            return (0, 0, 1);

        var size = window.WindowSize;
        return (size.Width, size.Height, GraphicsTypeManager.Instance.ScaleFactor);
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

    /// <summary>
    /// Returns only privileges actually granted.
    /// </summary>
    /// <remarks>
    /// Declaring a privilege in <c>tizen-manifest.xml</c> is not the same as holding it: privacy
    /// privileges can be denied by the user at runtime. Checking the granted state is what keeps the
    /// advertised capability map truthful.
    /// </remarks>
    static IReadOnlyCollection<string> DetectPrivileges()
    {
        var granted = new List<string>();

        foreach (var privilege in TizenPrivileges.Default.Concat(TizenPrivileges.Optional))
        {
            if (IsGranted(privilege))
                granted.Add(privilege);
        }

        return granted;
    }

    static bool IsGranted(string privilege)
    {
        try
        {
            return PrivacyPrivilegeManager.CheckPermission(privilege) == CheckResult.Allow;
        }
        catch (ArgumentException)
        {
            // Non-privacy privileges are not known to PrivacyPrivilegeManager. They are granted at
            // install time if declared, so treat a lookup failure as granted rather than hiding a
            // capability that actually works.
            return true;
        }
    }

    static Window? TryGetWindow()
    {
        try
        {
            return Window.Instance;
        }
        catch (InvalidOperationException)
        {
            // The agent can start before the first window exists.
            return null;
        }
    }
}
