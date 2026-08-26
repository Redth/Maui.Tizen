using Microsoft.Maui.DevFlow.Agent.Core;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Walks the NUI view hierarchy for elements DevFlow's MAUI walker cannot see.
/// </summary>
/// <remarks>
/// <para>
/// The base <see cref="VisualTreeWalker"/> already covers the MAUI visual tree. This subclass only
/// supplies the <em>native</em> layer: platform-owned NUI views such as Shell chrome, toolbar items
/// and dialogs, which are registered through <see cref="NativeElementDiagnosticsBridge"/>.
/// </para>
/// <para>
/// Element state maps onto NUI as follows, which is the mapping the parent audit specified:
/// </para>
/// <list type="table">
///   <item><term><c>IsVisible</c></term><description><see cref="View.Visibility"/></description></item>
///   <item><term><c>IsEnabled</c></term><description><see cref="View.Sensitive"/></description></item>
///   <item><term><c>IsFocused</c></term><description><see cref="View.KeyInputFocus"/></description></item>
///   <item><term><c>Bounds</c></term><description><see cref="View.ScreenPosition"/> + <see cref="View.CurrentSize"/></description></item>
/// </list>
/// <para>
/// <see cref="View.Name"/> is used as the automation id. It is the only identity NUI carries that
/// survives a layout pass; object hash codes do not, which is why ids come from the bridge instead.
/// </para>
/// </remarks>
public class TizenVisualTreeWalker : VisualTreeWalker
{
    readonly NativeElementDiagnosticsBridge _bridge;

    public TizenVisualTreeWalker()
        : this(NativeElementDiagnosticsBridge.Current)
    {
    }

    public TizenVisualTreeWalker(NativeElementDiagnosticsBridge bridge) =>
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

    public override List<ElementInfo> WalkNativeTree(IReadOnlyList<nint> knownWindowHandles, int maxDepth) =>
        [.. _bridge.Snapshot().Select(ToElementInfo)];

    public override List<ElementInfo> QueryNative(
        IReadOnlyList<nint> knownWindowHandles,
        string? type,
        string? automationId,
        string? text,
        string? selector) =>
        [.. _bridge.Query(type, automationId, text).Select(ToElementInfo)];

    public override List<ElementInfo> HitTestNativeElements(IReadOnlyList<nint> knownWindowHandles, double x, double y) =>
        [.. _bridge.HitTest(x, y).Select(ToElementInfo)];

    public override object? GetNativeElementById(string id) =>
        _bridge.TryGet(id, out var registration) ? registration.Descriptor.Target : null;

    public override ElementInfo? GetNativeElementInfoById(string id) =>
        _bridge.TryGet(id, out var registration) ? ToElementInfo(registration) : null;

    protected override bool CanInvokeRegisteredNativeElement(object nativeElement) =>
        FindDescriptor(nativeElement)?.CanInvoke ?? false;

    protected override bool CanFocusRegisteredNativeElement(object nativeElement) =>
        FindDescriptor(nativeElement)?.CanFocus ?? false;

    protected override bool CanSetValueRegisteredNativeElement(object nativeElement) =>
        FindDescriptor(nativeElement)?.CanSetValue ?? false;

    /// <summary>
    /// NUI's <see cref="View.Name"/> is the only stable, developer-assignable identity available.
    /// </summary>
    protected override string? EnsurePlatformStableId(object platformObj) =>
        platformObj is View { Name.Length: > 0 } view ? view.Name : null;

    /// <summary>Moves keyboard/remote focus to a registered native element.</summary>
    protected override string? TryNativeElementFocus(string elementId, object nativeElement)
    {
        if (nativeElement is not View view)
            return $"Element '{elementId}' is not a NUI View.";

        if (!view.Focusable)
            return $"Element '{elementId}' is not focusable.";

        view.KeyInputFocus = true;
        return null;
    }

    /// <summary>
    /// Sets text on a native NUI text field.
    /// </summary>
    /// <remarks>
    /// This is the <em>native</em> fill path and deliberately handles only NUI text widgets. MAUI
    /// inputs are filled by the base implementation through the MAUI element, which keeps bindings
    /// and validation running. Writing straight to the platform widget for a MAUI control would
    /// change the visible text without ever notifying the view model.
    /// </remarks>
    protected override string? TrySetValueRegisteredNativeElement(string elementId, object nativeElement, string value) =>
        nativeElement switch
        {
            TextField field => Apply(() => field.Text = value),
            TextEditor editor => Apply(() => editor.Text = value),
            _ => $"Element '{elementId}' is a {nativeElement?.GetType().Name ?? "null"}, " +
                 "which is not a NUI text input. Only TextField and TextEditor support native fill.",
        };

    static string? Apply(Action action)
    {
        action();
        return null;
    }

    NativeElementDescriptor? FindDescriptor(object nativeElement) =>
        _bridge.Snapshot()
            .FirstOrDefault(r => ReferenceEquals(r.Descriptor.Target, nativeElement))
            ?.Descriptor;

    /// <summary>Projects a registration into DevFlow's element shape.</summary>
    ElementInfo ToElementInfo(NativeElementRegistration registration)
    {
        var descriptor = registration.Descriptor;
        var view = descriptor.Target as View;

        var info = new ElementInfo
        {
            Id = registration.Id,
            Type = descriptor.TypeName,
            FullType = descriptor.Target.GetType().FullName ?? descriptor.TypeName,
            NativeType = descriptor.Target.GetType().Name,

            // 'native' distinguishes these from MAUI-tree elements in DevFlow's layer filter.
            Framework = "native",
            Origin = "tizen-native-bridge",
            Role = descriptor.Role,
            AutomationId = descriptor.AutomationId ?? view?.Name,
            Text = descriptor.Text,
            OwnerId = descriptor.OwnerId,
            Capabilities = [.. descriptor.Capabilities],
            RegistryGeneration = _bridge.Generation,
            Bounds = ToBounds(descriptor, view),
            IsVisible = view?.Visibility ?? true,
            IsEnabled = view?.Sensitive ?? true,
            IsFocused = view?.KeyInputFocus ?? false,
            Opacity = view?.Opacity ?? 1.0,
        };

        return info;
    }

    /// <summary>
    /// Prefers live NUI geometry over the bounds captured at registration time, because chrome moves
    /// (a toolbar slides, a dialog centres) after it is registered.
    /// </summary>
    static BoundsInfo ToBounds(NativeElementDescriptor descriptor, View? view)
    {
        if (view is null)
        {
            return new BoundsInfo
            {
                X = descriptor.Bounds.X,
                Y = descriptor.Bounds.Y,
                Width = descriptor.Bounds.Width,
                Height = descriptor.Bounds.Height,
            };
        }

        var position = view.ScreenPosition;
        var size = view.CurrentSize;

        return new BoundsInfo
        {
            X = position.X,
            Y = position.Y,
            Width = size.Width,
            Height = size.Height,
        };
    }
}
