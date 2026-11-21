using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper trading and the Security Master must agree on how a lot is counted. A fill that names its
/// asset class but carries no lot stamp opens a position in the unit the catalog declares for that
/// class (shares, contracts, face value, notional, fund units, currency or commitment), never in
/// the legacy shares fallback. The stamped path goes through <see cref="LotQuantity.UnitFor"/>,
/// which reads the same catalog.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaperLotUnitCatalogAgreementTests
{
    public static TheoryData<string> CatalogClasses()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
            data.Add(assetClass);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void UnstampedFill_OpensThePositionInTheCatalogLotUnit(string assetClass)
    {
        var expected = SecurityAssetClassCatalog.GetOrDefault(assetClass).LotUnit;
        var portfolio = new PaperTradingPortfolio(10_000_000m);
        var report = Fill("PAPER-" + assetClass) with { AssetClass = assetClass };

        portfolio.ApplyFill(report);

        var account = (AccountState)portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
        account.Positions[report.Symbol].LotUnit.Should().Be(expected,
            $"an unstamped '{assetClass}' fill must open in the catalog's lot unit");
    }

    [Theory]
    [MemberData(nameof(CatalogClasses))]
    public void LotQuantityUnitFor_UsesTheCatalogUnitAndDeclaredMultiplierTerm(string assetClass)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        var terms = descriptor.LotMultiplierTermKey is { } key
            ? JsonSerializer.SerializeToElement(new Dictionary<string, decimal> { [key] = 25m })
            : JsonSerializer.SerializeToElement(new { });

        var (unit, multiplier) = LotQuantity.UnitFor(assetClass, terms);

        unit.Should().Be(descriptor.LotUnit);
        multiplier.Should().Be(descriptor.LotMultiplierTermKey is null ? 1m : 25m,
            $"'{assetClass}' scales lots only by the multiplier term its catalog descriptor declares");
    }

    private static ExecutionReport Fill(string symbol) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = 1m,
        FilledQuantity = 1m,
        FillPrice = 10m,
        Commission = 0m,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
