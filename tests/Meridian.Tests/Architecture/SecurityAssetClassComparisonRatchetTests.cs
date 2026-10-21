using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet against comparing an asset class to its name. <see cref="SecurityAssetClassDescriptor"/>
/// declares what a class does (its lot unit, cash-flow behaviour, projection kind, profile
/// governance, equity classification, bond coupon structure, OCC symbols, rate quoting), so a
/// consumer asks the descriptor for the fact it needs instead of spelling the class. Every
/// <c>== "Bond"</c>, <c>string.Equals(x, "Equity", …)</c> or F# <c>= "Swap"</c> against a catalog
/// class name in <c>src/</c> fails here unless the file is listed in
/// <see cref="KnownComparisonSites"/> with the number of comparisons it still carries.
/// <para>
/// The list may only shrink: a file that drops a comparison must lower or remove its entry, and
/// no new file or higher count may be added. Vendor spellings ("us_option", "mleg") are not catalog
/// names and are not matched.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SecurityAssetClassComparisonRatchetTests
{
    private const string SourceOfTruth = "src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs";

    // Repository-relative, forward-slash path → comparisons still carried, by design. Each entry
    // compares a value that merely shares a spelling with a catalog class and is not a Security
    // Master asset class: a NetSuite account type ("Equity" as in retained earnings), an OpenFIGI
    // market sector ("Equity" as the vendor's instrument family) and a designer trade cell's
    // "instrument" vocabulary (Equity/ETF, the live engine's routing boundary). None has a catalog
    // fact to read.
    private static readonly IReadOnlyDictionary<string, int> KnownComparisonSites = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["src/Meridian.DataIntegration/AccountingSystem/NetSuiteAccountingProvider.cs"] = 1,
        ["src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiSymbolResolver.cs"] = 1,
        ["src/Meridian.Strategies/Live/Designer/DesignerStrategyPlan.cs"] = 1,
    };

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/", "/wwwroot/"];

    [Fact]
    public void NoSourceFile_ComparesAnAssetClassToItsName_BeyondTheKnownSites()
    {
        var counts = CountAssetClassComparisons();

        var offenders = counts
            .Where(entry => entry.Key != SourceOfTruth
                            && (!KnownComparisonSites.TryGetValue(entry.Key, out var allowed) || entry.Value > allowed))
            .Select(entry => $"{entry.Key} ({entry.Value})")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "comparing an asset class to its name bypasses the catalog; read the behaviour fact from "
            + "SecurityAssetClassDescriptor (LotUnit, CashFlowBehavior, ProjectionKind, RequiresGovernedProfile, "
            + "RequiresProfileReference, SupportsEquityClassification, CarriesBondCouponStructure, UsesOccOptionSymbols, "
            + "QuotesRatesAsFractions) or add the fact the site needs");
    }

    [Fact]
    public void KnownComparisonSites_StillCarryTheirComparisons_OrMustShrink()
    {
        var counts = CountAssetClassComparisons();

        foreach (var (site, allowed) in KnownComparisonSites)
        {
            counts.TryGetValue(site, out var actual);
            actual.Should().Be(allowed, $"{site} carries {actual} comparison(s); the ratchet entry must match so it only ever shrinks");
        }
    }

    [Fact]
    public void Catalog_IsTheOnlyPlaceAClassNameMayBeCompared()
    {
        KnownComparisonSites.Keys.Should().NotContain(SourceOfTruth, "the catalog is the source, not a comparison site");
    }

    [Theory]
    [InlineData("if (string.Equals(SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass, \"Repo\", StringComparison.Ordinal)) { }", ".cs", 1)]
    [InlineData("var x = assetClass == \"Bond\" || other != \"Equity\";", ".cs", 2)]
    [InlineData("if (\"Swap\" == security.AssetClass) { }", ".cs", 1)]
    [InlineData("if (assetClass.Equals(\"Option\", StringComparison.OrdinalIgnoreCase)) { }", ".cs", 1)]
    [InlineData("var assetClass = \"Bond\"; const string Name = \"Equity\";", ".cs", 0)]
    [InlineData("payload.AssetClass == \"us_option\"", ".cs", 0)]
    [InlineData("mappings.Where(m => string.Equals(m.Ticker?.Trim(), symbol, StringComparison.OrdinalIgnoreCase) && (exchange is null || string.Equals(m.Exchange, exchange)) && (m.Name, \"Equity\") is var _);", ".cs", 0)]
    [InlineData("if security.AssetClass = \"Bond\" then 1 else 0", ".fs", 1)]
    [InlineData("{ AssetClassName = \"Equity\"; Other = 1 }", ".fs", 0)]
    public void Counter_RecognisesComparisons_AndIgnoresAssignmentsAndVendorSpellings(string source, string extension, int expected)
        => CountComparisons(source, extension).Should().Be(expected);

    private static IReadOnlyDictionary<string, int> CountAssetClassComparisons()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".fs"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            var matches = CountComparisons(File.ReadAllText(file), extension);
            if (matches > 0)
            {
                counts[relative] = matches;
            }
        }

        return counts;
    }

    private static readonly string Names = string.Join("|", SecurityAssetClassCatalog.AssetClasses.Select(Regex.Escape));

    // C#: x == "Bond", x != "Bond", "Bond" == x, string.Equals(<any operand, nested calls included>,
    // "Bond", …), x.Equals("Bond", …). A single "=" is an assignment in C#, so it is not matched.
    private static readonly Regex CSharpComparison = new(
        "(?:==|!=)\\s*\"(?:" + Names + ")\""
        + "|\"(?:" + Names + ")\"\\s*(?:==|!=)"
        + "|Equals\\((?:(?:[^;\"()]|\\([^;\"()]*\\))*?,\\s*)?\"(?:" + Names + ")\"",
        RegexOptions.CultureInvariant);

    // F#: a qualified operand compared with = or <> (x.AssetClass = "Bond"). An unqualified
    // "AssetClassName = \"Bond\"" is a record field in a dispatch table, held by its parity test.
    private static readonly Regex FSharpComparison = new(
        "\\.\\w+\\s*(?:=|<>)\\s*\"(?:" + Names + ")\"",
        RegexOptions.CultureInvariant);

    private static int CountComparisons(string source, string extension)
        => (extension == ".fs" ? FSharpComparison : CSharpComparison).Matches(source).Count;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
