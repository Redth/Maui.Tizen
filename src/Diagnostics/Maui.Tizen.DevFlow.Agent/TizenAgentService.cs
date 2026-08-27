using Microsoft.Maui.DevFlow.Agent.Core;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Tizen backend for the DevFlow in-app agent.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the shape of the existing platform backends (<c>GtkAgentService</c>,
/// <c>WpfAgentService</c>): derive from <see cref="MauiDevFlowAgentService"/> and override only the
/// platform seams. All HTTP routing, CSS querying, element registries, epochs and error shaping stay
/// in DevFlow.
/// </para>
/// <para>
/// The overridden members here are exactly those pinned by
/// <c>Maui.Tizen.DevFlow.Tests.DevFlowContractTests</c>. If maui-labs changes a signature, the
/// hosted lane fails immediately rather than this project failing much later on a device.
/// </para>
/// </remarks>
public class TizenAgentService : MauiDevFlowAgentService
{
    /// <summary>Body shape accepted by <c>POST /api/v1/ui/actions/key</c>.</summary>
    sealed class TizenKeyRequest
    {
        public string? Key { get; set; }
    }

    readonly TizenAgentEnvironment _environment;
    readonly TizenPlatformIdentity _identity;
    readonly TizenScreenshotCapture _capture;
    readonly TizenNativeInput _nativeInput;

    public TizenAgentService(AgentOptions options)
        : this(options, TizenDeviceEnvironment.Detect())
    {
    }

    public TizenAgentService(AgentOptions options, TizenAgentEnvironment environment)
        : base(options)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _identity = new TizenPlatformIdentity(TizenPlatformReporting.Accurate, environment.Profile);
        _capture = new TizenScreenshotCapture(environment);
        _nativeInput = new TizenNativeInput(environment);
    }

    /// <summary>
    /// The capability map this agent advertises, recomputed on every read.
    /// </summary>
    /// <remarks>
    /// Deliberately not cached. Window-dependent capabilities change when the first window is
    /// created, and a map captured at construction would report the tree, screenshot and
    /// interaction endpoints as unsupported forever.
    /// </remarks>
    public IReadOnlyDictionary<string, TizenCapability> Capabilities =>
        TizenAgentCapabilityPolicy.Compute(_environment);

    protected override string PlatformName => TizenPlatformIdentity.TizenPlatformName;

    protected override string DeviceTypeName => _identity.Profile;

    protected override string IdiomName => _identity.Idiom;

    protected override VisualTreeWalker CreateTreeWalker() =>
        new TizenVisualTreeWalker(NativeElementDiagnosticsBridge.Current);

    protected override void PopulateCapabilities(Dictionary<string, object> capabilities)
    {
        base.PopulateCapabilities(capabilities);

        foreach (var (key, value) in TizenAgentCapabilityPolicy.ToPayload(Capabilities))
            capabilities[key] = value;

        foreach (var (key, value) in _identity.StatusExtensions)
            capabilities[key] = value;
    }

    /// <summary>
    /// NUI is strictly single-threaded: touching a <c>View</c> off the main loop either throws or
    /// corrupts state, so every UI operation is marshalled.
    /// </summary>
    protected override bool IsMainThreadDispatchRequired() => true;

    protected override string GetAppDataBasePath() => TizenDeviceEnvironment.GetAppDataPath();

    protected override Task<byte[]?> CaptureFullScreenAsync(int? windowIndex) =>
        _capture.CaptureWindowAsync(windowIndex);

    protected override Task<byte[]?> CaptureNativeElementScreenshotAsync(object nativeElement, ElementInfo? elementInfo) =>
        _capture.CaptureElementAsync(nativeElement, elementInfo);

    protected override ScreenshotCaptureFailure DescribeScreenshotFailure()
    {
        if (!_environment.HasWindow)
        {
            return new ScreenshotCaptureFailure(
                "No NUI window is attached yet.",
                PlatformErrorReasonInvalidRequest,
                retryable: true,
                ["Wait for the application's first window to be created before capturing."]);
        }

        if (!_environment.SupportsCapture)
        {
            return new ScreenshotCaptureFailure(
                "Tizen.NUI.Capture is unavailable on this image.",
                PlatformErrorReasonNotSupported,
                retryable: false,
                [
                    "Emulator images without a GL backend cannot capture; use a physical device.",
                    "Confirm the emulator was started with hardware acceleration enabled.",
                ]);
        }

        // DevFlow's preview assemblies are not nullable-annotated, so its declared non-null
        // returns are oblivious to the compiler. Trusting the documented contract here.
        return base.DescribeScreenshotFailure()!;
    }

    /// <summary>
    /// Reports window size and display density.
    /// </summary>
    /// <remarks>
    /// Density matters more here than on other platforms: DevFlow coordinates are logical, while
    /// NUI reports <c>ScreenPosition</c> and <c>CurrentSize</c> in physical pixels. Getting this
    /// wrong makes every coordinate-based tap land in the wrong place on non-mdpi devices.
    /// </remarks>
    protected override (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex) =>
        TizenDeviceEnvironment.GetWindowMetrics(windowIndex);

    /// <summary>
    /// Taps a registered native element, preferring synthesised input when the privilege allows it.
    /// </summary>
    /// <remarks>
    /// Synthesised input is attempted first because it is the only path that exercises real
    /// hit-testing; direct invocation would report success for an element hidden behind an overlay.
    /// </remarks>
    protected override async Task<string?> TryNativeElementTapAsync(string elementId, object nativeElement)
    {
        if (_nativeInput.SupportsSyntheticInput &&
            TreeWalkerBounds(nativeElement) is { Width: > 0, Height: > 0 } bounds)
        {
            var error = await _nativeInput
                .TryInjectTapAsync(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2))
                .ConfigureAwait(false);

            if (error is null)
                return NativeTapResult.Success;
        }

        var invocationError = await _nativeInput.TryInvokeAsync(nativeElement).ConfigureAwait(false);
        return NativeTapResult.FromError(invocationError);
    }

    static BoundsInfo? TreeWalkerBounds(object nativeElement) =>
        nativeElement is global::Tizen.NUI.BaseComponents.View view
            ? new BoundsInfo
            {
                X = view.ScreenPosition.X,
                Y = view.ScreenPosition.Y,
                Width = view.CurrentSize.Width,
                Height = view.CurrentSize.Height,
            }
            : null;

    /// <summary>
    /// Routes <c>POST /api/v1/ui/actions/key</c> to synthesised key injection.
    /// </summary>
    /// <remarks>
    /// Without this override the endpoint falls through to the framework default, which cannot
    /// deliver a real key event on Tizen. The TV remote focus harness depends on real key events:
    /// setting focus programmatically would validate the code that sets focus rather than the
    /// traversal order a remote actually produces.
    /// </remarks>
    protected override async Task<HttpResponse> HandleKey(HttpRequest request)
    {
        var key = ReadKeyName(request);

        if (string.IsNullOrWhiteSpace(key))
        {
            return CreatePlatformError(
                "A key name is required, e.g. {\"key\":\"Down\"}.",
                PlatformErrorReasonInvalidRequest,
                400,
                CreatePlatformErrorDetails());
        }

        if (!_nativeInput.SupportsSyntheticInput)
        {
            // 501 with a reason, matching how DevFlow reports any unsupported capability, and
            // consistent with the ui.key entry in the advertised capability map.
            return CreatePlatformError(
                $"Key injection requires the '{TizenPrivileges.InputGenerator}' privilege, which is " +
                "not granted on this device.",
                PlatformErrorReasonMissingPermission,
                501,
                CreatePlatformErrorDetails());
        }

        var error = await _nativeInput.TryInjectKeyAsync(key!).ConfigureAwait(false);

        return error is null
            ? HttpResponse.Json(new Dictionary<string, object> { ["ok"] = true, ["key"] = key! })
            : CreatePlatformError(error, PlatformErrorReasonUnknown, 500, CreatePlatformErrorDetails());
    }

    /// <summary>Accepts the key from the JSON body or, for convenience, the query string.</summary>
    static string? ReadKeyName(HttpRequest request)
    {
        if (request.QueryParams is not null &&
            request.QueryParams.TryGetValue("key", out var fromQuery) &&
            !string.IsNullOrWhiteSpace(fromQuery))
        {
            return fromQuery;
        }

        try
        {
            return request.BodyAs<TizenKeyRequest>()?.Key;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    protected override Task StopBackendAsync()    {
        NativeElementDiagnosticsBridge.Current.Clear();
        return base.StopBackendAsync();
    }

    protected override void DisposeBackendResources()
    {
        _capture.Dispose();
        base.DisposeBackendResources();
    }
}
