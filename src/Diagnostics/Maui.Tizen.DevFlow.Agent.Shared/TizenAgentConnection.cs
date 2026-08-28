namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Describes how an external DevFlow driver reaches the agent running on a Tizen target.
/// </summary>
/// <remarks>
/// <para>
/// The first version deliberately uses a <em>fixed</em> port plus an <c>sdb forward</c> tunnel
/// rather than dynamic port negotiation or mDNS discovery. A device is reached through <c>sdb</c>
/// anyway, the tunnel is what makes emulator and physical device identical from the driver's point
/// of view, and a fixed port keeps the CI job's teardown deterministic.
/// </para>
/// <para>
/// The trade-off is that only one agent per host port can be forwarded at a time. That is acceptable
/// while the device lane runs one target at a time, and it is why <see cref="HostPort"/> is
/// configurable: a future parallel lane allocates a distinct host port per target while the
/// device-side port stays fixed.
/// </para>
/// </remarks>
public sealed class TizenAgentConnection
{
    /// <summary>DevFlow's default agent port, matching the published spec.</summary>
    public const int DefaultDevFlowPort = 9223;

    public TizenAgentConnection(string? deviceSerial = null, int devicePort = DefaultDevFlowPort, int? hostPort = null)
    {
        if (devicePort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(devicePort), devicePort, "Port must be in 1-65535.");

        if (hostPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(hostPort), hostPort, "Port must be in 1-65535.");

        DeviceSerial = deviceSerial;
        DevicePort = devicePort;
        HostPort = hostPort ?? devicePort;
    }

    /// <summary>Target's <c>sdb</c> serial, or null to use the only attached target.</summary>
    public string? DeviceSerial { get; }

    /// <summary>Port the agent listens on inside the app.</summary>
    public int DevicePort { get; }

    /// <summary>Port the tunnel is exposed on for the driver.</summary>
    public int HostPort { get; }

    /// <summary>Base URL a DevFlow driver connects to.</summary>
    public Uri BaseAddress => new($"http://127.0.0.1:{HostPort}/");

    /// <summary>Arguments for establishing the tunnel: <c>sdb [-s serial] forward tcp:h tcp:d</c>.</summary>
    public IReadOnlyList<string> BuildForwardArguments() =>
    [
        .. DeviceSerial is null ? Array.Empty<string>() : ["-s", DeviceSerial],
        "forward",
        $"tcp:{HostPort}",
        $"tcp:{DevicePort}",
    ];

    /// <summary>Arguments for tearing the tunnel down again.</summary>
    public IReadOnlyList<string> BuildForwardRemoveArguments() =>
    [
        .. DeviceSerial is null ? Array.Empty<string>() : ["-s", DeviceSerial],
        "forward",
        "--remove",
        $"tcp:{HostPort}",
    ];

    public override string ToString() =>
        $"sdb forward tcp:{HostPort} -> tcp:{DevicePort}" +
        (DeviceSerial is null ? string.Empty : $" on {DeviceSerial}");
}
