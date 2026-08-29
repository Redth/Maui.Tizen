using System.Diagnostics;
using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Exercises the REAL eng/tools/PackageVerify tool (not a reimplementation of its logic) against
/// a real, already-restored NuGet package to prove two properties:
///
///   1. An untouched package verifies successfully (no false positives from the tool itself).
///   2. A package whose content was modified in place -- while its ORIGINAL, untouched
///      .signature.p7s entry is left completely intact -- is correctly rejected. This is the
///      exact scenario a bare System.Security.Cryptography.Pkcs.SignedCms.CheckSignature() call
///      on the isolated signature blob cannot detect: that check only proves the signature blob
///      itself is an internally well-formed, self-consistent PKCS#7 structure. It says nothing
///      about whether the package around it still matches. NuGet.Packaging's
///      IntegrityVerificationProvider recomputes the package's content hash and compares it to
///      the hash embedded in the (still perfectly valid) signature, which is what actually
///      catches this.
///
/// Uses an already-restored package from the local NuGet global-packages folder (the very
/// NuGet.Packaging package PackageVerify itself depends on, resolved dynamically from
/// Directory.Packages.props so the version never drifts) rather than downloading a fresh fixture:
/// by the time these tests run, `dotnet restore`/`build` has already fetched it as an ordinary
/// build dependency, so reading it here adds no new network dependency to the test suite.
/// Skips (rather than fails) if that package cannot be located, so a differently-configured NuGet
/// global-packages folder does not produce a spurious CI failure.
/// </summary>
public class PackageVerifyTests
{
    private static readonly string? NuGetPackagingNupkgPath = LocateNuGetPackagingNupkg();
    private static readonly string? PackageVerifyDllPath = LocatePackageVerifyDll();

    [Fact]
    public void Prerequisites_are_locatable()
    {
        // If these skip-worthy conditions are hit in CI (rather than a quick local iteration
        // where the solution hasn't been restored/built yet), that is itself worth surfacing
        // loudly rather than having every other test in this class silently skip.
        Assert.True(NuGetPackagingNupkgPath is not null, "Could not locate an already-restored NuGet.Packaging .nupkg in the local global-packages folder. Run 'dotnet restore eng/tools/PackageVerify' first.");
        Assert.True(PackageVerifyDllPath is not null, "Could not locate the built eng/tools/PackageVerify output. Run 'dotnet build eng/tools/PackageVerify -c Release' first.");
    }

    [Fact]
    public void Untampered_package_verifies_as_valid()
    {
        if (NuGetPackagingNupkgPath is null || PackageVerifyDllPath is null)
        {
            return; // Prerequisites_are_locatable already fails loudly when this happens.
        }

        var result = RunPackageVerify(NuGetPackagingNupkgPath);
        Assert.True(result.RootElement.GetProperty("isSigned").GetBoolean(), "expected the real NuGet.Packaging package to be signed");
        Assert.True(result.RootElement.GetProperty("isValid").GetBoolean(), "expected an untouched, real package to verify successfully");
    }

    [Fact]
    public void Package_modified_in_place_with_its_original_signature_intact_is_rejected()
    {
        if (NuGetPackagingNupkgPath is null || PackageVerifyDllPath is null)
        {
            return; // Prerequisites_are_locatable already fails loudly when this happens.
        }

        var tamperedPath = Path.Combine(Path.GetTempPath(), $"mt-pkgverify-tampered-{Guid.NewGuid():N}.nupkg");
        try
        {
            TamperPackageInPlace(NuGetPackagingNupkgPath, tamperedPath);

            // Defense-in-depth #1 (the raw file hash pin used by generate-api-baseline.ps1): the
            // tampered file's SHA-256 must differ from the original's, independent of anything
            // signature-related.
            Assert.NotEqual(Sha256Hex(NuGetPackagingNupkgPath), Sha256Hex(tamperedPath));

            // Defense-in-depth #2 (this test's actual subject): NuGet's own integrity check, which
            // recomputes a content hash and compares it to the one embedded in the (still present,
            // still internally valid) original signature.
            var result = RunPackageVerify(tamperedPath);
            Assert.True(result.RootElement.GetProperty("isSigned").GetBoolean(), "the tampered copy still carries its original .signature.p7s entry");
            Assert.False(result.RootElement.GetProperty("isValid").GetBoolean(), "expected a package modified after signing to fail verification");

            var errors = result.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errors, e => e is not null && e.Contains("NU3008", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tamperedPath))
            {
                File.Delete(tamperedPath);
            }
        }
    }

    private static JsonDocument RunPackageVerify(string nupkgPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{PackageVerifyDllPath}\" \"{nupkgPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"PackageVerify exited {process.ExitCode}: {stderr}");
        return JsonDocument.Parse(stdout.Trim());
    }

    /// <summary>
    /// Flips one byte inside a local file entry's COMPRESSED DATA region, leaving every zip
    /// structural field (local file header, central directory, and critically the
    /// .signature.p7s entry itself) byte-for-byte untouched. This is deliberately NOT done via a
    /// full zip re-write (e.g. System.IO.Compression.ZipArchive add/replace), because rewriting
    /// normalizes central-directory metadata (timestamps, external attributes) in ways that
    /// themselves invalidate the package's low-level structure before NuGet's integrity check
    /// even runs -- which would test zip-structural validation instead of the intended
    /// content-hash-mismatch scenario. An in-place raw byte patch is the only way to reliably
    /// isolate "content differs" from "structure differs".
    /// </summary>
    private static void TamperPackageInPlace(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);

        using (var archive = new System.IO.Compression.ZipArchive(File.OpenRead(sourcePath), System.IO.Compression.ZipArchiveMode.Read))
        {
            var target = archive.Entries.FirstOrDefault(e =>
                !e.FullName.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase) &&
                !e.FullName.EndsWith("/", StringComparison.Ordinal) &&
                e.CompressedLength > 0);
            Assert.True(target is not null, "expected at least one non-signature file entry with compressed data to tamper");

            using var fs = new FileStream(destinationPath, FileMode.Open, FileAccess.ReadWrite);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

            fs.Seek(target!.Offset(), SeekOrigin.Begin);
            var localHeader = reader.ReadBytes(30);
            var nameLen = BitConverter.ToUInt16(localHeader, 26);
            var extraLen = BitConverter.ToUInt16(localHeader, 28);
            var dataOffset = target.Offset() + 30 + nameLen + extraLen;

            fs.Seek(dataOffset, SeekOrigin.Begin);
            var firstByte = fs.ReadByte();
            fs.Seek(dataOffset, SeekOrigin.Begin);
            fs.WriteByte((byte)(firstByte ^ 0xFF));
        }
    }

    private static string Sha256Hex(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? LocateNuGetPackagingNupkg()
    {
        var version = ReadPinnedPackageVersion("NuGet.Packaging");
        if (version is null)
        {
            return null;
        }

        var globalPackagesFolder = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(globalPackagesFolder))
        {
            globalPackagesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }

        var path = Path.Combine(globalPackagesFolder, "nuget.packaging", version, $"nuget.packaging.{version}.nupkg");
        return File.Exists(path) ? path : null;
    }

    private static string? ReadPinnedPackageVersion(string packageId)
    {
        var propsPath = TestPaths.Path_("Directory.Packages.props");
        if (!File.Exists(propsPath))
        {
            return null;
        }

        var doc = System.Xml.Linq.XDocument.Load(propsPath);
        var element = doc.Descendants("PackageVersion")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        return (string?)element?.Attribute("Version");
    }

    private static string? LocatePackageVerifyDll()
    {
        var candidateRoots = new[]
        {
            TestPaths.Path_("artifacts", "bin", "PackageVerify"),
        };

        foreach (var root in candidateRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var match = Directory.GetFiles(root, "maui-tizen-packageverify.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

file static class ZipArchiveEntryExtensions
{
    // ZipArchiveEntry does not expose its local-header byte offset publicly; it is available via
    // reflection over the internal field the BCL implementation itself uses. This is fragile
    // across BCL versions in principle, but scoped to a single test file exercising an offline,
    // already-pinned dependency -- acceptable here in exchange for testing the REAL on-disk byte
    // layout rather than reimplementing zip parsing from scratch.
    public static long Offset(this System.IO.Compression.ZipArchiveEntry entry)
    {
        var field = typeof(System.IO.Compression.ZipArchiveEntry).GetField("_offsetOfLocalHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("ZipArchiveEntry._offsetOfLocalHeader field not found; BCL layout may have changed.");
        return (long)field.GetValue(entry)!;
    }
}
