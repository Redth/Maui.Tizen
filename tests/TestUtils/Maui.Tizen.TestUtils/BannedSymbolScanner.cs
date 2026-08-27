using System.Text.RegularExpressions;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Scans compiled C# source for symbols that are unavailable or deprecated on the target API level.
/// </summary>
public static class BannedSymbolScanner
{
    static readonly Dictionary<string, Regex> Cache = [];
    static readonly Lock CacheLock = new();

    /// <summary>Scans a single file's text.</summary>
    /// <param name="path">Used only to report the location; the file is not read again.</param>
    public static IReadOnlyList<BannedSymbolViolation> ScanText(string path, string text, IEnumerable<BannedSymbol> rules)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Comments and literals are blanked out but keep their length, so offsets below still map
        // back to real line and column numbers in the original file.
        var code = CSharpSourceText.StripCommentsAndLiterals(text);
        var violations = new List<BannedSymbolViolation>();

        foreach (var rule in rules)
        {
            foreach (var symbol in rule.Symbols)
            {
                foreach (Match match in PatternFor(symbol).Matches(code))
                {
                    if (IsAllowed(code, match, rule))
                        continue;

                    var (line, column) = Position(code, match.Index);
                    violations.Add(new BannedSymbolViolation(rule, symbol, path, line, column, LineText(text, line)));
                }
            }
        }

        return violations;
    }

    /// <summary>Scans a set of files.</summary>
    public static IReadOnlyList<BannedSymbolViolation> ScanFiles(IEnumerable<string> paths, IEnumerable<BannedSymbol> rules)
    {
        var ruleList = rules.ToList();
        var violations = new List<BannedSymbolViolation>();

        foreach (var path in paths)
            violations.AddRange(ScanText(path, File.ReadAllText(path), ruleList));

        return violations;
    }

    /// <summary>
    /// Matches the symbol as a whole identifier path.
    /// </summary>
    /// <remarks>
    /// The trailing <c>(?!\w)</c> is what keeps a <c>MapService</c> ban from firing on
    /// <c>MapServiceToken</c>, the compatibility shim the ban is specifically meant to preserve. A
    /// naive substring search would flag it and there would be no correct way to silence that
    /// without disabling the rule.
    /// </remarks>
    static Regex PatternFor(string symbol)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(symbol, out var cached))
                return cached;

            var pattern = $@"(?<!\w){Regex.Escape(symbol)}(?!\w)";
            var regex = new Regex(pattern, RegexOptions.CultureInvariant);
            Cache[symbol] = regex;
            return regex;
        }
    }

    /// <summary>
    /// True when the match is part of an explicitly allowed longer identifier.
    /// </summary>
    /// <remarks>
    /// Redundant with the <c>(?!\w)</c> boundary for the current rules, and kept deliberately: the
    /// allow-list is the reviewable record of which look-alike identifiers are intentional, and it
    /// still holds if a future rule needs a looser pattern.
    /// </remarks>
    static bool IsAllowed(string code, Match match, BannedSymbol rule)
    {
        foreach (var allowed in rule.AllowedIdentifiers)
        {
            if (match.Index + allowed.Length <= code.Length &&
                code.AsSpan(match.Index, allowed.Length).SequenceEqual(allowed))
            {
                return true;
            }
        }

        return false;
    }

    static (int Line, int Column) Position(string text, int index)
    {
        var line = 1;
        var lastNewline = -1;

        for (var i = 0; i < index; i++)
        {
            if (text[i] != '\n')
                continue;

            line++;
            lastNewline = i;
        }

        return (line, index - lastNewline);
    }

    static string LineText(string text, int line)
    {
        var lines = text.Split('\n');
        return line - 1 < lines.Length ? lines[line - 1].TrimEnd('\r').Trim() : string.Empty;
    }
}

public sealed record BannedSymbolViolation(
    BannedSymbol Rule,
    string Symbol,
    string Path,
    int Line,
    int Column,
    string LineText)
{
    /// <summary>A failure message that names the rule, the location and the fix.</summary>
    public string Describe()
    {
        var replacement = Rule.Replacement is { Length: > 0 } r
            ? $"{Environment.NewLine}      Use {r} instead."
            : string.Empty;

        return
            $"  {RepoLayout.Relative(Path)}({Line},{Column}): '{Symbol}' is banned on " +
            $"{Api15Contract.Document.ApiLevel} [{Rule.Id}]{Environment.NewLine}" +
            $"      {LineText}{Environment.NewLine}" +
            $"      {Rule.Reason}{replacement}";
    }
}
