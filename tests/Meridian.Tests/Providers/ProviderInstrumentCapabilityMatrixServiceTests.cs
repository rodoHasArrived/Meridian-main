using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Guards the provider × instrument-type capability matrix read-model: every declared
/// market-data provider appears with a cell per instrument type, capability flags stay grounded in the
/// descriptor catalogs, and discovery failures surface alongside the grid.
/// </summary>
public sealed class ProviderInstrumentCapabilityMatrixServiceTests
{
    [Fact]
    public void GetMatrix_IbkrBondCoverageDoesNotInventStreamingQuotesOrTrades()
    {
        var ibkr = new ProviderInstrumentCapabilityMatrixService().GetMatrix().Providers.Single(row => row.ProviderId == "ibkr");
        var bond = ibkr.Cells.Single(cell => cell.InstrumentType == "Bond");
        bond.Supported.Should().BeTrue("bond contract and historical coverage remains declared");
        bond.Backfill.Should().BeTrue();
        bond.Stream.Should().BeFalse("the detailed streaming profiles do not include bonds");
        ibkr.FixedIncomeTasks.Should().HaveCount(8);
        ibkr.FixedIncomeTasks!.Single(task => task.Task == "ExecutableQuotes").AdapterSupport.Should().Be("Unsupported");
        ibkr.FixedIncomeTasks.Single(task => task.Task == "Trades").AdapterSupport.Should().Be("Unsupported");
        ibkr.FixedIncomeTasks.Single(task => task.Task == "Terms").AdapterSupport.Should().Be("AdapterDeclared");
    }

    [Fact]
    public void GetMatrix_FredRateInputsDoNotClaimPriceOrDiscountFactorAvailability()
    {
        var fred = new ProviderInstrumentCapabilityMatrixService().GetMatrix().Providers.Single(row => row.ProviderId == "fred");
        fred.FixedIncomeTasks!.Single(task => task.Task == "Curves").Detail.Should().Contain("construction method");
        fred.FixedIncomeTasks.Single(task => task.Task == "Fixings").AdapterSupport.Should().Be("AdapterDeclared");
        fred.FixedIncomeTasks.Single(task => task.Task == "EvaluatedPrices").AdapterSupport.Should().Be("Unsupported");
        fred.FixedIncomeTasks.SelectMany(task => task.Connections).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMatrixForTenant_RetainsOwnedConnectionEvidenceWithoutPromotingGeneralCertification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-fi-readiness-{Guid.NewGuid():N}", "appsettings.json");
        try
        {
            var store = new ConfigStore(path);
            await store.SaveAsync(new AppConfig(ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new("ours", "interactive-brokers", "Owned TWS", TenantId: "tenant-a"),
                    new("theirs", "ibkr", "Private TWS", TenantId: "tenant-b"),
                    new("legacy", "ibkr", "Legacy TWS"),
                    new("ambiguous", "ibkr", "Duplicate owned", TenantId: "tenant-a"),
                    new("ambiguous", "ibkr", "Duplicate foreign", TenantId: "tenant-b")
                ],
                Certifications: [new("ours", "Passed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), true, ["Connection is reachable"])])));
            var service = new ProviderInstrumentCapabilityMatrixService(configStore: store);
            service.GetMatrix().Providers.SelectMany(row => row.FixedIncomeTasks!).SelectMany(task => task.Connections).Should().BeEmpty();
            var ibkr = service.GetMatrixForTenant("tenant-a").Providers.Single(row => row.ProviderId == "ibkr");
            var terms = ibkr.FixedIncomeTasks!.Single(task => task.Task == "Terms");
            var connection = terms.Connections.Should().ContainSingle().Subject;
            connection.ConnectionId.Should().Be("ours");
            connection.ConfigurationState.Should().Be("Configured");
            connection.EntitlementState.Should().Be("Unknown");
            connection.FreshnessState.Should().Be("Unknown");
            connection.VerificationState.Should().Be("live-untested");
            connection.ObservationAsOf.Should().BeNull();
            connection.VerifiedAt.Should().BeNull();
            connection.Evidence.Should().Contain(evidence => evidence.Kind == "ConnectionCertification" && evidence.Detail.Contains("Passed"));
            connection.Evidence.Should().Contain(evidence => evidence.Kind == "TaskEvidence");
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetMatrix_IncludesEveryMarketDataCatalogProviderWithFullInstrumentCoverage()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();

        matrix.InstrumentTypes.Should().BeEquivalentTo(
            InstrumentTypeDescriptorCatalog.All.Select(static descriptor => descriptor.InstrumentType.ToString()));
        matrix.Providers.Select(static row => row.ProviderId).Should().BeEquivalentTo(
            ProviderCapabilityDescriptorCatalog.Descriptors
                .Where(static descriptor => descriptor.ProviderId != "openfigi")
                .Select(static descriptor => descriptor.ProviderId),
            "OpenFIGI is inventoried as a symbol resolver, which the matrix does not display");

        foreach (var row in matrix.Providers)
        {
            row.Cells.Should().HaveCount(matrix.InstrumentTypes.Count);
        }
    }

    [Fact]
    public void GetMatrix_AlpacaCellsReflectDeclaredCoverageAndSurfaces()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();
        var alpaca = matrix.Providers.Single(static row => row.ProviderId == "alpaca");

        alpaca.Surfaces.Streaming.Should().BeTrue();
        alpaca.Surfaces.Brokerage.Should().BeTrue();

        var equity = alpaca.Cells.Single(static cell => cell.InstrumentType == "Equity");
        equity.Supported.Should().BeTrue();
        equity.Stream.Should().BeTrue();
        equity.Backfill.Should().BeTrue();
        equity.CorporateActions.Should().BeTrue("equities carry corporate-action lifecycle data");
        equity.OptionsChain.Should().BeFalse("an equity is not an option instrument");

        var equityOption = alpaca.Cells.Single(static cell => cell.InstrumentType == "EquityOption");
        equityOption.Supported.Should().BeTrue();
        equityOption.OptionsChain.Should().BeTrue();
        equityOption.CorporateActions.Should().BeFalse("option instruments do not carry corporate actions");

        var bond = alpaca.Cells.Single(static cell => cell.InstrumentType == "Bond");
        bond.Supported.Should().BeFalse("Alpaca does not declare bond coverage");
        bond.Stream.Should().BeFalse();
        bond.Backfill.Should().BeFalse();
    }

    [Fact]
    public void GetMatrix_CorporateActionFlagsAreGatedByInstrumentSemantics()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();
        var finnhub = matrix.Providers.Single(static row => row.ProviderId == "finnhub");

        finnhub.Surfaces.CorporateActions.Should().BeTrue();
        finnhub.Cells.Single(static cell => cell.InstrumentType == "Equity")
            .CorporateActions.Should().BeTrue();
        finnhub.Cells.Single(static cell => cell.InstrumentType == "Crypto")
            .CorporateActions.Should().BeFalse("crypto instruments do not carry corporate actions");
    }

    [Fact]
    public void GetMatrix_PolygonRetainsHistoricalCoverageWithoutUnsupportedStreamingClaims()
    {
        var polygon = new ProviderInstrumentCapabilityMatrixService().GetMatrix().Providers.Single(row => row.ProviderId == "polygon");
        foreach (var instrument in new[] { "Forex", "Crypto", "Index" })
        {
            var cell = polygon.Cells.Single(cell => cell.InstrumentType == instrument);
            cell.Backfill.Should().BeTrue();
            cell.Stream.Should().BeFalse("the current Polygon client handles stock/option channel messages only");
        }
        polygon.Cells.Single(cell => cell.InstrumentType == "Equity").Stream.Should().BeTrue();
        polygon.Cells.Single(cell => cell.InstrumentType == "EquityOption").Stream.Should().BeTrue();
    }

    [Fact]
    public void GetMatrix_SyntheticOptionsRemainVisibleWithoutClaimingOptionStreamingOrSearch()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();
        var synthetic = matrix.Providers.Single(row => row.ProviderId == "synthetic");
        foreach (var instrument in new[] { "EquityOption", "IndexOption" })
        {
            var cell = synthetic.Cells.Single(cell => cell.InstrumentType == instrument);
            cell.OptionsChain.Should().BeTrue();
            cell.Stream.Should().BeFalse();
            cell.SymbolSearch.Should().BeFalse("the synthetic reference catalog has no option instruments");
        }
        synthetic.Cells.Single(cell => cell.InstrumentType == "Equity").SymbolSearch.Should().BeTrue();
    }

    [Fact]
    public void GetMatrix_SymbolResolverOnlyFamilyRemainsInCatalogWithoutAnEmptyMatrixRow()
    {
        var openFigi = ProviderCapabilityDescriptorCatalog.Descriptors.Single(descriptor => descriptor.ProviderId == "openfigi");
        openFigi.HasSymbolResolver.Should().BeTrue();
        new ProviderInstrumentCapabilityMatrixService().GetMatrix().Providers.Should().NotContain(row => row.ProviderId == "openfigi",
            "the matrix has no symbol-resolution surface and must not render a misleading empty row");
    }

    [Fact]
    public void GetMatrix_ReferenceOnlyProviderStaysScopedToDeclaredTypes()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();
        var fred = matrix.Providers.Single(static row => row.ProviderId == "fred");

        fred.DeclaredInstrumentTypes.Should().BeEquivalentTo("Index");
        fred.Cells.Single(static cell => cell.InstrumentType == "Equity").Supported.Should().BeFalse();
        fred.Cells.Single(static cell => cell.InstrumentType == "Index").Backfill.Should().BeTrue();
    }

    [Fact]
    public void GetMatrix_WithoutRegistryOrWithCleanRegistry_ReportsNoDiscoveryFailures()
    {
        new ProviderInstrumentCapabilityMatrixService().GetMatrix()
            .DiscoveryFailures.Should().BeEmpty();

        new ProviderInstrumentCapabilityMatrixService(new DataSourceRegistry()).GetMatrix()
            .DiscoveryFailures.Should().BeEmpty();
    }

    [Fact]
    public void GetMatrix_WithActivationFailure_ProjectsTypedRegistryFailure()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterModules(
            new ServiceCollection(),
            typeof(Meridian.Tests.ProviderSdk.ThrowingConstructorProviderModule).Assembly);

        var matrix = new ProviderInstrumentCapabilityMatrixService(registry).GetMatrix();

        matrix.DiscoveryFailures.Should().Contain(failure =>
            failure.Stage == "activate" &&
            failure.Subject.Contains(nameof(Meridian.Tests.ProviderSdk.ThrowingConstructorProviderModule), StringComparison.Ordinal) &&
            failure.ErrorType == nameof(InvalidOperationException));
    }
}
