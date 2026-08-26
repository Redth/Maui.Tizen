using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.DevFlow.Agent.Core;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Screenshot capture backed by <see cref="Capture"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Capture"/> is file-based and asynchronous: it writes a PNG to a path and raises
/// <see cref="Capture.Finished"/>. There is no in-memory overload, so the flow is necessarily
/// capture to a temp file under the app's own data directory, read the bytes, delete the file.
/// </para>
/// <para>
/// The app data directory is used rather than <c>/tmp</c> because a sandboxed Tizen application is
/// not guaranteed write access outside its own storage, and a capture that silently fails to write
/// is indistinguishable from a capture that produced nothing.
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
    public Task<byte[]?> CaptureWindowAsync(int? windowIndex)
    {
        if (!CanCapture)
            return Task.FromResult<byte[]?>(null);

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = ResolveWindow(windowIndex);
            return window is null
                ? Task.FromResult<byte[]?>(null)
                : CaptureAsync(window.GetDefaultLayer(), new Size2D((int)window.WindowSize.Width, (int)window.WindowSize.Height));
        }).Unwrap();
    }

    /// <summary>Captures a single native element, or null when capture is unavailable.</summary>
    public Task<byte[]?> CaptureElementAsync(object nativeElement, ElementInfo elementInfo)
    {
        if (!CanCapture || nativeElement is not View view)
            return Task.FromResult<byte[]?>(null);

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var size = view.CurrentSize;
            return size.Width <= 0 || size.Height <= 0
                ? Task.FromResult<byte[]?>(null)
                : CaptureAsync(view, new Size2D((int)size.Width, (int)size.Height));
        }).Unwrap();
    }

    bool CanCapture => !_disposed && _environment is { HasWindow: true, SupportsCapture: true };

    static Window? ResolveWindow(int? windowIndex) =>
        windowIndex is null or 0 ? Window.Instance : Window.Instance;

    async Task<byte[]?> CaptureAsync(View source, Size2D size)
    {
        var path = Path.Combine(
            TizenDeviceEnvironment.GetAppDataPath(),
            $"devflow-capture-{Guid.NewGuid():N}.png");

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFinished(object? sender, Capture.FinishedEventArgs e) =>
            completion.TrySetResult(e.Success);

        Capture.Instance.Finished += OnFinished;

        try
        {
            Capture.Instance.Start(source, size, path, Color.Transparent);

            var completed = await Task.WhenAny(completion.Task, Task.Delay(CaptureTimeout)).ConfigureAwait(false);
            if (completed != completion.Task || !await completion.Task.ConfigureAwait(false))
                return null;

            return File.Exists(path) ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
        }
        finally
        {
            Capture.Instance.Finished -= OnFinished;
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
