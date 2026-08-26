using System.Diagnostics.CodeAnalysis;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Registry of native Tizen elements that have no MAUI visual-tree counterpart.
/// </summary>
/// <remarks>
/// <para>
/// DevFlow's <c>VisualTreeWalker</c> exposes platform extension points
/// (<c>WalkNativeTree</c>, <c>QueryNative</c>, <c>GetNativeElementById</c>, <c>HitTestNativeElements</c>,
/// <c>TryNativeElement*</c>) but no public registration API - the equivalent bookkeeping is internal
/// to the framework backends. This bridge is the Tizen-side implementation of that bookkeeping.
/// </para>
/// <para>
/// It exists because several things a test must interact with on Tizen are NUI views owned by the
/// platform rather than MAUI elements: Shell chrome, toolbar items, and native dialogs. They never
/// appear in the MAUI visual tree, so without an explicit registry a driver simply cannot see or tap
/// them.
/// </para>
/// <para>
/// <see cref="Generation"/> maps onto DevFlow's <c>registryGeneration</c>: request payloads carry the
/// generation they were computed against, so a driver acting on a stale snapshot can be rejected
/// with a stale-element error instead of tapping whatever now occupies that id.
/// </para>
/// <para>
/// This type holds native handles as <see cref="object"/> and has no Tizen references, so its
/// behaviour is exercised on hosted runners.
/// </para>
/// </remarks>
public sealed class NativeElementDiagnosticsBridge
{
    /// <summary>Prefix applied to generated ids so they are distinguishable in DevFlow payloads.</summary>
    public const string IdPrefix = "tizen-native:";

    readonly Lock _gate = new();
    readonly Dictionary<string, NativeElementRegistration> _byId = new(StringComparer.Ordinal);
    readonly List<string> _order = [];

    int _generation;
    int _nextId;

    /// <summary>Shared instance used by the Tizen agent.</summary>
    public static NativeElementDiagnosticsBridge Current { get; } = new();

    /// <summary>
    /// Incremented on every mutation. Callers pass the generation their snapshot was taken at so
    /// stale interactions can be detected.
    /// </summary>
    public int Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _byId.Count;
        }
    }

    /// <summary>Registers a native element and returns its stable id.</summary>
    public string Register(NativeElementDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_gate)
        {
            var id = IdPrefix + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _byId[id] = new NativeElementRegistration(id, descriptor);
            _order.Add(id);
            _generation++;
            return id;
        }
    }

    /// <summary>Removes a registration. Returns false when the id was already gone.</summary>
    public bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        lock (_gate)
        {
            if (!_byId.Remove(id))
                return false;

            _order.Remove(id);
            _generation++;
            return true;
        }
    }

    /// <summary>Removes every registration, e.g. when a page is torn down.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_byId.Count == 0)
                return;

            _byId.Clear();
            _order.Clear();
            _generation++;
        }
    }

    /// <summary>Registrations in registration order.</summary>
    public IReadOnlyList<NativeElementRegistration> Snapshot()
    {
        lock (_gate)
            return [.. _order.Select(id => _byId[id])];
    }

    public bool TryGet(string id, [NotNullWhen(true)] out NativeElementRegistration? registration)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        lock (_gate)
            return _byId.TryGetValue(id, out registration);
    }

    /// <summary>
    /// Registrations whose bounds contain the point, innermost first.
    /// </summary>
    /// <remarks>
    /// Ordering by ascending area approximates "topmost" without a real z-order, which NUI does not
    /// expose uniformly for platform-owned chrome. Smaller elements sit on top of larger containers
    /// in practice, and this only disambiguates registered chrome, never the MAUI tree.
    /// </remarks>
    public IReadOnlyList<NativeElementRegistration> HitTest(double x, double y)
    {
        lock (_gate)
        {
            return
            [
                .. _order
                    .Select(id => _byId[id])
                    .Where(r => r.Descriptor.Bounds.Contains(x, y))
                    .OrderBy(r => r.Descriptor.Bounds.Area)
            ];
        }
    }

    /// <summary>Registrations matching a DevFlow query.</summary>
    /// <param name="type">Matches <see cref="NativeElementDescriptor.TypeName"/> exactly.</param>
    /// <param name="automationId">Matches <see cref="NativeElementDescriptor.AutomationId"/> exactly.</param>
    /// <param name="text">Matches <see cref="NativeElementDescriptor.Text"/> as a substring.</param>
    public IReadOnlyList<NativeElementRegistration> Query(string? type, string? automationId, string? text)
    {
        lock (_gate)
        {
            IEnumerable<NativeElementRegistration> query = _order.Select(id => _byId[id]);

            if (!string.IsNullOrEmpty(type))
                query = query.Where(r => string.Equals(r.Descriptor.TypeName, type, StringComparison.Ordinal));

            if (!string.IsNullOrEmpty(automationId))
                query = query.Where(r => string.Equals(r.Descriptor.AutomationId, automationId, StringComparison.Ordinal));

            if (!string.IsNullOrEmpty(text))
                query = query.Where(r => r.Descriptor.Text?.Contains(text, StringComparison.Ordinal) == true);

            return [.. query];
        }
    }
}

/// <param name="Descriptor">Immutable description captured at registration time.</param>
public sealed record NativeElementRegistration(string Id, NativeElementDescriptor Descriptor);

/// <summary>Describes a platform-owned element for DevFlow.</summary>
/// <param name="Target">The native object, held as <see cref="object"/> to keep this assembly portable.</param>
/// <param name="TypeName">Short type name surfaced as DevFlow's <c>type</c>.</param>
/// <param name="Role">DevFlow role, e.g. <c>button</c>, <c>tab</c>, <c>dialog</c>.</param>
/// <param name="OwnerId">Id of the owning element, when this is chrome belonging to something else.</param>
public sealed record NativeElementDescriptor(
    object Target,
    string TypeName,
    string Role,
    NativeElementBounds Bounds,
    string? AutomationId = null,
    string? Text = null,
    string? OwnerId = null,
    bool CanInvoke = false,
    bool CanFocus = false,
    bool CanSetValue = false)
{
    /// <summary>DevFlow <c>capabilities</c> array for this element.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get
        {
            var capabilities = new List<string>(3);

            if (CanInvoke)
                capabilities.Add("invoke");

            if (CanFocus)
                capabilities.Add("focus");

            if (CanSetValue)
                capabilities.Add("set-value");

            return capabilities;
        }
    }
}

/// <summary>Screen-space bounds in device-independent units.</summary>
public readonly record struct NativeElementBounds(double X, double Y, double Width, double Height)
{
    public double Area => Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>True when the point falls inside, treating right/bottom edges as exclusive.</summary>
    public bool Contains(double x, double y) =>
        Width > 0 && Height > 0 &&
        x >= X && x < X + Width &&
        y >= Y && y < Y + Height;
}
