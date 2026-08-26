// Deterministic inventory of dotnet/maui files relevant to the Tizen extraction.
//
// This tool performs a pure file-system walk plus text scan of a local source checkout. It never
// restores, builds, or executes the scanned project, so no Tizen workload/SDK is required.
//
// Output conforms to eng/manifests/source-disposition.schema.json (owned by the foundation/import
// workstream -- this tool is the schema's consumer, not its author).
//
// Usage:
//   maui-tizen-source-inventory --baselines <eng/baselines.json>
//                                --primary-root <path-to-net11-checkout>
//                                --legacy-root <path-to-9.0.120-checkout>
//                                --out <source-disposition.json>
//                                --summary-out <source-disposition.summary.md>
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Migration.SourceInventory;

public static class Program
{
    public static int Main(string[] args)
    {
        string? baselinesPath = null, primaryRoot = null, legacyRoot = null, outPath = null, summaryOutPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i])
            {
                case "--baselines": baselinesPath = Next(); break;
                case "--primary-root": primaryRoot = Next(); break;
                case "--legacy-root": legacyRoot = Next(); break;
                case "--out": outPath = Next(); break;
                case "--summary-out": summaryOutPath = Next(); break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        if (baselinesPath is null || primaryRoot is null || legacyRoot is null || outPath is null)
        {
            Console.Error.WriteLine("Usage: maui-tizen-source-inventory --baselines <path> --primary-root <path> --legacy-root <path> --out <path> [--summary-out <path>]");
            Console.Error.WriteLine("(--legacy-root is mandatory: a net11-only scan silently under-reports by the src/Compatibility files that only exist at the 9.0.120 behaviorBaseline.)");
            return 1;
        }

        var baselines = JsonDocument.Parse(File.ReadAllText(baselinesPath)).RootElement;
        var source = baselines.GetProperty("source");
        var primaryRef = source.GetProperty("sourceBaseline").GetProperty("commit").GetString()!;
        var legacyRef = source.GetProperty("behaviorBaseline").GetProperty("commit").GetString()!;

        var scanner = new Scanner();
        var entries = scanner.Scan(primaryRoot, legacyRoot).ToList();
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var manifest = new
        {
            schemaVersion = 1,
            generatedFrom = new
            {
                sourceBaseline = primaryRef,
                behaviorBaseline = legacyRef,
                generatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                tool = "eng/tools/SourceInventory (maui-tizen-source-inventory)",
            },
            entries,
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(manifest, jsonOptions));
        Console.WriteLine($"Wrote {outPath} ({entries.Count} entries)");

        if (summaryOutPath is not null)
        {
            var md = SummaryWriter.Write(entries, primaryRef, legacyRef);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(summaryOutPath))!);
            File.WriteAllText(summaryOutPath, md);
            Console.WriteLine($"Wrote {summaryOutPath}");
        }

        return 0;
    }
}

/// <summary>Matches eng/manifests/source-disposition.schema.json's "entry" definition.</summary>
public sealed class InventoryEntry
{
    public required string Path { get; init; }
    public required string SourceRef { get; init; } // "sourceBaseline" | "behaviorBaseline" | "both"
    public string? Area { get; init; }
    public string? Package { get; init; } // one of the schema's package enum values, or "none"
    public required string Kind { get; init; } // "tizen-specific" | "shared-conditional" | "project" | "asset"
    public required string Disposition { get; init; } // "move" | "rename" | "rebuild" | "keep-upstream" | "exclude"
    public string? TargetPath { get; init; }
    public string? TargetNamespace { get; init; }
    public string? CollisionRisk { get; init; } // "none" | "namespace-only" | "type-name" | "assembly-identity"
    public string? Notes { get; init; }
}

/// <summary>
/// Path-prefix remap table mirroring eng/import/normalize-layout.sh's `move_children` calls.
/// Keep these two files in sync: if normalize-layout.sh's mapping changes, update this table too.
/// </summary>
internal static class LayoutMap
{
    public sealed record Mapping(string SourcePrefix, string? TargetPrefix, string Package, string Area);

    public static readonly IReadOnlyList<Mapping> Mappings =
    [
        new("src/Core/maps/src/", "src/Maui.Tizen.Maps/Core/", "Maui.Tizen.Maps", "Maps"),
        new("src/Controls/Maps/src/", "src/Maui.Tizen.Maps/Controls/", "Maui.Tizen.Maps", "Maps"),
        new("src/Core/src/Handlers/", "src/Maui.Tizen.Core/Handlers/", "Maui.Tizen.Core", "Core.Handlers"),
        new("src/Core/src/", "src/Maui.Tizen.Core/", "Maui.Tizen.Core", "Core.Platform"),
        new("src/Controls/src/Core/", "src/Maui.Tizen.Controls/Core/", "Maui.Tizen.Controls", "Controls"),
        new("src/Controls/src/Xaml/", "src/Maui.Tizen.Controls/Xaml/", "Maui.Tizen.Controls", "Controls"),
        new("src/Essentials/src/", "src/Maui.Tizen.Essentials/", "Maui.Tizen.Essentials", "Essentials"),
        new("src/BlazorWebView/src/Maui/", "src/Maui.Tizen.BlazorWebView/", "Maui.Tizen.BlazorWebView", "BlazorWebView"),
        new("src/Graphics/src/Graphics.Skia/", "src/Maui.Tizen.Graphics/Graphics.Skia/", "Maui.Tizen.Graphics", "Graphics"),
        new("src/Graphics/src/Graphics/", "src/Maui.Tizen.Graphics/Graphics/", "Maui.Tizen.Graphics", "Graphics"),
        new("src/SingleProject/Resizetizer/src/", "src/Maui.Tizen.Build.Tasks/", "Maui.Tizen.Build.Tasks", "Build.Tasks"),
        new("src/Controls/samples/", "samples/Controls/", "none", "Samples"),
        new("src/Essentials/samples/", "samples/Essentials/", "none", "Samples"),
        new("src/Graphics/samples/", "samples/Graphics/", "none", "Samples"),
        new("src/Controls/tests/", "tests/Controls/", "none", "Tests"),
        new("eng/common/cross/", "eng/cross/", "none", "Eng"),
    ];

    public static Mapping? Match(string relativePath)
    {
        Mapping? best = null;
        foreach (var m in Mappings)
        {
            if (relativePath.StartsWith(m.SourcePrefix, StringComparison.Ordinal) &&
                (best is null || m.SourcePrefix.Length > best.SourcePrefix.Length))
            {
                best = m;
            }
        }
        return best;
    }

    // Areas with no normalize-layout.sh mapping yet, but a documented eventual home (see
    // docs/architecture.md's package layout table and docs/migration.md's open decisions).
    public static (string Area, string Package)? MatchUnmapped(string relativePath)
    {
        if (relativePath.StartsWith("src/Templates/", StringComparison.Ordinal))
        {
            return ("Templates", "Maui.Tizen.Templates");
        }

        // The legacy top-level Xamarin.Forms.Platform.Compatibility stack. Distinct from
        // src/Controls/src/Core/Compatibility/** (still present on net11.0, handled by the
        // "src/Controls/src/Core/" mapping above as part of Maui.Tizen.Controls).
        if (relativePath.StartsWith("src/Compatibility/", StringComparison.Ordinal))
        {
            return ("Compatibility", "Maui.Tizen.Compatibility");
        }

        return null;
    }
}

internal sealed partial class Scanner
{
    private static readonly string[] SkipDirNames = [".git", "bin", "obj", "artifacts", "node_modules", ".vs", ".vscode"];

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".xaml" };
    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".targets", ".props" };

    [GeneratedRegex(@"#\s*(if|elif)\b[^\n]*\b(__)?TIZEN(__)?\b", RegexOptions.Multiline)]
    private static partial Regex TizenIfDefRegex();

    [GeneratedRegex(@"\bDevicePlatform\s*\.\s*Tizen\b")]
    private static partial Regex DevicePlatformTizenRegex();

    [GeneratedRegex(@"\bOperatingSystem\s*\.\s*IsTizen\b")]
    private static partial Regex OperatingSystemIsTizenRegex();

    [GeneratedRegex(@"\bPlatformSupport\s*\.\s*Tizen\b|\bKnownPlatform\s*\.\s*Tizen\b|\bTargetPlatform\s*\.\s*Tizen\b")]
    private static partial Regex PlatformIdentityTizenRegex();

    [GeneratedRegex(@"<TargetFrameworks?[^>]*>[^<]*tizen[^<]*</TargetFrameworks?>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworksTizenRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^Microsoft\.Maui(\.[A-Za-z0-9_]+)*$")]
    private static partial Regex MicrosoftMauiNamespaceRegex();

    /// <summary>
    /// Scans both baselines together, as docs/migration.md requires: a net11-only pass silently
    /// under-reports by the ~76 Tizen-named files under the legacy top-level src/Compatibility/**
    /// stack, which was deleted upstream before net11.0 and only exists at 9.0.120.
    /// </summary>
    public IEnumerable<InventoryEntry> Scan(string primaryRoot, string legacyRoot)
    {
        primaryRoot = Path.GetFullPath(primaryRoot);
        legacyRoot = Path.GetFullPath(legacyRoot);

        foreach (var file in EnumerateFiles(primaryRoot))
        {
            var relative = ToRelative(primaryRoot, file);
            var legacyCounterpart = Path.Combine(legacyRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var existsInLegacy = File.Exists(legacyCounterpart);
            var entry = ClassifyFile(relative, file, sourceRef: existsInLegacy ? "both" : "sourceBaseline");
            if (entry is not null)
            {
                yield return entry;
            }
        }

        foreach (var file in EnumerateFiles(legacyRoot))
        {
            var relative = ToRelative(legacyRoot, file);
            if (!IsTizenPath(relative))
            {
                continue; // legacy-only recovery is scoped to Tizen-named files; shared-conditional
                          // drift between refs is out of scope for Phase 0.
            }

            var primaryCounterpart = Path.Combine(primaryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(primaryCounterpart))
            {
                continue; // already yielded above with sourceRef "both".
            }

            var entry = ClassifyLegacyOnlyFile(relative, file);
            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    private InventoryEntry? ClassifyLegacyOnlyFile(string relative, string fullPath)
    {
        var category = CategoryFor(relative);
        var unmapped = LayoutMap.MatchUnmapped(relative);

        string area, package;
        if (unmapped is { } u)
        {
            (area, package) = u;
        }
        else
        {
            var mapping = LayoutMap.Match(relative);
            area = mapping?.Area ?? "Other";
            package = mapping?.Package ?? "none";
        }

        // Per the Compatibility layer's disposition question is not "is it on net11.0" (it
        // isn't) but "does any net11 Tizen handler depend on implementation that exists only
        // here?" -- an audit this generator cannot perform. "pending-audit" makes the eventual
        // drop (or partial move) a reviewed conclusion rather than a default the generator
        // asserted on its own. See docs/migration.md's Compatibility open decision.
        var disposition = area == "Compatibility" ? "pending-audit" : "exclude";
        var notes = area == "Compatibility"
            ? "Present at 9.0.120 only (legacy top-level src/Compatibility/** stack, deleted upstream before net11.0). Audit: does any net11 Tizen handler depend on implementation that exists only here? Move only what is genuinely required; per docs/migration.md the expected outcome is that this package is not shipped at all."
            : "Present at 9.0.120 only; not found at the net11.0 source baseline.";

        return new InventoryEntry
        {
            Path = relative,
            SourceRef = "behaviorBaseline",
            Area = area,
            Package = package,
            Kind = category == "project" ? "project" : category == "asset" ? "asset" : "tizen-specific",
            Disposition = disposition,
            TargetPath = null,
            TargetNamespace = null,
            CollisionRisk = "none",
            Notes = notes,
        };
    }

    private InventoryEntry? ClassifyFile(string relative, string fullPath, string sourceRef)
    {
        var isTizenPath = IsTizenPath(relative);
        var category = CategoryFor(relative);

        string? text = null;
        var signals = new List<string>();

        var isTextFile = category is "code" or "project";
        if (isTextFile)
        {
            text = File.ReadAllText(fullPath);

            if (TizenIfDefRegex().IsMatch(text)) signals.Add("#if TIZEN");
            if (DevicePlatformTizenRegex().IsMatch(text)) signals.Add("DevicePlatform.Tizen");
            if (OperatingSystemIsTizenRegex().IsMatch(text)) signals.Add("OperatingSystem.IsTizen");
            if (PlatformIdentityTizenRegex().IsMatch(text)) signals.Add("platform-identity:Tizen");
            if (category == "project" && TargetFrameworksTizenRegex().IsMatch(text)) signals.Add("TargetFrameworks:tizen");
        }

        var hasSharedSignal = !isTizenPath && signals.Count > 0;

        if (!isTizenPath && !hasSharedSignal)
        {
            return null; // not relevant to the Tizen inventory.
        }

        // kind follows file type first (project/asset), then path/content (tizen-specific vs
        // shared-conditional) for code files -- matches source-disposition.schema.json's kind
        // enum, where "project" and "asset" are file-type categories orthogonal to the
        // Tizen-vs-shared distinction that only applies to code.
        var kind = category switch
        {
            "project" => "project",
            "asset" => "asset",
            _ => isTizenPath ? "tizen-specific" : "shared-conditional",
        };

        var unmapped = LayoutMap.MatchUnmapped(relative);
        string area, package;
        LayoutMap.Mapping? mapping = null;
        if (unmapped is { } u)
        {
            (area, package) = u;
        }
        else
        {
            mapping = LayoutMap.Match(relative);
            area = mapping?.Area ?? "Other";
            package = mapping?.Package ?? "none";
        }

        var isLegacyCompatibilityRenderer = relative.Contains("/Compatibility/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("src/Compatibility/", StringComparison.OrdinalIgnoreCase);

        string disposition;
        string? targetPath = null;
        string collisionRisk;
        string? notes = null;

        if (kind == "tizen-specific")
        {
            if (isLegacyCompatibilityRenderer)
            {
                targetPath = ComputeTargetPath(relative, mapping);
                if (targetPath is not null)
                {
                    disposition = "rebuild";
                    notes = "Legacy Xamarin.Forms-era Compatibility renderer shim; standalone rewrite recommended against the current handler architecture rather than a verbatim copy.";
                }
                else
                {
                    disposition = "keep-upstream";
                    notes = "Legacy Xamarin.Forms-era Compatibility renderer shim with no target project mapping yet; standalone rewrite recommended once its destination is decided.";
                }
                collisionRisk = "none";
            }
            else
            {
                targetPath = ComputeTargetPath(relative, mapping);
                if (targetPath is not null)
                {
                    disposition = "move";
                    collisionRisk = area is "Core.Handlers" or "Core.Platform" or "Controls" or "Maps" && category == "code"
                        ? "type-name" // see docs/architecture.md Rule 1-3: likely a partial fragment of a type that also exists upstream.
                        : "none";
                }
                else
                {
                    disposition = "keep-upstream";
                    notes = "Tizen-specific file with no target project mapping yet (see eng/import/normalize-layout.sh); revisit once its destination is decided.";
                    collisionRisk = "none";
                }
            }
        }
        else if (kind == "shared-conditional")
        {
            var tizenSignalCount = signals.Count;
            var conditionalLineCount = text is null ? 0 : TizenIfDefRegex().Matches(text).Count;
            var warrantsExtraction = tizenSignalCount > 1 || conditionalLineCount > 1;

            // "rebuild" requires a proposed targetPath (schema allOf rule): propose the new
            // standalone implementation lives alongside the rest of its area's target project,
            // since there is no verbatim source file to relocate.
            targetPath = warrantsExtraction ? ComputeTargetPath(relative, mapping) : null;

            if (warrantsExtraction && targetPath is not null)
            {
                disposition = "rebuild";
                notes = "Shared file with a non-trivial Tizen conditional branch; per docs/architecture.md Rule 4, the file cannot be copied wholesale (the non-Tizen branches belong to the neutral MAUI assembly) -- extract the Tizen branch as a standalone implementation here.";
            }
            else
            {
                disposition = "keep-upstream";
                notes = warrantsExtraction
                    ? "Shared file with a non-trivial Tizen conditional branch, but its area has no target project mapping yet; extraction recommended once one exists."
                    : "Small/isolated Tizen conditional; candidate to clean up from dotnet/maui once Tizen ships independently, but not large enough to warrant extracting a standalone implementation yet.";
            }
            collisionRisk = "none";
        }
        else if (kind == "project")
        {
            if (isTizenPath)
            {
                targetPath = ComputeTargetPath(relative, mapping);
                disposition = targetPath is not null ? "move" : "keep-upstream";
                notes = targetPath is null ? "Tizen-named project file with no target project mapping yet." : null;
            }
            else
            {
                disposition = "keep-upstream";
                notes = "Shared project file referencing a Tizen target framework; stays upstream rather than being copied wholesale.";
            }
            collisionRisk = "none";
        }
        else // asset
        {
            if (isLegacyCompatibilityRenderer)
            {
                disposition = "exclude";
                notes = "Legacy Compatibility-era asset; excluded pending the Compatibility layer decision in docs/migration.md.";
            }
            else
            {
                targetPath = ComputeTargetPath(relative, mapping);
                disposition = targetPath is not null ? "move" : "keep-upstream";
                notes = targetPath is null ? "Tizen-named asset with no target project mapping yet." : null;
            }
            collisionRisk = "none";
        }

        string? targetNamespace = null;
        if (text is not null && (disposition is "move" or "rename") && category == "code")
        {
            var m = NamespaceRegex().Match(text);
            // Only recorded when it matches the schema's targetNamespace pattern (preserved
            // Microsoft.Maui.* namespaces). Samples/tests under their own app namespace (e.g.
            // GraphicsTester.Skia.Tizen) intentionally leave this null.
            if (m.Success && MicrosoftMauiNamespaceRegex().IsMatch(m.Groups[1].Value))
            {
                targetNamespace = m.Groups[1].Value; // preserved as-is; see docs/architecture.md Rule 1/2.
            }
        }

        return new InventoryEntry
        {
            Path = relative,
            SourceRef = sourceRef,
            Area = area,
            Package = package,
            Kind = kind,
            Disposition = disposition,
            TargetPath = targetPath,
            TargetNamespace = targetNamespace,
            CollisionRisk = collisionRisk,
            Notes = notes,
        };
    }

    private static string? ComputeTargetPath(string relative, LayoutMap.Mapping? mapping)
    {
        if (mapping?.TargetPrefix is null)
        {
            return null;
        }
        return mapping.TargetPrefix + relative[mapping.SourcePrefix.Length..];
    }

    private static string CategoryFor(string relative)
    {
        var ext = Path.GetExtension(relative);
        if (ProjectExtensions.Contains(ext)) return "project";
        if (CodeExtensions.Contains(ext)) return "code";
        return "asset";
    }

    private static bool IsTizenPath(string relativePath) =>
        relativePath.Contains("tizen", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            IEnumerable<string> files;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (SkipDirNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                stack.Push(sub);
            }

            foreach (var f in files)
            {
                yield return f;
            }
        }
    }

    private static string ToRelative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
}

internal static class SummaryWriter
{
    public static string Write(List<InventoryEntry> entries, string primaryRef, string legacyRef)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maui.Tizen source disposition summary");
        sb.AppendLine();
        sb.AppendLine("Generated by `eng/tools/SourceInventory`. Do not hand-edit; regenerate via");
        sb.AppendLine("`eng/scripts/generate-source-inventory.ps1` and commit the result.");
        sb.AppendLine();
        sb.AppendLine($"* sourceBaseline: `{primaryRef}`");
        sb.AppendLine($"* behaviorBaseline: `{legacyRef}`");
        sb.AppendLine($"* Total entries: **{entries.Count}**");
        sb.AppendLine();

        AppendCountTable(sb, "By area", entries, e => e.Area ?? "(none)");
        AppendCountTable(sb, "By kind", entries, e => e.Kind);
        AppendCountTable(sb, "By disposition", entries, e => e.Disposition);
        AppendCountTable(sb, "By sourceRef", entries, e => e.SourceRef);

        sb.AppendLine("## Area x disposition");
        sb.AppendLine();
        sb.AppendLine("| Area | move | rename | rebuild | keep-upstream | exclude |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var area in entries.Select(e => e.Area ?? "(none)").Distinct().OrderBy(a => a, StringComparer.Ordinal))
        {
            var byDisp = entries.Where(e => (e.Area ?? "(none)") == area).GroupBy(e => e.Disposition).ToDictionary(g => g.Key, g => g.Count());
            int C(string d) => byDisp.GetValueOrDefault(d, 0);
            sb.AppendLine($"| {area} | {C("move")} | {C("rename")} | {C("rebuild")} | {C("keep-upstream")} | {C("exclude")} |");
        }
        sb.AppendLine();

        var collisionCandidates = entries.Where(e => e.CollisionRisk == "type-name").ToList();
        sb.AppendLine($"## Type-collision review candidates ({collisionCandidates.Count})");
        sb.AppendLine();
        sb.AppendLine("Files flagged `\"collisionRisk\": \"type-name\"` -- neutral (non-Tizen-branded)");
        sb.AppendLine("handler/service/platform files under an area that also ships a non-Tizen");
        sb.AppendLine("implementation upstream. See `docs/architecture.md`'s type collision rules.");
        sb.AppendLine();
        foreach (var e in collisionCandidates.Take(50))
        {
            sb.AppendLine($"* `{e.Path}`" + (e.TargetNamespace is null ? "" : $" (`{e.TargetNamespace}`)"));
        }
        if (collisionCandidates.Count > 50)
        {
            sb.AppendLine($"* ... and {collisionCandidates.Count - 50} more (see the full manifest).");
        }

        return sb.ToString();
    }

    private static void AppendCountTable(StringBuilder sb, string title, List<InventoryEntry> entries, Func<InventoryEntry, string> keySelector)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine("| Value | Entries |");
        sb.AppendLine("|---|---:|");
        foreach (var g in entries.GroupBy(keySelector).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"| {g.Key} | {g.Count()} |");
        }
        sb.AppendLine();
    }
}
