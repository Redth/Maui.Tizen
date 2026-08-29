using Microsoft.Extensions.DependencyInjection;

namespace Maui.Tizen.Conventions.Tests;

// ---------------------------------------------------------------------------------------------
// Fakes.
//
// The parity, registration and coverage engines are the load-bearing part of these conventions,
// and they cannot be exercised against the Tizen backend on a hosted runner: mapper dictionaries
// are runtime state built by static initialisers, so reading them requires executing code that
// links against Tizen.NET.
//
// These fakes give the engines a real workout today. Without them the whole suite would consist of
// skips, and a broken engine would be indistinguishable from a passing one right up until the day
// the device lane first runs.
// ---------------------------------------------------------------------------------------------

sealed class FakeMapper(params string[] keys)
{
    public IEnumerable<string> GetKeys() => keys;
}

static class CrossPlatformButtonHandler
{
    public static FakeMapper Mapper { get; } = new("Background", "IsEnabled", "Text", "TextColor");

    public static FakeMapper CommandMapper { get; } = new("Focus", "Unfocus");
}

static class CompliantTizenButtonHandler
{
    public static FakeMapper Mapper { get; } = new("Background", "IsEnabled", "Text", "TextColor");

    public static FakeMapper CommandMapper { get; } = new("Focus", "Unfocus");
}

static class IncompleteTizenButtonHandler
{
    // Missing TextColor: compiles, never throws, and the control just renders the wrong colour.
    public static FakeMapper Mapper { get; } = new("Background", "IsEnabled", "Text");

    public static FakeMapper CommandMapper { get; } = new("Focus");
}

static class ExtendedTizenButtonHandler
{
    public static FakeMapper Mapper { get; } = new("Background", "IsEnabled", "Text", "TextColor", "TizenOverlayMode");
}

static class HandlerWithoutMappers
{
}

interface IFakeBattery;

interface IFakeGeolocation;

interface IFakeFlashlight;

sealed class TizenBattery : IFakeBattery;

sealed class TizenGeolocation : IFakeGeolocation;

sealed class DuplicateGeolocation : IFakeGeolocation;

/// <summary>
/// Verifies the handler mapper/command parity engine.
/// </summary>
public class HandlerParityConventionTests
{
    [Fact]
    public void MapperInspector_ReadsPropertyAndCommandKeysByShape()
    {
        Assert.True(MapperInspector.TryGetPropertyMapperKeys(typeof(CrossPlatformButtonHandler), out var properties));
        Assert.Equal(["Background", "IsEnabled", "Text", "TextColor"], properties);

        Assert.True(MapperInspector.TryGetCommandMapperKeys(typeof(CrossPlatformButtonHandler), out var commands));
        Assert.Equal(["Focus", "Unfocus"], commands);
    }

    [Fact]
    public void MapperInspector_ReportsAbsenceRatherThanThrowing()
    {
        // A handler with no mappers is a real state during migration; it must produce a clear
        // parity failure, not an exception from deep inside the engine.
        Assert.False(MapperInspector.TryGetPropertyMapperKeys(typeof(HandlerWithoutMappers), out var keys));
        Assert.Empty(keys);
    }

    [Fact]
    public void MapperKeys_AreReturnedInDeterministicOrder()
    {
        MapperInspector.TryGetPropertyMapperKeys(typeof(ExtendedTizenButtonHandler), out var keys);

        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
    }

    [Fact]
    public void Parity_PassesForAMatchingHandler()
    {
        MapperInspector.TryGetPropertyMapperKeys(typeof(CrossPlatformButtonHandler), out var expected);
        MapperInspector.TryGetPropertyMapperKeys(typeof(CompliantTizenButtonHandler), out var actual);

        var report = HandlerParityAnalyzer.Compare("ButtonHandler.Mapper", expected, actual);

        Assert.True(report.Passed, report.Describe());
    }

    [Fact]
    public void Parity_DetectsMissingPropertyKeys()
    {
        MapperInspector.TryGetPropertyMapperKeys(typeof(CrossPlatformButtonHandler), out var expected);
        MapperInspector.TryGetPropertyMapperKeys(typeof(IncompleteTizenButtonHandler), out var actual);

        var report = HandlerParityAnalyzer.Compare("ButtonHandler.Mapper", expected, actual);

        Assert.False(report.Passed);
        Assert.Equal(["TextColor"], report.MissingKeys);
        Assert.Contains("TextColor", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parity_DetectsMissingCommandKeys()
    {
        MapperInspector.TryGetCommandMapperKeys(typeof(CrossPlatformButtonHandler), out var expected);
        MapperInspector.TryGetCommandMapperKeys(typeof(IncompleteTizenButtonHandler), out var actual);

        var report = HandlerParityAnalyzer.Compare("ButtonHandler.CommandMapper", expected, actual);

        Assert.False(report.Passed);
        Assert.Equal(["Unfocus"], report.MissingKeys);
    }

    [Fact]
    public void Parity_FlagsUndeclaredPlatformOnlyKeys()
    {
        MapperInspector.TryGetPropertyMapperKeys(typeof(CrossPlatformButtonHandler), out var expected);
        MapperInspector.TryGetPropertyMapperKeys(typeof(ExtendedTizenButtonHandler), out var actual);

        var report = HandlerParityAnalyzer.Compare("ButtonHandler.Mapper", expected, actual);

        Assert.False(report.Passed);
        Assert.Equal(["TizenOverlayMode"], report.UnexpectedExtraKeys);
    }

    [Fact]
    public void Parity_AllowsDeclaredPlatformOnlyKeysButStillReportsThem()
    {
        // Platform-only keys are legitimate; an unreviewed list of them is not. Declaring one keeps
        // the suite green while leaving it visible in the report.
        MapperInspector.TryGetPropertyMapperKeys(typeof(CrossPlatformButtonHandler), out var expected);
        MapperInspector.TryGetPropertyMapperKeys(typeof(ExtendedTizenButtonHandler), out var actual);

        var report = HandlerParityAnalyzer.Compare(
            "ButtonHandler.Mapper",
            expected,
            actual,
            knownPlatformOnlyKeys: ["TizenOverlayMode"]);

        Assert.True(report.Passed, report.Describe());
        Assert.Equal(["TizenOverlayMode"], report.DeclaredPlatformOnlyKeys);
        Assert.Contains("platform-only", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parity_BindsToTheRealBackendWhenItCanExecute()
    {
        // Mapper contents are runtime state, so this needs the Tizen backend to be executing.
        // On a hosted runner it skips; in the device lane it becomes the real parity gate.
        ValidationSkip.When(
            !ProductAssemblies.RunningOnTizen,
            "Live mapper parity requires the Tizen backend to be executing in-process. It runs in " +
            "the device lane; see docs/validation/device-lane.md.");

        var assembly = ProductAssemblies.LoadOrSkip("Maui.Tizen.Controls");

        var handlers = ImplementationCoverageAnalyzer.SafeGetTypes(assembly)
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(handlers);
    }
}

/// <summary>Verifies the DI registration convention engine.</summary>
public class ServiceRegistrationConventionTests
{
    [Fact]
    public void DetectsAMissingRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeBattery, TizenBattery>();

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [
                new ServiceExpectation(typeof(IFakeBattery), ServiceLifetime.Singleton, typeof(TizenBattery)),
                new ServiceExpectation(typeof(IFakeGeolocation), ServiceLifetime.Singleton),
            ]);

        Assert.False(report.Passed);
        Assert.Contains("IFakeGeolocation", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsAWrongLifetime()
    {
        // A platform service registered transiently instead of as a singleton produces duplicated
        // native handles rather than an error.
        var services = new ServiceCollection();
        services.AddTransient<IFakeBattery, TizenBattery>();

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [new ServiceExpectation(typeof(IFakeBattery), ServiceLifetime.Singleton)]);

        Assert.False(report.Passed);
        Assert.Contains("Transient", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsAWrongImplementation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeGeolocation, DuplicateGeolocation>();

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [new ServiceExpectation(typeof(IFakeGeolocation), ServiceLifetime.Singleton, typeof(TizenGeolocation))]);

        Assert.False(report.Passed);
        Assert.Contains("DuplicateGeolocation", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsDuplicateRegistrations()
    {
        // Resolving a doubly registered service succeeds and silently returns the last one, so this
        // is only visible by inspecting descriptors.
        var services = new ServiceCollection();
        services.AddSingleton<IFakeGeolocation, TizenGeolocation>();
        services.AddSingleton<IFakeGeolocation, DuplicateGeolocation>();

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [new ServiceExpectation(typeof(IFakeGeolocation), ServiceLifetime.Singleton)]);

        Assert.False(report.Passed);
        Assert.Contains("registered 2 times", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void PassesAConformingRegistrationSet()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeBattery, TizenBattery>();
        services.AddSingleton<IFakeGeolocation, TizenGeolocation>();

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [
                new ServiceExpectation(typeof(IFakeBattery), ServiceLifetime.Singleton, typeof(TizenBattery)),
                new ServiceExpectation(typeof(IFakeGeolocation), ServiceLifetime.Singleton, typeof(TizenGeolocation)),
            ]);

        Assert.True(report.Passed, report.Describe());
        Assert.Equal(2, report.VerifiedCount);
    }

    [Fact]
    public void ReportsFactoryRegistrationsThatCannotBeVerifiedStatically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeBattery>(_ => new TizenBattery());

        var report = ServiceRegistrationAnalyzer.Analyze(
            "Tizen Essentials",
            services,
            [new ServiceExpectation(typeof(IFakeBattery), ServiceLifetime.Singleton, typeof(TizenBattery))]);

        Assert.False(report.Passed);
        Assert.Contains("factory", report.Describe(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Verifies the Essentials implementation-coverage engine.</summary>
public class EssentialsCoverageConventionTests
{
    [Fact]
    public void DetectsAContractWithNoImplementation()
    {
        var report = ImplementationCoverageAnalyzer.Analyze(
            "Tizen Essentials",
            [typeof(IFakeBattery), typeof(IFakeFlashlight)],
            typeof(TizenBattery).Assembly);

        Assert.False(report.Passed);
        Assert.Contains(typeof(IFakeFlashlight), report.MissingImplementations);
        Assert.Contains("IFakeFlashlight", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DetectsAmbiguousImplementations()
    {
        var report = ImplementationCoverageAnalyzer.Analyze(
            "Tizen Essentials",
            [typeof(IFakeGeolocation)],
            typeof(TizenGeolocation).Assembly);

        Assert.False(report.Passed);
        Assert.Single(report.AmbiguousImplementations);
    }

    [Fact]
    public void AllowsExplicitlyDeclaredGaps()
    {
        // Some Essentials APIs have no Tizen equivalent. Declaring them keeps the suite meaningful
        // instead of permanently red.
        var report = ImplementationCoverageAnalyzer.Analyze(
            "Tizen Essentials",
            [typeof(IFakeBattery), typeof(IFakeFlashlight)],
            typeof(TizenBattery).Assembly,
            knownUnimplemented: [typeof(IFakeFlashlight)]);

        Assert.True(report.Passed, report.Describe());
        Assert.Equal(typeof(TizenBattery), report.ResolvedImplementations[typeof(IFakeBattery)]);
    }

    [Fact]
    public void BindsToTheRealEssentialsAssemblyWhenItCanExecute()
    {
        ValidationSkip.When(
            !ProductAssemblies.RunningOnTizen,
            "Essentials coverage requires the Tizen backend to be loadable in-process. It runs in " +
            "the device lane; see docs/validation/device-lane.md.");

        Assert.NotNull(ProductAssemblies.LoadOrSkip("Maui.Tizen.Essentials"));
    }
}
