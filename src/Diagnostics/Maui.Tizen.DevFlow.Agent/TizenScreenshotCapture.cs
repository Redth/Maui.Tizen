using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.DevFlow.Agent.Core;
using global::Tizen.NUI;
using IOPath = System.IO.Path;
using NUIColor = global::Tizen.NUI.Color;
using NUISize = global::Tizen.NUI.Size;
using NUIView = global::Tizen.NUI.BaseComponents.View;
using NUIWindow = global::Tizen.NUI.Window;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Screenshot capture backed by <see cref="Capture"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Capture"/> is file-based and asynchronous: <see cref="Capture.Start(Container, NUISize, string, NUIColor)"/>
/// writes a PNG and then raises <see cref="Capture.Finished"/> with
/// <see cref="CaptureFinishedEventArgs.Success"/>. There is no in-memory overload that yields
/// encoded bytes, so the flow is necessarily capture to a temp file, read, delete.
/// </para>
/// <para>
/// A <see cref="Capture"/> instance is created per request rather than shared. It is a stateful
/// native object with a single <c>Finished</c> event, so a shared instance would deliver one
/// request's completion to another's handler under concurrent capture.
/// </para>
/// <para>
/// The file goes in the application's own data directory, not <c>/tmp</c>: a sandboxed Tizen
/// application is not guaranteed write access elsewhere, and a capture that silently fails to write
/// is indistinguishable from one that produced nothing.
/// </para>
/// </remarks>
public sealed class TizenScreenshotCapture : IDisposable
{
    /// <summary>
    /// Upper bound on a single capture. A wedged GL pipeline never raises
    /// <see cref="Capture.Finished"/>, which would otherwise hang the agent's HTTP request forever.
    /// </summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    readonly TizenAgentEnvironment _environment;
    bool _disposed;

    public TizenScreenshotCapture(TizenAgentEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>Captures a whole window, or null when capture is unavailable.</summary>
    public async Task<byte[]?> CaptureWindowAsync(int? windowIndex)
    {
        if (!CanCapture)
            return null;

        var target = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = ResolveWindow(windowIndex);

            return window is null
                ? default((Container? Source, NUISize? Size))
                : (window.GetDefaultLayer(), new NUISize(window.WindowSize.Width, window.WindowSize.Height));
        }).ConfigureAwait(false);

        return target.Source is null || target.Size is null
            ? null
            : await CaptureAsync(target.Source, target.Size).ConfigureAwait(false);
    }

    /// <summary>Captures a single native element, or null when capture is unavailable.</summary>
    public async Task<byte[]?> CaptureElementAsync(object nativeElement, ElementInfo? elementInfo)
    {
        if (!CanCapture || nativeElement is not NUIView view)
            return null;

        var size = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var current = view.CurrentSize;
            return current.Width <= 0 || current.Height <= 0
                ? null
                : new NUISize(current.Width, current.Height);
        }).ConfigureAwait(false);

        return size is null ? null : await CaptureAsync(view, size).ConfigureAwait(false);
    }

    bool CanCapture => !_disposed && _environment is { HasWindow: true, SupportsCapture: true };

    /// <remarks>
    /// NUI exposes no index-based window lookup, so any index other than the default would resolve
    /// to the same window. Returning it anyway would silently capture the wrong surface, so a
    /// non-default index is rejected instead.
    /// </remarks>
    static NUIWindow? ResolveWindow(int? windowIndex) =>
        windowIndex is null or 0 ? NUIWindow.Default : null;

    async Task<byte[]?> CaptureAsync(Container source, NUISize size)
    {
        var path = IOPath.Combine(
            TizenDeviceEnvironment.GetAppDataPath(),
            $"devflow-capture-{Guid.NewGuid():N}.png");

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new Capture();

        void OnFinished(object? sender, CaptureFinishedEventArgs e) => completion.TrySetResult(e.Success);

        capture.Finished += OnFinished;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                capture.Start(source, size, path, NUIColor.Transparent)).ConfigureAwait(false);

            var completed = await Task.WhenAny(completion.Task, Task.Delay(CaptureTimeout)).ConfigureAwait(false);
            if (completed != completion.Task || !await completion.Task.ConfigureAwait(false))
                return null;

            return File.Exists(path) ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
        }
        finally
        {
            capture.Finished -= OnFinished;
            capture.Dispose();
            TryDelete(path);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leaked temp capture must never fail the request that produced it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _disposed = true;
}
