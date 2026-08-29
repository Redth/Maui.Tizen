namespace Maui.Tizen.Conventions.Tests;

/// <summary>
/// Verifies the API15 source rules against the pinned Samsung reference pack.
/// </summary>
/// <remarks>
/// <para>
/// The rules in <c>eng/validation/api15-contract.json</c> assert things about a platform this
/// repository cannot build against. Left unverified they are just prose, and prose about a
/// preview-era SDK goes stale quickly and silently.
/// </para>
/// <para>
/// The reference pack is an ordinary NuGet package, so its contents can be read on any hosted
/// runner. That turns each rule into a checked fact and, just as usefully, makes the rules
/// self-retiring: when Samsung restores a removed assembly or drops a deprecation, these fail and
/// say so.
/// </para>
/// </remarks>
public class Api15ReferencePackTests
{
    static async Task<string> AcquireOrSkipAsync()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");

        var pack = RepositoryBaselines.Target.ReferencePack;

        Assert.False(
            string.IsNullOrWhiteSpace(pack.Id) || string.IsNullOrWhiteSpace(pack.Version),
            "eng/baselines.json > target.referencePack must declare an id and version.");

        var directory = await ReferencePackProbe
            .TryAcquireAsync(pack.Id, pack.Version, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        ValidationSkip.When(
            directory is null,
            $"'{pack.Id}/{pack.Version}' is neither in the NuGet cache nor reachable on nuget.org " +
            "from this runner, so the API15 rules cannot be verified against it here.");

        return directory!;
    }

    [Fact]
    public async Task ReferencePack_MatchesTheBaselinePin()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);
        var assemblies = ReferencePackProbe.EnumerateAssemblies(directory);

        // A pack that unpacked to nothing would make every assertion below vacuously pass.
        Assert.True(
            assemblies.Count > 50,
            $"Expected a substantial reference pack but found {assemblies.Count} assemblies in " +
            $"'{directory}'. Verifying anything against it would be meaningless.");

        Assert.Contains("Tizen.NUI.dll", assemblies, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovedAssemblies_AreActuallyAbsentFromTheReferencePack()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);
        var assemblies = ReferencePackProbe.EnumerateAssemblies(directory);

        foreach (var rule in Api15Contract.Document.BannedSymbols)
        {
            if (rule.ReferencePackAssembly is not { Length: > 0 } assembly || rule.ExpectedInReferencePack is not { } expected)
                continue;

            var present = assemblies.Contains(assembly, StringComparer.OrdinalIgnoreCase);

            if (expected)
            {
                Assert.True(present, $"Rule '{rule.Id}' expects '{assembly}' in the reference pack, but it is absent.");
                continue;
            }

            Assert.False(
                present,
                $"""
                 Rule '{rule.Id}' bans symbols on the grounds that '{assembly}' was removed in
                 {Api15Contract.Document.ApiLevel}, but the reference pack DOES contain it.

                 The ban is therefore wrong and should be removed from
                 eng/validation/api15-contract.json, along with any code written to work around it.
                 """);
        }
    }

    [Fact]
    public async Task DeprecatedMembers_AreActuallyObsoleteInTheReferencePack()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);

        foreach (var rule in Api15Contract.Document.BannedSymbols)
        {
            if (rule.ReferencePackType is not { Length: > 0 } typeName ||
                rule.ObsoleteMember is not { Length: > 0 } obsoleteMember)
            {
                continue;
            }

            var assemblyName = typeName.StartsWith("Tizen.NUI.", StringComparison.Ordinal)
                ? "Tizen.NUI.dll"
                : typeName.Split('.')[0] + ".dll";

            var assemblyPath = ReferencePackProbe.FindAssembly(directory, assemblyName);
            Assert.True(assemblyPath is not null, $"'{assemblyName}' is not in the reference pack.");

            var members = ReferencePackProbe.ReadTypeMembers(assemblyPath!, typeName);
            Assert.True(members is not null, $"Type '{typeName}' is not in '{assemblyName}'.");

            Assert.True(
                members!.IsObsolete(obsoleteMember),
                $"""
                 Rule '{rule.Id}' bans '{typeName}.{obsoleteMember}' as deprecated, but it carries no
                 [Obsolete] attribute in the reference pack. Either the deprecation was reverted or
                 the rule is wrong; in both cases the rule should go.
                 """);

            if (rule.ReplacementMember is { Length: > 0 } replacement)
            {
                Assert.True(
                    members.HasProperty(replacement),
                    $"Rule '{rule.Id}' points at '{typeName}.{replacement}' as the replacement, but " +
                    "that member does not exist in the reference pack.");
            }
        }
    }

    [Fact]
    public async Task WindowInstance_DeprecationMessageStillNamesDefault()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);

        var nui = ReferencePackProbe.FindAssembly(directory, "Tizen.NUI.dll");
        Assert.True(nui is not null, "Tizen.NUI.dll is not in the reference pack.");

        var window = ReferencePackProbe.ReadTypeMembers(nui!, "Tizen.NUI.Window");
        Assert.True(window is not null, "Tizen.NUI.Window is not in Tizen.NUI.dll.");

        Assert.True(window!.HasProperty("Default"));
        Assert.True(window.IsObsolete("Instance"));

        // Pins the guidance the guard repeats to developers. If Samsung ever points the
        // deprecation somewhere else, the guard's advice would silently become wrong.
        Assert.Contains(
            "Default",
            window.ObsoleteMembers["Instance"],
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Tizen.NUI.dll", "Tizen.NUI.Clipboard")]
    [InlineData("Tizen.Security.WebAuthn.dll", "Tizen.Security.WebAuthn.Authenticator")]
    public async Task EssentialsApi15Capabilities_ArePresent(
        string assemblyName,
        string typeName)
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);
        var assembly = ReferencePackProbe.FindAssembly(directory, assemblyName);

        Assert.True(assembly is not null, $"'{assemblyName}' is not in the API15 reference pack.");
        Assert.NotNull(ReferencePackProbe.ReadTypeMembers(assembly!, typeName));
    }

    [Fact]
    public async Task ScreenshotStrideAndClipboardTextMethods_ArePublicInApi15()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);
        var nui = ReferencePackProbe.FindAssembly(directory, "Tizen.NUI.dll");
        Assert.NotNull(nui);

        var pixelBuffer = ReferencePackProbe.ReadTypeMembers(nui!, "Tizen.NUI.PixelBuffer");
        var clipboard = ReferencePackProbe.ReadTypeMembers(nui!, "Tizen.NUI.Clipboard");

        Assert.NotNull(pixelBuffer);
        Assert.NotNull(clipboard);
        Assert.True(pixelBuffer!.HasMethod("GetStrideBytes"));
        Assert.True(clipboard!.HasMethod("SetData"));
        Assert.True(clipboard.HasMethod("GetData"));
    }

    [Fact]
    public async Task WebAuthnAuthenticator_HasTheRequiredApi15Operations()
    {
        var directory = await AcquireOrSkipAsync().ConfigureAwait(true);
        var assembly = ReferencePackProbe.FindAssembly(directory, "Tizen.Security.WebAuthn.dll");
        Assert.NotNull(assembly);

        var authenticator = ReferencePackProbe.ReadTypeMembers(
            assembly!,
            "Tizen.Security.WebAuthn.Authenticator");
        Assert.NotNull(authenticator);

        Assert.True(authenticator!.HasMethod("SupportedAuthenticators"));
        Assert.True(authenticator.HasMethod("MakeCredential"));
        Assert.True(authenticator.HasMethod("GetAssertion"));
        Assert.True(authenticator.HasMethod("Cancel"));
    }
}
