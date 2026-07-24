using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Guards the provider × instrument-type capability matrix read-model: every declared
/// provider appears with a cell per instrument type, capability flags stay grounded in the
/// descriptor catalogs, and discovery failures surface alongside the grid.
/// </summary>
public sealed class ProviderInstrumentCapabilityMatrixServiceTests
{
    [Fact]
    public void GetMatrix_IncludesEveryCatalogProviderWithFullInstrumentCoverage()
    {
        var matrix = new ProviderInstrumentCapabilityMatrixService().GetMatrix();

        matrix.InstrumentTypes.Should().BeEquivalentTo(
            InstrumentTypeDescriptorCatalog.All.Select(static descriptor => descriptor.InstrumentType.ToString()));
        matrix.Providers.Select(static row => row.ProviderId).Should().BeEquivalentTo(
            ProviderCapabilityDescriptorCatalog.Descriptors.Select(static descriptor => descriptor.ProviderId));

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
