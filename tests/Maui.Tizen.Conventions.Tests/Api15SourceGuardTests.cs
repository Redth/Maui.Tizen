namespace Maui.Tizen.Conventions.Tests;

/// <summary>
/// API15 source guards.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, both derived from the pinned reference pack rather than from prose:
/// <c>Tizen.Maps</c>/<c>MapService</c> no longer exist, and <c>Tizen.NUI.Window.Instance</c> is
/// <c>[Obsolete]</c> in favour of <c>Window.Default</c>. With <c>TreatWarningsAsErrors</c> in CI the
/// second is already fatal; banning it at source level means the failure names the replacement
/// instead of surfacing as a bare CS0618 on a machine that cannot even build the project.
/// </para>
/// <para>
/// Scope is deliberately narrow: only files a project actually compiles. See
/// <see cref="CompiledSourceInventory"/> for why scanning the raw historical imports would be
/// actively harmful.
/// </para>
/// </remarks>
public class Api15SourceGuardTests
{
    static IReadOnlyList<BannedSymbol> Rules => Api15Contract.Document.BannedSymbols;

    // -----------------------------------------------------------------------------------------
    // The guard itself.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void CompiledProductSource_UsesNoApiLevelBannedSymbols()
    {
        var sets = CompiledSourceInventory.ForAllProductProjects();
        var compiled = sets.Where(s => !s.CompilesNothing).ToList();

        ValidationSkip.When(
            compiled.Count == 0,
            "No project under src/ compiles any source yet. The imported packages set " +
            "EnableDefaultCompileItems=false, so their files are <None> and are intentionally out " +
            "of scope; this guard activates per project as each one opts into compiling.");

        var violations = BannedSymbolScanner
            .ScanFiles(compiled.SelectMany(s => s.Files), Rules)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"""
             {violations.Count} API15 violation(s) in compiled source:

             {string.Join(Environment.NewLine + Environment.NewLine, violations.Select(v => v.Describe()))}

             Scanned {compiled.Sum(s => s.Files.Count)} compiled file(s) across: {string.Join(", ", compiled.Select(s => s.ProjectName))}.
             """);
    }

    [Fact]
    public void ImportedPackagesThatCompileNothing_AreReportedNotScanned()
    {
        var sets = CompiledSourceInventory.ForAllProductProjects();
        ValidationSkip.When(sets.Count == 0, "No projects under src/.");

        // Not an assertion that they must stay unadopted - a record of which ones are, so the
        // scope of the guard above is visible rather than implicit.
        var unadopted = sets.Where(s => s.CompilesNothing).Select(s => s.ProjectName).ToList();
        var adopted = sets.Where(s => !s.CompilesNothing).ToList();

        Assert.True(
            sets.Count == unadopted.Count + adopted.Count,
            "Every project must be classified as either compiling source or not.");

        foreach (var set in adopted)
        {
            Assert.True(
                set.Files.All(File.Exists),
                $"'{set.ProjectName}' reports compiled files that do not exist on disk.");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Scanner behaviour. These make the guard trustworthy: a scanner that never fires is
    // indistinguishable from clean source.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Scanner_FlagsRemovedTizenMapsUsage()
    {
        const string Source =
            """
            using Tizen.Maps;

            class Geocoder
            {
                void Locate() => new MapService("app", "key").CreateGeocodeRequest("x");
            }
            """;

        var violations = BannedSymbolScanner.ScanText("Geocoder.cs", Source, Rules);

        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Equal("tizen-maps-removed", v.Rule.Id));
        Assert.Contains(violations, v => v.Symbol == "Tizen.Maps");
        Assert.Contains(violations, v => v.Symbol == "MapService");
    }

    [Fact]
    public void Scanner_DoesNotFlagTheMapServiceTokenCompatibilityShim()
    {
        // The whole reason 'MapService' is banned but 'MapServiceToken' is not: the shim is kept
        // deliberately so DI-bridge startup keeps working. A substring ban would flag it, and the
        // only way to silence that would be to drop the rule entirely.
        const string Source =
            """
            class Startup
            {
                public string? MapServiceToken { get; set; }

                void Configure(Options options) => options.MapServiceToken = "ignored";
            }
            """;

        Assert.Empty(BannedSymbolScanner.ScanText("Startup.cs", Source, Rules));
    }

    [Fact]
    public void Scanner_FlagsDeprecatedWindowInstance()
    {
        const string Source =
            """
            class Capture
            {
                void Grab() => Window.Instance.GetDefaultLayer();
            }
            """;

        var violation = Assert.Single(BannedSymbolScanner.ScanText("Capture.cs", Source, Rules));

        Assert.Equal("nui-window-instance-deprecated", violation.Rule.Id);
        Assert.Equal("Window.Default", violation.Rule.Replacement);
        Assert.Equal(3, violation.Line);
        Assert.Contains("Window.Default", violation.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_AcceptsTheReplacement()
    {
        const string Source =
            """
            class Capture
            {
                void Grab() => Window.Default.GetDefaultLayer();
            }
            """;

        Assert.Empty(BannedSymbolScanner.ScanText("Capture.cs", Source, Rules));
    }

    [Fact]
    public void Scanner_MatchesFullyQualifiedUsage()
    {
        const string Source = "class C { void M() => Tizen.NUI.Window.Instance.Show(); }";

        Assert.Single(BannedSymbolScanner.ScanText("C.cs", Source, Rules));
    }

    [Fact]
    public void Scanner_IgnoresCommentsAndStringLiterals()
    {
        // This repository's own documentation discusses both banned symbols at length. A scanner
        // that matched raw text would fail on the comments explaining the rules.
        const string Source =
            """
            class C
            {
                // Tizen.Maps was removed in API15; use nothing. Window.Instance is obsolete.
                /* MapService no longer exists. Window.Instance -> Window.Default. */
                const string Note = "Tizen.Maps and Window.Instance are gone";
                const string Verbatim = @"MapService: Window.Instance";
                void M() => System.Console.WriteLine($"Window.Instance {Note}");
            }
            """;

        Assert.Empty(BannedSymbolScanner.ScanText("C.cs", Source, Rules));
    }

    [Fact]
    public void Scanner_ReportsAccurateLineNumbersAfterStrippedContent()
    {
        // Positions must survive stripping, or every violation points at the wrong place.
        const string Source =
            """
            // line 1 comment mentioning Window.Instance
            /* line 2
               line 3 */
            class C
            {
                void M() => Window.Instance.Show();
            }
            """;

        var violation = Assert.Single(BannedSymbolScanner.ScanText("C.cs", Source, Rules));

        Assert.Equal(6, violation.Line);
        Assert.Contains("Window.Instance", violation.LineText, StringComparison.Ordinal);
    }

    [Fact]
    public void StripCommentsAndLiterals_PreservesLength()
    {
        const string Source = "var a = \"x\"; // c\n/* b */ var d = 'e';\n";

        Assert.Equal(Source.Length, CSharpSourceText.StripCommentsAndLiterals(Source).Length);
    }

    // -----------------------------------------------------------------------------------------
    // Compile-set resolution.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Inventory_TreatsDefaultCompileItemsFalseAsCompilingNothing()
    {
        using var workspace = TempWorkspace.Create("inventory-none");

        workspace.WriteFile("P/P.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile("P/Banned.cs", "class C { void M() => Window.Instance.Show(); }");

        var set = CompiledSourceInventory.ForProject(workspace.Combine("P", "P.csproj"));

        Assert.False(set.DefaultCompileItemsEnabled);
        Assert.True(set.CompilesNothing);
    }

    [Fact]
    public void Inventory_IncludesEverythingWhenDefaultItemsAreEnabled()
    {
        using var workspace = TempWorkspace.Create("inventory-default");

        workspace.WriteFile("P/P.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        workspace.WriteFile("P/A.cs", "class A;");
        workspace.WriteFile("P/Nested/B.cs", "class B;");

        var set = CompiledSourceInventory.ForProject(workspace.Combine("P", "P.csproj"));

        Assert.True(set.DefaultCompileItemsEnabled);
        Assert.Equal(2, set.Files.Count);
    }

    [Fact]
    public void Inventory_HonoursExplicitIncludeAndRemove()
    {
        using var workspace = TempWorkspace.Create("inventory-explicit");

        workspace.WriteFile("P/P.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Adopted/**/*.cs" />
                <Compile Remove="Adopted/Skipped.cs" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteFile("P/Adopted/A.cs", "class A;");
        workspace.WriteFile("P/Adopted/Skipped.cs", "class S;");
        workspace.WriteFile("P/NotAdopted/B.cs", "class B;");

        var set = CompiledSourceInventory.ForProject(workspace.Combine("P", "P.csproj"));

        Assert.Single(set.Files);
        Assert.EndsWith("A.cs", set.Files[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_InheritsDefaultCompileItemsFromAnImportedPropsFile()
    {
        // This is exactly how eng/targets/TizenPackage.props keeps the imported packages out of
        // scope, so the inheritance path needs its own coverage.
        using var workspace = TempWorkspace.Create("inventory-import");

        workspace.WriteFile("shared/Package.props",
            """
            <Project>
              <PropertyGroup>
                <EnableDefaultCompileItems Condition="'$(EnableDefaultCompileItems)' == ''">false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile("P/P.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="$(MSBuildThisFileDirectory)../shared/Package.props" />
            </Project>
            """);
        workspace.WriteFile("P/A.cs", "class A;");

        var set = CompiledSourceInventory.ForProject(workspace.Combine("P", "P.csproj"));

        Assert.False(set.DefaultCompileItemsEnabled);
        Assert.True(set.CompilesNothing);
    }

    [Fact]
    public void Inventory_ExcludesBuildOutput()
    {
        using var workspace = TempWorkspace.Create("inventory-output");

        workspace.WriteFile("P/P.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        workspace.WriteFile("P/A.cs", "class A;");
        workspace.WriteFile("P/obj/Debug/Generated.cs", "class G;");
        workspace.WriteFile("P/bin/Debug/Stale.cs", "class S;");

        var set = CompiledSourceInventory.ForProject(workspace.Combine("P", "P.csproj"));

        Assert.Single(set.Files);
    }

    // -----------------------------------------------------------------------------------------
    // Recorded API15 support decisions.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Contract_IsWellFormed()
    {
        var document = Api15Contract.Document;

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("API15", document.ApiLevel);
        Assert.NotEmpty(document.BannedSymbols);

        foreach (var rule in document.BannedSymbols)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.NotEmpty(rule.Symbols);

            // A ban without a reason is unactionable when it fires on someone else's PR.
            Assert.False(
                string.IsNullOrWhiteSpace(rule.Reason),
                $"Banned symbol rule '{rule.Id}' has no reason.");
        }

        Assert.Distinct(document.BannedSymbols.Select(r => r.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void Geocoding_IsRecordedAsUnsupportedOnApi15()
    {
        var geocoding = Assert.Single(
            Api15Contract.Document.UnsupportedServices,
            s => s.Contract == "IGeocoding");

        Assert.Equal("unsupported", geocoding.Status);

        // Not merely degraded: the platform API it was built on no longer exists, so it must not be
        // registered and must fail loudly rather than returning empty results that look like "no
        // match found".
        Assert.True(geocoding.DoNotRegisterInDi);
        Assert.Contains("PlatformNotSupportedException", geocoding.Behaviour, StringComparison.Ordinal);
        Assert.Contains("Tizen.Maps", geocoding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MapServiceToken_IsRecordedAsAcceptedNoOp()
    {
        var shim = Assert.Single(
            Api15Contract.Document.CompatibilityShims,
            s => s.Member == "MapServiceToken");

        Assert.True(shim.IsAcceptedNoOp);
        Assert.False(string.IsNullOrWhiteSpace(shim.Reason));

        // The shim must be reachable by the scanner's allow-list, otherwise the ban on MapService
        // would break the very startup path this record exists to preserve.
        var mapsRule = Assert.Single(Api15Contract.Document.BannedSymbols, r => r.Id == "tizen-maps-removed");
        Assert.Contains("MapServiceToken", mapsRule.AllowedIdentifiers, StringComparer.Ordinal);
    }
}
