namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Tizen privilege URIs the DevFlow agent cares about.
/// </summary>
/// <remarks>
/// Privileges are declared in <c>tizen-manifest.xml</c> and granted at install time. Querying them
/// at runtime rather than assuming them is what keeps the agent honest: a capability that is
/// advertised but unusable produces a hang or an opaque native failure at the moment a test tries to
/// drive the UI, which is the worst possible time to discover it.
/// </remarks>
public static class TizenPrivileges
{
    /// <summary>
    /// Required to synthesise native input events (<c>efl_util_input</c>). Without it the agent must
    /// fall back to framework-level interaction and must not advertise native input.
    /// </summary>
    public const string InputGenerator = "http://tizen.org/privilege/inputgenerator";

    /// <summary>Required to bind the agent's TCP listener.</summary>
    public const string Internet = "http://tizen.org/privilege/internet";

    /// <summary>Required for window manipulation such as resize.</summary>
    public const string Display = "http://tizen.org/privilege/display";

    /// <summary>Privileges the agent declares by default.</summary>
    public static IReadOnlyList<string> Default { get; } = [Internet, Display];

    /// <summary>Privileges that unlock optional capabilities when additionally granted.</summary>
    public static IReadOnlyList<string> Optional { get; } = [InputGenerator];
}

/// <summary>
/// What the agent observed about the device it is running on.
/// </summary>
/// <remarks>
/// Represented as plain data with no Tizen types so the capability decisions that depend on it can
/// be executed and asserted on a hosted runner.
/// </remarks>
public sealed class TizenAgentEnvironment
{
    readonly bool _hasWindow = true;
    readonly bool _supportsCapture = true;
    readonly bool _supportsWindowResize = true;

    /// <summary>Device profile, e.g. <c>mobile</c> or <c>tv</c>.</summary>
    public string Profile { get; init; } = TizenDeviceProfiles.Mobile;

    /// <summary>Privileges actually granted to the host application.</summary>
    public IReadOnlyCollection<string> GrantedPrivileges { get; init; } = [];

    /// <summary>
    /// Live probe for window availability, evaluated on every read.
    /// </summary>
    /// <remarks>
    /// The agent starts before the application's first window exists, so a value captured at
    /// construction would report every window-dependent capability as permanently unsupported for
    /// the lifetime of the process. A driver connecting after the UI appeared would be told the
    /// agent cannot walk the tree, which is both wrong and unrecoverable.
    /// </remarks>
    public Func<bool>? WindowProbe { get; init; }

    /// <summary>Live probe for screenshot capture availability.</summary>
    public Func<bool>? CaptureProbe { get; init; }

    /// <summary>Live probe for programmatic window resize.</summary>
    public Func<bool>? WindowResizeProbe { get; init; }

    /// <summary>True when a NUI window is available to capture and drive.</summary>
    public bool HasWindow
    {
        get => WindowProbe?.Invoke() ?? _hasWindow;
        init => _hasWindow = value;
    }

    /// <summary>
    /// True when <c>Tizen.NUI.Capture</c> is usable. Emulator images without a GL backend can fail
    /// capture while everything else works.
    /// </summary>
    public bool SupportsCapture
    {
        get => CaptureProbe?.Invoke() ?? _supportsCapture;
        init => _supportsCapture = value;
    }

    /// <summary>True when the window manager permits programmatic resize.</summary>
    public bool SupportsWindowResize
    {
        get => WindowResizeProbe?.Invoke() ?? _supportsWindowResize;
        init => _supportsWindowResize = value;
    }

    public bool HasPrivilege(string privilege) =>
        GrantedPrivileges.Contains(privilege, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Computes the capability map published at <c>GET /api/v1/agent/capabilities</c>.
/// </summary>
/// <remarks>
/// DevFlow's contract is that unsupported capabilities report <c>supported: false</c> and their
/// endpoints answer HTTP 501 with a <c>not_supported</c> payload. Deriving both from one place means
/// the advertised map and the runtime behaviour cannot disagree.
/// </remarks>
public static class TizenAgentCapabilityPolicy
{
    /// <summary>Capability keys used by the DevFlow HTTP surface.</summary>
    public static class Keys
    {
        public const string UiTree = "ui.tree";
        public const string UiQuery = "ui.query";
        public const string UiHitTest = "ui.hit-test";
        public const string Screenshot = "ui.screenshot";
        public const string Tap = "ui.tap";
        public const string Fill = "ui.fill";
        public const string Scroll = "ui.scroll";
        public const string Focus = "ui.focus";
        public const string Key = "ui.key";
        public const string Resize = "ui.resize";
        public const string NativeInput = "ui.native-input";
        public const string Storage = "storage";
        public const string Theme = "device.theme";
    }

    /// <summary>Evaluates every capability for <paramref name="environment"/>.</summary>
    public static IReadOnlyDictionary<string, TizenCapability> Compute(TizenAgentEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var map = new Dictionary<string, TizenCapability>(StringComparer.Ordinal);

        void Add(string key, bool supported, string? unsupportedReason = null) =>
            map[key] = new TizenCapability(key, supported, supported ? null : unsupportedReason);

        // Tree walking and querying only need a live window.
        Add(Keys.UiTree, environment.HasWindow, "No NUI window is attached yet.");
        Add(Keys.UiQuery, environment.HasWindow, "No NUI window is attached yet.");
        Add(Keys.UiHitTest, environment.HasWindow, "No NUI window is attached yet.");

        Add(
            Keys.Screenshot,
            environment is { HasWindow: true, SupportsCapture: true },
            "Tizen.NUI.Capture is unavailable on this image; emulator images without a GL backend cannot capture.");

        // Framework-level interaction goes through MAUI/NUI directly and needs no extra privilege.
        Add(Keys.Tap, environment.HasWindow, "No NUI window is attached yet.");
        Add(Keys.Fill, environment.HasWindow, "No NUI window is attached yet.");
        Add(Keys.Scroll, environment.HasWindow, "No NUI window is attached yet.");
        Add(Keys.Focus, environment.HasWindow, "No NUI window is attached yet.");

        // Synthesised input is privileged; advertising it without the privilege produces silent no-ops.
        var nativeInput = environment.HasWindow && environment.HasPrivilege(TizenPrivileges.InputGenerator);
        Add(
            Keys.NativeInput,
            nativeInput,
            $"The '{TizenPrivileges.InputGenerator}' privilege is not granted, so native input events " +
            "cannot be synthesised. Framework-level tap and fill remain available.");

        // Key delivery is synthesised through InputGenerator exactly like touch, so it needs the
        // same privilege. Advertising it on window presence alone was wrong: the endpoint would be
        // reported as supported and then do nothing, which surfaces to a driver as a test that
        // pressed a key and observed no reaction - far harder to diagnose than a clean 501.
        Add(
            Keys.Key,
            nativeInput,
            $"Key injection is synthesised through InputGenerator and requires the " +
            $"'{TizenPrivileges.InputGenerator}' privilege, which is not granted.");

        Add(
            Keys.Resize,
            environment is { HasWindow: true, SupportsWindowResize: true },
            "The window manager does not permit programmatic resize on this profile; TV windows are " +
            "fixed to the panel resolution.");

        Add(Keys.Storage, true);

        // Tizen exposes no per-application theme override equivalent to the other platforms.
        Add(Keys.Theme, false, "Tizen does not expose a per-application light/dark override.");

        return map;
    }

    /// <summary>Shapes the capability map the way DevFlow's status endpoint expects.</summary>
    public static Dictionary<string, object> ToPayload(IReadOnlyDictionary<string, TizenCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var payload = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (key, capability) in capabilities.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            payload[key] = capability.Supported
                ? new Dictionary<string, object> { ["supported"] = true }
                : new Dictionary<string, object>
                {
                    ["supported"] = false,
                    ["reason"] = capability.UnsupportedReason ?? "Not supported on Tizen.",
                };
        }

        return payload;
    }
}

/// <param name="UnsupportedReason">
/// Human-readable cause, echoed in the HTTP 501 <c>not_supported</c> body. Null when supported.
/// </param>
public sealed record TizenCapability(string Key, bool Supported, string? UnsupportedReason);
