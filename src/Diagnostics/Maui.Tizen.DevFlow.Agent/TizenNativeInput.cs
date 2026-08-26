using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using global::Tizen.NUI.WindowSystem;
using NUIView = global::Tizen.NUI.BaseComponents.View;
using NUIWindow = global::Tizen.NUI.Window;

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
///     <b>Synthesised input</b> (<see cref="TryInjectTapAsync"/>) posts real touch events through
///     <see cref="InputGenerator"/>. It exercises the full input stack, so it is the only way to
///     validate gesture recognisers and hit-testing - and it requires the
///     <c>http://tizen.org/privilege/inputgenerator</c> privilege.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Managed invocation</b> (<see cref="TryInvokeAsync"/>) calls the cross-platform MAUI API.
///     It always works, but it bypasses hit-testing entirely, so a control covered by an overlay
///     still "taps" successfully. It is the fallback, never the default.
///     </description>
///   </item>
/// </list>
/// <para>
/// The distinction is advertised through the <c>ui.native-input</c> capability so a driver knows
/// which guarantees it is getting instead of silently receiving the weaker one.
/// </para>
/// <para>
/// Tizen types are aliased throughout. Under <c>UseMaui</c> both <c>Microsoft.Maui.Controls</c> and
/// <c>Tizen.NUI</c> are in scope and <c>View</c> and <c>Window</c> exist in both, so the aliases
/// keep every use unambiguous regardless of which global usings are active.
/// </para>
/// </remarks>
public sealed class TizenNativeInput(TizenAgentEnvironment environment)
{
    readonly TizenAgentEnvironment _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>True when real touch and key events can be synthesised.</summary>
    public bool SupportsSyntheticInput =>
        _environment.HasPrivilege(TizenPrivileges.InputGenerator);

    /// <summary>
    /// Taps at a screen coordinate using synthesised touch input.
    /// </summary>
    /// <remarks>
    /// A tap is a <c>Begin</c> followed by an <c>End</c> at the same point. The generator is created
    /// and disposed per gesture rather than being held open: it owns a Wayland connection, and a
    /// long-lived one outlives the window it was created against when the app is backgrounded.
    /// </remarks>
    /// <returns>An error message, or null on success.</returns>
    public Task<string?> TryInjectTapAsync(double x, double y)
    {
        if (!SupportsSyntheticInput)
        {
            return Task.FromResult<string?>(
                $"Synthesised input requires the '{TizenPrivileges.InputGenerator}' privilege, which " +
                "is not granted. Declare it in tizen-manifest.xml and reinstall, or fall back to " +
                "managed invocation.");
        }

        return MainThread.InvokeOnMainThreadAsync((Func<string?>)(() =>
        {
            if (NUIWindow.Default is null)
                return "No NUI window is attached.";

            using var generator = CreateGenerator(InputGenerator.DeviceType.Touchscreen);

            generator.GenerateTouch(0, InputGenerator.TouchType.Begin, (int)x, (int)y);
            generator.GenerateTouch(0, InputGenerator.TouchType.End, (int)x, (int)y);

            return null;
        }));
    }

    /// <summary>Sends a key by name using synthesised keyboard input.</summary>
    /// <remarks>
    /// Used by the TV remote focus harness. Focus traversal has to be driven by real key events:
    /// setting focus programmatically would validate the code that sets focus rather than the
    /// traversal order a remote actually produces.
    /// </remarks>
    /// <returns>An error message, or null on success.</returns>
    public Task<string?> TryInjectKeyAsync(string keyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);

        if (!SupportsSyntheticInput)
        {
            return Task.FromResult<string?>(
                $"Synthesised key input requires the '{TizenPrivileges.InputGenerator}' privilege, " +
                "which is not granted.");
        }

        return MainThread.InvokeOnMainThreadAsync((Func<string?>)(() =>
        {
            using var generator = CreateGenerator(InputGenerator.DeviceType.Keyboard);

            generator.GenerateKey(keyName, true);
            generator.GenerateKey(keyName, false);

            return null;
        }));
    }

    /// <summary>
    /// Invokes an element through the managed MAUI API, bypassing hit-testing.
    /// </summary>
    /// <remarks>
    /// Buttons go through <see cref="IButton.Clicked"/> rather than the platform widget's event. A
    /// NUI event cannot be raised from outside its declaring class at all, and even where a
    /// platform equivalent exists it would skip the command binding and <c>Clicked</c> handlers
    /// that a test is usually there to observe.
    /// </remarks>
    /// <returns>An error message, or null on success.</returns>
    public Task<string?> TryInvokeAsync(object? nativeElement) =>
        MainThread.InvokeOnMainThreadAsync((Func<string?>)(() =>
        {
            switch (nativeElement)
            {
                case IButton button:
                    button.Clicked();
                    return null;

                case NUIView view when view.Focusable:
                    view.KeyInputFocus = true;
                    return null;

                case null:
                    return "No element was supplied.";

                default:
                    return $"{nativeElement.GetType().Name} exposes no managed invoke path. " +
                           "Register the MAUI element rather than the platform view, or grant " +
                           $"'{TizenPrivileges.InputGenerator}' so the element can be tapped for real.";
            }
        }));

    /// <remarks>
    /// A null display name connects to the compositor's default display, which is the only one a
    /// sandboxed application can reach.
    /// </remarks>
    static InputGenerator CreateGenerator(InputGenerator.DeviceType deviceType)
    {
        var display = new TizenCoreWlDisplay();
        display.Connect(null);

        return new InputGenerator(display, deviceType);
    }
}
