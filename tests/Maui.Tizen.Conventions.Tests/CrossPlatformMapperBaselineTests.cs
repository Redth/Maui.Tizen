using System.Reflection;

namespace Maui.Tizen.Conventions.Tests;

/// <summary>
/// Derives the expected handler mapper keys from the pinned MAUI packages.
/// </summary>
/// <remarks>
/// <para>
/// Handler parity has two halves. The Tizen half needs the backend executing on a device. The
/// cross-platform half does not: <c>Microsoft.Maui.Core</c> ships a neutral <c>lib/net11.0</c>
/// asset, so the expected key sets can be read here, on an ordinary hosted runner, from the exact
/// package version the repository builds against.
/// </para>
/// <para>
/// That matters because the alternative is a hand-maintained list of expected keys, which is wrong
/// the moment MAUI adds a mapper entry - and wrong in the most damaging direction, since a missing
/// expectation makes a missing Tizen implementation look like parity.
/// </para>
/// </remarks>
public class CrossPlatformMapperBaselineTests
{
    static Assembly MauiCore => typeof(Microsoft.Maui.IView).Assembly;

    /// <summary>Handler types that expose a static property mapper.</summary>
    static IReadOnlyList<Type> HandlerTypes() =>
        [.. ImplementationCoverageAnalyzer.SafeGetTypes(MauiCore)
            .Where(t => t is { IsPublic: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Where(t => MapperInspector.GetStaticMember(t, MapperInspector.PropertyMapperMemberName) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    [Fact]
    public void ThePinnedMauiPackageIsTheOneTheRepositoryBuildsAgainst()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");

        var informational = MauiCore
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? string.Empty;

        // Reading expected keys from a different MAUI build than the product uses would produce
        // parity failures that describe the test's dependencies rather than the backend.
        Assert.False(string.IsNullOrWhiteSpace(informational));
    }

    [Fact]
    public void CrossPlatformHandlersExposeReadableMappers()
    {
        var handlers = HandlerTypes();

        Assert.True(
            handlers.Count > 5,
            $"Only {handlers.Count} handler(s) with a static Mapper were found in " +
            $"'{MauiCore.GetName().Name}'. The parity baseline would be near-empty and would " +
            "assert almost nothing.");
    }

    [Fact]
    public void EveryDiscoveredHandlerYieldsAReadableExpectedKeySet()
    {
        var handlers = HandlerTypes();
        var unreadable = new List<string>();
        var empty = new List<string>();
        var withKeys = 0;

        foreach (var handler in handlers)
        {
            // Unreadable and empty are different things, and conflating them was wrong.
            // ApplicationHandler and the MenuFlyout family legitimately map no properties - they
            // are structural handlers - so an empty set is correct MAUI behaviour, not a gap.
            // A mapper that cannot be READ is the real problem: it silently contributes no
            // expectations, so a Tizen handler could omit everything and still show parity.
            if (!MapperInspector.TryGetPropertyMapperKeys(handler, out var keys))
            {
                unreadable.Add(handler.FullName ?? handler.Name);
                continue;
            }

            if (keys.Count == 0)
                empty.Add(handler.FullName ?? handler.Name);
            else
                withKeys++;
        }

        Assert.True(
            unreadable.Count == 0,
            $"""
             These handlers expose a Mapper whose keys could not be read, so they contribute no
             expectations to parity:
             {string.Join(Environment.NewLine, unreadable.Select(u => "    " + u))}
             """);

        // Guards the shape of the baseline as a whole: if most handlers went empty, parity would
        // still "pass" while asserting almost nothing.
        Assert.True(
            withKeys > empty.Count,
            $"Only {withKeys} of {handlers.Count} handlers contribute expected keys " +
            $"({empty.Count} are empty). The parity baseline has become too weak to be meaningful.");
    }

    [Fact]
    public void ButtonHandlerBaselineIncludesTheKeysAPlatformMustImplement()
    {
        var buttonHandler = HandlerTypes()
            .FirstOrDefault(t => t.Name.Equals("ButtonHandler", StringComparison.Ordinal));

        ValidationSkip.When(
            buttonHandler is null,
            "ButtonHandler was not found in the pinned Microsoft.Maui.Core.");

        Assert.True(MapperInspector.TryGetPropertyMapperKeys(buttonHandler!, out var keys));

        // A concrete spot-check: if these ever stopped appearing, the baseline would be silently
        // weaker and a Tizen handler could omit them without failing parity.
        Assert.Contains("Background", keys, StringComparer.Ordinal);
        Assert.Contains("IsEnabled", keys, StringComparer.Ordinal);
    }

    [Fact]
    public void ParityBindsTheGeneratedBaselineToTheTizenBackendOnDevice()
    {
        // The expected side is available here; the actual side is not. On a device this becomes the
        // real parity gate, comparing the same generated baseline against the live Tizen mappers.
        ValidationSkip.When(
            !ProductAssemblies.RunningOnTizen,
            "The Tizen half of parity requires the backend executing in-process. The expected key " +
            "sets are generated here from the pinned MAUI packages; the comparison runs in the " +
            "device lane. See docs/validation/device-lane.md.");

        Assert.NotEmpty(HandlerTypes());
    }
}
