namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Where the application under test registers its in-app convention assertions.
/// </summary>
/// <remarks>
/// <para>
/// A static registry rather than DI because the DevFlow extension route is mapped while the agent's
/// options are being built, before any service provider exists. The route needs a way to reach the
/// provider at request time, not at registration time.
/// </para>
/// <para>
/// No provider is registered by this assembly. The catalog application supplies one; until then the
/// route answers 501, which is what makes a device lane pointed at a non-self-asserting app fail
/// rather than silently pass.
/// </para>
/// </remarks>
public static class ConventionAssertionProviderRegistry
{
    static IConventionAssertionProvider? _current;

    /// <summary>The registered provider, or null when the app supplies none.</summary>
    public static IConventionAssertionProvider? Current => Volatile.Read(ref _current);

    /// <summary>True when an application has supplied assertions.</summary>
    public static bool HasProvider => Current is not null;

    /// <summary>Registers the application's provider. The last registration wins.</summary>
    public static void Register(IConventionAssertionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref _current, provider);
    }

    /// <summary>Clears the registration. Intended for tests.</summary>
    public static void Clear() => Volatile.Write(ref _current, null);
}
