using Microsoft.Maui.ApplicationModel;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Interaction that goes through the platform rather than through MAUI.
/// </summary>
/// <remarks>
/// <para>
/// Two distinct mechanisms live here and they are not interchangeable:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Synthesised input</b> (<see cref="TryInjectTapAsync"/>) posts real touch events through the
///     window. It exercises the full input stack, so it is the only way to validate gesture
///     recognisers and hit-testing - and it requires the
///     <c>http://tizen.org/privilege/inputgenerator</c> privilege.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Direct invocation</b> (<see cref="TryInvokeAsync"/>) calls the widget's own API. It always
///     works, but it bypasses hit-testing entirely, so a control covered by an overlay still
///     "taps" successfully. It is the fallback, never the default.
///     </description>
///   </item>
/// </list>
/// <para>
/// The distinction is advertised through the <c>ui.native-input</c> capability so a driver knows
/// which guarantees it is getting instead of silently receiving the weaker one.
/// </para>
/// </remarks>
public sealed class TizenNativeInput(TizenAgentEnvironment environment)
{
    readonly TizenAgentEnvironment _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>True when real touch events can be synthesised.</summary>
    public bool SupportsSyntheticInput =>
        _environment.HasPrivilege(TizenPrivileges.InputGenerator);

    /// <summary>
    /// Taps at a screen coordinate using synthesised input.
    /// </summary>
    /// <returns>An error message, or null on success.</returns>
    public Task<string?> TryInjectTapAsync(double x, double y)
    {
        if (!SupportsSyntheticInput)
        {
            return Task.FromResult<string?>(
                $"Synthesised input requires the '{TizenPrivileges.InputGenerator}' privilege, which " +
                "is not granted. Declare it in tizen-manifest.xml and reinstall, or fall back to " +
                "framework-level interaction.");
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = Window.Default;
            if (window is null)
                return "No NUI window is attached.";

            var down = new Touch();
            window.FeedTouch(down, (int)x, (int)y);
            return (string?)null;
        });
    }

    /// <summary>
    /// Invokes a native element directly, bypassing hit-testing.
    /// </summary>
    /// <returns>An error message, or null on success.</returns>
    public Task<string?> TryInvokeAsync(object nativeElement) =>
        MainThread.InvokeOnMainThreadAsync<string?>(() =>
        {
            switch (nativeElement)
            {
                case Tizen.NUI.Components.Button button:
                    button.Clicked?.Invoke(button, new global::System.EventArgs());
                    return null;

                case View view when view.Focusable:
                    view.KeyInputFocus = true;
                    return null;

                default:
                    return $"{nativeElement?.GetType().Name ?? "null"} exposes no direct invoke path.";
            }
        });
}
