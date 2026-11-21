using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class FinancialValueReadContractTests
{
    [Fact]
    public void ReconciliationComparisons_PreserveExactDecimalTextAndMissingValuesAlongsideLegacyNumbers()
    {
        var comparison = new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value,
            9007199254740993.125m, 9007199254740993.126m, 0.001m, 0.0000000000000000000000000001m, "USD");
        var row = new ReconciliationBreakQueueItem("case-exact", "run-exact", "Exact cash", ReconciliationBreakCategory.AmountMismatch,
            ReconciliationBreakQueueStatus.Open, 9007199254740993.125m, "Retained comparison", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, null,
            ToleranceBand: 0.0000000000000000000000000001m, Measures: [comparison]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = document.RootElement;
        root.GetProperty("varianceExact").GetString().Should().Be("9007199254740993.125");
        root.GetProperty("toleranceBandExact").GetString().Should().Be("0.0000000000000000000000000001");
        var measure = root.GetProperty("measures")[0];
        measure.GetProperty("expectedExact").GetString().Should().Be("9007199254740993.125");
        measure.GetProperty("actualExact").GetString().Should().Be("9007199254740993.126");
        measure.GetProperty("varianceExact").GetString().Should().Be("0.001");
        measure.GetProperty("toleranceExact").GetString().Should().Be("0.0000000000000000000000000001");
        measure.GetProperty("expected").GetDecimal().Should().Be(comparison.Expected);
        root.GetProperty("variance").GetDecimal().Should().Be(row.Variance);

        var unavailable = new ReconciliationBreakMeasureDto(ReconciliationBreakMeasureKindDto.Value,
            null, null, null, null, "USD", "No retained pair");
        unavailable.ExpectedExact.Should().BeNull();
        unavailable.ActualExact.Should().BeNull();
        unavailable.VarianceExact.Should().BeNull();
        unavailable.ToleranceExact.Should().BeNull();
    }

    [Fact]
    public void TrialBalance_PreservesExactDecimalTextAlongsideLegacyNumericFields()
    {
        var row = new LedgerPeriodTrialBalanceLineDto("Cash", "Asset", null, null,
            9007199254740993.125m, 0.001m, 9007199254740993.124m, 2);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        document.RootElement.GetProperty("balanceExact").GetString().Should().Be("9007199254740993.124");
        document.RootElement.GetProperty("debitTotalExact").GetString().Should().Be("9007199254740993.125");
        document.RootElement.GetProperty("creditTotalExact").GetString().Should().Be("0.001");
        document.RootElement.GetProperty("balance").GetDecimal().Should().Be(row.Balance);
    }

    [Fact]
    public void WorkstationBalance_UsesInvariantExactText()
    {
        var row = new LedgerTrialBalanceLine("Cash", "Asset", null, null, 12345678901234567890.12345678m, 1);
        row.BalanceExact.Should().Be(row.Balance.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TradingPosition_RetainsSourcePrecisionWithoutChangingLegacyDisplay()
    {
        var row = new WorkstationTradingPositionRow("AAPL", "AAPL", "Long", "1", "10.12", "11.12", "—", "$1.00", "$11.12",
            AveragePriceExact: "10.12345678", MarkPriceExact: "11.12345678", UnrealizedPnlExact: "1", ExposureExact: "11.12345678");
        row.AveragePrice.Should().Be("10.12");
        row.AveragePriceExact.Should().Be("10.12345678");
        row.Currency.Should().BeNull("a display glyph does not establish a currency code");
    }
}
