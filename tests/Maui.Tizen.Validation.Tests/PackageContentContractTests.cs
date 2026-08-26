namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Self-tests for the package-content contract parser and its glob dialect.
/// </summary>
public class PackageContentContractTests
{
    static PackageContentContract Parse(params string[] lines) =>
        PackageContentContract.Parse("Test.Package", "in-memory.contract.txt", lines);

    [Theory]
    [InlineData("lib/net11.0-tizen11.0/A.dll", "lib/net11.0-tizen11.0/A.dll", true)]
    [InlineData("lib/*/A.dll", "lib/net11.0-tizen11.0/A.dll", true)]
    [InlineData("lib/*/A.dll", "lib/net11.0-tizen11.0/sub/A.dll", false)]
    [InlineData("lib/**/A.dll", "lib/net11.0-tizen11.0/sub/A.dll", true)]
    [InlineData("**/*.pdb", "lib/net11.0/A.pdb", true)]
    [InlineData("**/*.pdb", "lib/net11.0/A.dll", false)]
    [InlineData("buildTransitive/**", "buildTransitive/Maui.Tizen.targets", true)]
    public void Glob_MatchesExpectedPaths(string pattern, string path, bool expected) =>
        Assert.Equal(expected, GlobMatcher.IsMatch(pattern, path));

    [Fact]
    public void Glob_DoubleStarSlash_AlsoMatchesZeroDirectories()
    {
        // 'a/**/b.txt' must match 'a/b.txt'; otherwise every contract needs two rules per path.
        Assert.True(GlobMatcher.IsMatch("a/**/b.txt", "a/b.txt"));
        Assert.True(GlobMatcher.IsMatch("a/**/b.txt", "a/c/b.txt"));
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var contract = Parse("# a comment", string.Empty, "   ", "require lib/**");

        Assert.Single(contract.Rules);
        Assert.Equal(ContractRuleKind.Require, contract.Rules[0].Kind);
    }

    [Fact]
    public void Parse_RejectsUnknownRuleKind()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("allow lib/**"));
        Assert.Contains("allow", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsMalformedLine()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("require"));
        Assert.Contains("require", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReportsLineNumbers()
    {
        var contract = Parse("# header", "require lib/**", "forbid **/*.pdb");

        Assert.Equal(2, contract.Rules[0].LineNumber);
        Assert.Equal(3, contract.Rules[1].LineNumber);
    }

    [Fact]
    public void Evaluate_SatisfiedContract_Passes()
    {
        var contract = Parse("require lib/**/*.dll", "forbid **/*.pdb");
        var evaluation = contract.Evaluate(["lib/net11.0-tizen11.0/A.dll", "Test.Package.nuspec"]);

        Assert.True(evaluation.Passed, evaluation.Describe(["lib/net11.0-tizen11.0/A.dll"]));
    }

    [Fact]
    public void Evaluate_UnmatchedRequirement_Fails()
    {
        var contract = Parse("require lib/**/*.dll");
        var evaluation = contract.Evaluate(["Test.Package.nuspec"]);

        Assert.False(evaluation.Passed);
        Assert.Single(evaluation.UnsatisfiedRequirements);

        // The failure must name the rule's line so the fix location is unambiguous.
        Assert.Contains("line 1", evaluation.Describe(["Test.Package.nuspec"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ForbiddenEntry_FailsAndListsMatches()
    {
        var contract = Parse("forbid **/*.pdb");
        var entries = new[] { "lib/net11.0/A.dll", "lib/net11.0/A.pdb" };

        var evaluation = contract.Evaluate(entries);

        Assert.False(evaluation.Passed);
        Assert.Contains("lib/net11.0/A.pdb", evaluation.Describe(entries), StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredContracts_AreAllParseable()
    {
        var ids = PackageContentContract.EnumerateDeclaredPackageIds();

        Assert.SkipWhen(ids.Count == 0, "No package-content contracts are declared yet.");

        foreach (var id in ids)
        {
            var contract = PackageContentContract.Load(id);
            Assert.True(
                contract.Rules.Count > 0,
                $"'{id}' declares a contract with no rules, which would silently assert nothing.");
        }
    }
}
