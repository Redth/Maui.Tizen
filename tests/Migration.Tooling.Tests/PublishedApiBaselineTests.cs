using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Consistency checks for eng/api-baselines/net9.0-tizen7.0 (the last-published MAUI Tizen
/// NuGet API surface, captured via eng/tools/ApiDump). Offline: only re-hashes already-committed
/// files, never re-downloads a package or re-runs ApiDump.
/// </summary>
public class PublishedApiBaselineTests
{
    private const string OutDir = "eng/api-baselines/net9.0-tizen7.0";

    [Fact]
    public void Manifest_exists_and_pins_the_baselines_json_version()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");

        var packageVersion = manifest.RootElement.GetProperty("packageVersion").GetString();
        var expectedTag = baselines.RootElement.GetProperty("source").GetProperty("behaviorBaseline").GetProperty("tag").GetString();

        Assert.Equal(expectedTag, packageVersion);
    }

    [Fact]
    public void Every_package_is_signed_hash_pinned_and_has_a_dump_file_matching_recorded_hashes()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");
        using var trustAnchor = TestPaths.LoadJson("eng/api-baselines/net9.0-tizen7.0-package-trust.json");
        var expectedCommit = baselines.RootElement.GetProperty("source").GetProperty("behaviorBaseline").GetProperty("commit").GetString();

        var trustedHashes = trustAnchor.RootElement.GetProperty("packages").EnumerateArray()
            .ToDictionary(p => p.GetProperty("packageId").GetString()!, p => p.GetProperty("nupkgSha256").GetString()!);

        var packages = manifest.RootElement.GetProperty("packages").EnumerateArray().ToList();
        Assert.NotEmpty(packages);

        foreach (var pkg in packages)
        {
            var packageId = pkg.GetProperty("packageId").GetString()!;
            Assert.Equal(expectedCommit, pkg.GetProperty("nuspecRepositoryCommit").GetString());

            // Every recorded package must be signed and have passed REAL NuGet signature
            // verification (integrity + trust -- see eng/tools/PackageVerify), not just a bare
            // SignedCms check on the isolated .signature.p7s blob. Microsoft.Maui.* packages are
            // always author/repository-signed, so an unsigned or invalid entry indicates
            // something went through unverified.
            Assert.True(pkg.GetProperty("signed").GetBoolean(), $"{packageId} is recorded as unsigned");
            Assert.True(pkg.GetProperty("signatureIntegrityAndTrustValid").GetBoolean(), $"{packageId} failed NuGet package signature verification (integrity + trust)");
            Assert.Matches("^[0-9a-f]{64}$", pkg.GetProperty("nupkgSha256").GetString()!);
            Assert.Matches("^[0-9a-f]{64}$", pkg.GetProperty("assemblySha256").GetString()!);

            // The manifest's recorded hash must match the pinned repository trust anchor exactly
            // -- this is the "reject unknown/mismatched package before generation" contract,
            // checked here as an offline cross-check that generation actually enforced it (rather
            // than only trusting the live script to have done so at generation time).
            Assert.True(trustedHashes.TryGetValue(packageId, out var pinnedHash), $"{packageId} has no entry in the trust anchor");
            Assert.Equal(pinnedHash, pkg.GetProperty("nupkgSha256").GetString());
            Assert.Equal(pinnedHash, pkg.GetProperty("pinnedNupkgSha256").GetString());

            var assemblyName = Path.GetFileNameWithoutExtension(pkg.GetProperty("assembly").GetString());
            var dumpPath = TestPaths.Path_(OutDir, assemblyName + ".json");
            Assert.True(File.Exists(dumpPath), $"Missing API dump for {assemblyName} at {dumpPath}");

            using var dump = JsonDocument.Parse(File.ReadAllText(dumpPath));
            var types = dump.RootElement.GetProperty("types");
            Assert.True(types.GetArrayLength() > 0, $"{assemblyName}.json has no public types recorded");

            var sha256 = dump.RootElement.GetProperty("sha256").GetString();
            Assert.Matches("^[0-9a-f]{64}$", sha256!);

            // The recorded output hash must match the committed dump file exactly -- this is the
            // "stale artifact" detector for the dump itself, not just its inputs.
            Assert.Equal(TestPaths.Sha256Hex(dumpPath), pkg.GetProperty("outputSha256").GetString());
        }
    }

    [Fact]
    public void Type_entries_are_sorted_deterministically()
    {
        foreach (var file in Directory.GetFiles(TestPaths.Path_(OutDir), "*.json"))
        {
            if (Path.GetFileName(file) == "manifest.json")
            {
                continue;
            }

            using var dump = JsonDocument.Parse(File.ReadAllText(file));
            var names = dump.RootElement.GetProperty("types")
                .EnumerateArray()
                .Select(t =>
                {
                    var ns = t.GetProperty("namespace").GetString()!;
                    var name = t.GetProperty("name").GetString()!;
                    var fullName = ns.Length == 0 ? name : $"{ns}.{name}";
                    return (fullName, t.GetProperty("arity").GetInt32());
                })
                .ToList();

            var sorted = names
                .OrderBy(n => n.Item1, StringComparer.Ordinal)
                .ThenBy(n => n.Item2)
                .ToList();

            Assert.Equal(sorted, names);
        }
    }

    [Fact]
    public void Nested_types_are_qualified_by_their_declaring_type()
    {
        // Without qualification, two unrelated nested types with the same simple name (common
        // for enums/delegates nested inside different handler classes) would be indistinguishable.
        var anyNestedTypeFound = false;
        foreach (var file in Directory.GetFiles(TestPaths.Path_(OutDir), "*.json"))
        {
            if (Path.GetFileName(file) == "manifest.json") continue;

            using var dump = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var t in dump.RootElement.GetProperty("types").EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                if (name.Contains('+'))
                {
                    anyNestedTypeFound = true;
                    var parts = name.Split('+');
                    Assert.True(parts.Length >= 2, $"Malformed nested type name: {name}");
                    Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p)));
                }
            }
        }
        Assert.True(anyNestedTypeFound, "Expected at least one nested type across the dumped assemblies to exercise declaring-type qualification.");
    }

    [Fact]
    public void Delegates_record_their_invoke_signature()
    {
        var anyDelegateFound = false;
        foreach (var file in Directory.GetFiles(TestPaths.Path_(OutDir), "*.json"))
        {
            if (Path.GetFileName(file) == "manifest.json") continue;

            using var dump = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var t in dump.RootElement.GetProperty("types").EnumerateArray())
            {
                if (t.GetProperty("kind").GetString() != "delegate") continue;
                anyDelegateFound = true;
                Assert.True(t.TryGetProperty("delegateSignature", out var sig) && !string.IsNullOrEmpty(sig.GetString()),
                    $"Delegate {t.GetProperty("name").GetString()} in {Path.GetFileName(file)} has no recorded Invoke signature");
                Assert.StartsWith("Invoke(", sig.GetString());
            }
        }
        Assert.True(anyDelegateFound, "Expected at least one delegate type across the dumped assemblies.");
    }

    [Fact]
    public void Enums_record_underlying_type_and_numeric_member_values()
    {
        var anyEnumFound = false;
        foreach (var file in Directory.GetFiles(TestPaths.Path_(OutDir), "*.json"))
        {
            if (Path.GetFileName(file) == "manifest.json") continue;

            using var dump = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var t in dump.RootElement.GetProperty("types").EnumerateArray())
            {
                if (t.GetProperty("kind").GetString() != "enum") continue;
                anyEnumFound = true;
                Assert.True(t.TryGetProperty("underlyingType", out var ut) && !string.IsNullOrEmpty(ut.GetString()),
                    $"Enum {t.GetProperty("name").GetString()} in {Path.GetFileName(file)} has no recorded underlying type");

                foreach (var member in t.GetProperty("members").EnumerateArray())
                {
                    var signature = member.GetProperty("signature").GetString()!;
                    Assert.Contains(" = ", signature);
                }
            }
        }
        Assert.True(anyEnumFound, "Expected at least one enum type across the dumped assemblies.");
    }
}
