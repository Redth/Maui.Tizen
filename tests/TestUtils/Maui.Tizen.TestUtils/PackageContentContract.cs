using System.Text.RegularExpressions;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// A declarative, reviewable description of what a package must and must not contain.
/// </summary>
/// <remarks>
/// <para>
/// Contracts live in <c>eng/validation/package-contents/&lt;PackageId&gt;.contract.txt</c>. The format is
/// intentionally plain text so that a change to shipped package layout shows up as a readable diff
/// in code review rather than being buried in test code:
/// </para>
/// <code>
/// # comments start with '#'
/// require  lib/net11.0-tizen11.0/Microsoft.Maui.Tizen.dll
/// require  buildTransitive/**/*.targets
/// forbid   **/*.pdb
/// forbid   lib/net6.0/**
/// </code>
/// <para>
/// <c>require</c> patterns must match at least one entry; <c>forbid</c> patterns must match none.
/// Globs support <c>*</c> (within a segment) and <c>**</c> (across segments).
/// </para>
/// </remarks>
public sealed class PackageContentContract
{
    PackageContentContract(string packageId, string sourcePath, IReadOnlyList<ContractRule> rules)
    {
        PackageId = packageId;
        SourcePath = sourcePath;
        Rules = rules;
    }

    public string PackageId { get; }

    public string SourcePath { get; }

    public IReadOnlyList<ContractRule> Rules { get; }

    /// <summary>Absolute path of the contract file for <paramref name="packageId"/>.</summary>
    public static string PathFor(string packageId) =>
        Path.Combine(RepoLayout.PackageContentContracts, $"{packageId}.contract.txt");

    /// <summary>Enumerates every contract declared in the repository.</summary>
    public static IReadOnlyList<string> EnumerateDeclaredPackageIds()
    {
        if (!Directory.Exists(RepoLayout.PackageContentContracts))
            return [];

        const string Suffix = ".contract.txt";

        return [.. Directory
            .EnumerateFiles(RepoLayout.PackageContentContracts, "*" + Suffix)
            .Select(p => Path.GetFileName(p))
            .Select(name => name[..^Suffix.Length])
            .OrderBy(p => p, StringComparer.Ordinal)];
    }

    public static PackageContentContract Load(string packageId)
    {
        var path = PathFor(packageId);

        if (!File.Exists(path))
            throw new FileNotFoundException($"No package-content contract for '{packageId}' at {path}.", path);

        return Parse(packageId, path, File.ReadAllLines(path));
    }

    public static PackageContentContract Parse(string packageId, string sourcePath, IEnumerable<string> lines)
    {
        var rules = new List<ContractRule>();
        var lineNumber = 0;

        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new FormatException(
                    $"{sourcePath}({lineNumber}): expected '<require|forbid> <glob>' but found '{line}'.");
            }

            var kind = parts[0].ToLowerInvariant() switch
            {
                "require" => ContractRuleKind.Require,
                "forbid" => ContractRuleKind.Forbid,
                _ => throw new FormatException(
                    $"{sourcePath}({lineNumber}): unknown rule '{parts[0]}'. Expected 'require' or 'forbid'."),
            };

            rules.Add(new ContractRule(kind, parts[1].Trim(), lineNumber));
        }

        return new PackageContentContract(packageId, sourcePath, rules);
    }

    /// <summary>Evaluates this contract against a package's entry list.</summary>
    public ContractEvaluation Evaluate(IReadOnlyList<string> entries)
    {
        var unsatisfied = new List<ContractRule>();
        var violations = new List<ContractViolation>();

        foreach (var rule in Rules)
        {
            var matches = entries.Where(e => GlobMatcher.IsMatch(rule.Pattern, e)).ToList();

            switch (rule.Kind)
            {
                case ContractRuleKind.Require when matches.Count == 0:
                    unsatisfied.Add(rule);
                    break;
                case ContractRuleKind.Forbid when matches.Count > 0:
                    violations.Add(new ContractViolation(rule, matches));
                    break;
            }
        }

        return new ContractEvaluation(this, unsatisfied, violations);
    }
}

public enum ContractRuleKind
{
    Require,
    Forbid,
}

/// <param name="LineNumber">Line in the contract file, so failures point at the rule to fix.</param>
public sealed record ContractRule(ContractRuleKind Kind, string Pattern, int LineNumber);

public sealed record ContractViolation(ContractRule Rule, IReadOnlyList<string> MatchedEntries);

public sealed record ContractEvaluation(
    PackageContentContract Contract,
    IReadOnlyList<ContractRule> UnsatisfiedRequirements,
    IReadOnlyList<ContractViolation> Violations)
{
    public bool Passed => UnsatisfiedRequirements.Count == 0 && Violations.Count == 0;

    /// <summary>A reviewer-friendly description of everything that went wrong.</summary>
    public string Describe(IReadOnlyList<string> actualEntries)
    {
        if (Passed)
            return $"'{Contract.PackageId}' satisfies {Contract.Rules.Count} content rule(s).";

        var lines = new List<string>
        {
            $"Package '{Contract.PackageId}' does not match {RepoLayout.Relative(Contract.SourcePath)}:",
        };

        foreach (var rule in UnsatisfiedRequirements)
            lines.Add($"  [line {rule.LineNumber}] require '{rule.Pattern}' matched no entry.");

        foreach (var violation in Violations)
        {
            lines.Add($"  [line {violation.Rule.LineNumber}] forbid '{violation.Rule.Pattern}' matched:");
            lines.AddRange(violation.MatchedEntries.Select(e => $"      {e}"));
        }

        lines.Add("  Actual package entries:");
        lines.AddRange(actualEntries.Select(e => $"      {e}"));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Translates the contract glob dialect into anchored regular expressions.</summary>
public static class GlobMatcher
{
    static readonly Dictionary<string, Regex> Cache = [];
    static readonly Lock CacheLock = new();

    public static bool IsMatch(string pattern, string path)
    {
        Regex regex;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(pattern, out regex!))
            {
                regex = new Regex(Translate(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Cache[pattern] = regex;
            }
        }

        return regex.IsMatch(path.Replace('\\', '/'));
    }

    internal static string Translate(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var builder = new System.Text.StringBuilder("^");

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];

            if (c == '*')
            {
                var isDoubleStar = i + 1 < normalized.Length && normalized[i + 1] == '*';
                if (isDoubleStar)
                {
                    i++;

                    // '**/' should also match zero directories, so 'a/**/b.txt' matches 'a/b.txt'.
                    if (i + 1 < normalized.Length && normalized[i + 1] == '/')
                    {
                        i++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }

                continue;
            }

            builder.Append(c == '?' ? "[^/]" : Regex.Escape(c.ToString()));
        }

        return builder.Append('$').ToString();
    }
}
