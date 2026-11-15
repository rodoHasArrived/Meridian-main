using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Ratchet against new hand-spelled brokerage asset-class codes.
/// <see cref="ProviderAssetClassCodeCatalog"/> is the one table of provider codes; a site that
/// needs to know what "us_option" or IB "OPT" means reads the table (or
/// <see cref="AlpacaAssetClassCodes"/>), not a string literal. The persisted
/// <c>USD_US_EQUITY</c> journal tag lives only in <see cref="ExecutionReportingAssetClasses"/>.
/// <para>
/// Each file's count of vendor-spelled literals may only go down. A file not in
/// <see cref="KnownSites"/> may not spell any, and a listed file whose count drops must have its
/// ceiling lowered (or be removed at zero) so the ratchet stays tight. Several listed sites are
/// IB subscription <c>SecurityType</c> values or search vocabularies rather than order or
/// position codes; they stay counted so new ones still have to justify themselves.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ProviderAssetClassCodeRatchetTests
{
    private static readonly string[] SourcesOfTruth =
    [
        "src/Meridian.Contracts/SecurityMaster/ProviderAssetClassCodeCatalog.cs",
        "src/Meridian.Execution/Services/ExecutionReportingAssetClasses.cs",
    ];

    private static readonly string[] VendorSpellings =
    [
        "us_equity", "us_option", "us_treasury", "fixed_income_treasury", "corporate_bond", "us_corporate",
        "OPT", "FOP",
        "USD_US_EQUITY",
    ];

    // Repository-relative path -> the most vendor-spelled literals the file may contain.
    private static readonly IReadOnlyDictionary<string, int> KnownSites = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["src/Meridian.Application/SecurityMaster/CanonicalSymbolRegistryMigrationService.cs"] = 1,
        ["src/Meridian.Application/Subscriptions/Services/PortfolioImportService.cs"] = 4,
        ["src/Meridian.Application/Subscriptions/SubscriptionOrchestrator.cs"] = 1,
        ["src/Meridian.Contracts/Configuration/SymbolConfig.cs"] = 2,
        ["src/Meridian.Contracts/Domain/Enums/InstrumentType.cs"] = 1,
        ["src/Meridian.Contracts/SecurityMaster/InstrumentTypeDescriptorCatalog.cs"] = 3,
        ["src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs"] = 1,
        ["src/Meridian.Core/Config/ConfigValidationHelper.cs"] = 2,
        ["src/Meridian.Execution.Sdk/IBrokerageGateway.cs"] = 2,
        ["src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs"] = 1,
        ["src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProvider.cs"] = 8,
        ["src/Meridian.Infrastructure/Adapters/InteractiveBrokers/ContractFactory.cs"] = 2,
        ["src/Meridian.Ui.Services/Services/SymbolMappingService.cs"] = 2,
        ["src/Meridian.Ui.Shared/HtmlTemplateGenerator.cs"] = 2,
        ["src/Meridian.Wpf/Views/SymbolsPage.xaml.cs"] = 6,
    };

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/", "/wwwroot/"];

    [Fact]
    public void NoFile_AddsVendorSpelledAssetClassCodes()
    {
        var counts = CountVendorSpellings();

        var offenders = counts
            .Where(static entry => !KnownSites.TryGetValue(entry.Key, out var ceiling) || entry.Value > ceiling)
            .Select(static entry => $"{entry.Key} ({entry.Value})")
            .OrderBy(static site => site, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "brokerage asset-class codes belong in ProviderAssetClassCodeCatalog; read the table or "
            + "AlpacaAssetClassCodes instead of spelling the vendor code");
    }

    [Fact]
    public void KnownSites_CeilingsAreTight_SoTheRatchetOnlyGoesDown()
    {
        var counts = CountVendorSpellings();

        foreach (var (site, ceiling) in KnownSites)
        {
            counts.TryGetValue(site, out var actual);
            actual.Should().Be(
                ceiling,
                $"{site} now spells {actual} vendor codes; lower its ceiling (or remove it at zero)");
        }
    }

    [Fact]
    public void SourcesOfTruth_AreNotListedAsSites()
        => KnownSites.Keys.Should().NotIntersectWith(SourcesOfTruth);

    [Fact]
    public void UsdUsEquity_KeepsItsPersistedValue()
        => ExecutionReportingAssetClasses.UsdUsEquity.Should().Be("USD_US_EQUITY");

    private static IReadOnlyDictionary<string, int> CountVendorSpellings()
    {
        var root = FindRepositoryRoot();
        var literal = new Regex(
            "\"(" + string.Join("|", VendorSpellings.Select(Regex.Escape)) + ")\"",
            RegexOptions.CultureInvariant);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) is not (".cs" or ".fs"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (SourcesOfTruth.Contains(relative, StringComparer.Ordinal)
                || SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            var count = literal.Matches(File.ReadAllText(file)).Count;
            if (count > 0)
            {
                counts[relative] = count;
            }
        }

        return counts;
    }

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
