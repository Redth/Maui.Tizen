// Real NuGet package signature verification: integrity (does the package's content still match
// what was signed?) and trust (does the signing certificate chain to a trusted root?).
//
// This is deliberately NOT a bare System.Security.Cryptography.Pkcs.SignedCms.CheckSignature()
// call on the isolated .signature.p7s blob. That check only proves the signature *blob itself* is
// an internally well-formed, cryptographically self-consistent PKCS#7 structure -- it does not
// recompute a hash over the package's actual current content and compare it to the hash embedded
// inside the signature. A package can be modified (e.g. a file replaced) while its original,
// untouched .signature.p7s entry is left in place, and a bare SignedCms check on that entry alone
// will still report success, because the entry alone is still valid -- it just no longer matches
// the (now different) package around it. NuGet.Packaging's IntegrityVerificationProvider performs
// the actual recompute-and-compare step that catches exactly that tamper scenario.
//
// Usage:
//   maui-tizen-packageverify <nupkg-path>
//
// Prints a single JSON object to stdout describing the verification result. Exit code is always 0
// unless the tool itself crashes (e.g. the file does not exist or is not a package at all) --
// callers must inspect the "isValid" field, not the process exit code, to decide whether the
// package passed verification.
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace Migration.PackageVerify;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: maui-tizen-packageverify <nupkg-path>");
            return 1;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        var result = new VerificationResult
        {
            Path = Path.GetFileName(path),
            Sha256 = sha256,
        };

        try
        {
            using var reader = new PackageArchiveReader(path);
            result.IsSigned = await reader.IsSignedAsync(CancellationToken.None);

            if (result.IsSigned)
            {
                var primary = await reader.GetPrimarySignatureAsync(CancellationToken.None);
                result.SignatureType = primary?.Type.ToString();

                var providers = new List<ISignatureVerificationProvider>
                {
                    new IntegrityVerificationProvider(),
                    new SignatureTrustAndValidityVerificationProvider(),
                };
                var verifier = new PackageSignatureVerifier(providers);
                var settings = SignedPackageVerifierSettings.GetDefault();
                var verifyResult = await verifier.VerifySignaturesAsync(reader, settings, CancellationToken.None);

                result.IsValid = verifyResult.IsValid;
                foreach (var r in verifyResult.Results)
                {
                    foreach (var issue in r.GetErrorIssues())
                    {
                        result.Errors.Add($"{issue.Code}: {issue.Message}");
                    }
                    foreach (var issue in r.GetWarningIssues())
                    {
                        result.Warnings.Add($"{issue.Code}: {issue.Message}");
                    }
                }
            }
            else
            {
                result.IsValid = false;
                result.Errors.Add("Package is not signed.");
            }
        }
        catch (Exception ex)
        {
            // A malformed/tampered package can make NuGet.Packaging's own low-level zip/metadata
            // reader throw (e.g. a corrupted central directory) rather than cleanly reporting a
            // verification failure. Either way, that IS a verification failure from this tool's
            // point of view -- report it as such rather than crashing the caller.
            result.IsValid = false;
            result.Errors.Add($"Exception during verification: {ex.GetType().Name}: {ex.Message}");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

        return 0;
    }
}

public sealed class VerificationResult
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public bool IsSigned { get; set; }
    public string? SignatureType { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
