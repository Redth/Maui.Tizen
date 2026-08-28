using Microsoft.Extensions.DependencyInjection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Asserts that a backend registers the services it is expected to, with the expected lifetimes.
/// </summary>
/// <remarks>
/// Inspects <see cref="ServiceDescriptor"/>s on the collection rather than resolving from a built
/// provider. Resolution would execute platform constructors that cannot run on a hosted runner, and
/// it hides duplicate registrations: resolving a doubly registered service succeeds and silently
/// returns the last one, while a descriptor scan can flag it.
/// </remarks>
public static class ServiceRegistrationAnalyzer
{
    /// <summary>Verifies <paramml="expectations"/> against <paramref name="services"/>.</summary>
    public static ServiceRegistrationReport Analyze(
        string subject,
        IServiceCollection services,
        IEnumerable<ServiceExpectation> expectations)
    {
        ArgumentNullException.ThrowIfNull(services);

        var problems = new List<string>();
        var verified = 0;

        foreach (var expectation in expectations)
        {
            var matches = services
                .Where(d => d.ServiceType == expectation.ServiceType)
                .ToList();

            if (matches.Count == 0)
            {
                problems.Add(
                    $"  {expectation.ServiceType.FullName} is not registered " +
                    $"(expected {expectation.Lifetime} -> {expectation.ImplementationType?.FullName ?? "any"}).");
                continue;
            }

            if (expectation.ExpectSingleRegistration && matches.Count > 1)
            {
                problems.Add(
                    $"  {expectation.ServiceType.FullName} is registered {matches.Count} times; " +
                    "the last registration silently wins at resolution time.");
                continue;
            }

            var descriptor = matches[^1];

            if (descriptor.Lifetime != expectation.Lifetime)
            {
                problems.Add(
                    $"  {expectation.ServiceType.FullName} is registered as {descriptor.Lifetime} " +
                    $"but {expectation.Lifetime} was expected.");
                continue;
            }

            if (expectation.ImplementationType is { } expectedImplementation)
            {
                var actualImplementation = descriptor.ImplementationType;

                if (actualImplementation is null)
                {
                    // Factory or instance registration: the implementation type is not statically known.
                    if (descriptor.ImplementationInstance is { } instance)
                    {
                        actualImplementation = instance.GetType();
                    }
                    else
                    {
                        problems.Add(
                            $"  {expectation.ServiceType.FullName} is registered via a factory, so " +
                            $"'{expectedImplementation.FullName}' cannot be verified statically. " +
                            "Register the implementation type directly or drop the expectation.");
                        continue;
                    }
                }

                if (actualImplementation != expectedImplementation)
                {
                    problems.Add(
                        $"  {expectation.ServiceType.FullName} resolves to " +
                        $"'{actualImplementation.FullName}' but '{expectedImplementation.FullName}' was expected.");
                    continue;
                }
            }

            verified++;
        }

        return new ServiceRegistrationReport(subject, verified, problems);
    }
}

/// <summary>A single expected DI registration.</summary>
/// <param name="ServiceType">The contract that must be registered.</param>
/// <param name="Lifetime">The lifetime it must be registered with.</param>
/// <param name="ImplementationType">Optional concrete type that must back it.</param>
/// <param name="ExpectSingleRegistration">
/// When true, more than one registration for the contract is treated as an error.
/// </param>
public sealed record ServiceExpectation(
    Type ServiceType,
    ServiceLifetime Lifetime,
    Type? ImplementationType = null,
    bool ExpectSingleRegistration = true);

public sealed record ServiceRegistrationReport(string Subject, int VerifiedCount, IReadOnlyList<string> Problems)
{
    public bool Passed => Problems.Count == 0;

    public string Describe() =>
        Passed
            ? $"'{Subject}': {VerifiedCount} registration(s) verified."
            : string.Join(
                Environment.NewLine,
                new[] { $"'{Subject}' registration check failed:" }.Concat(Problems));
}
