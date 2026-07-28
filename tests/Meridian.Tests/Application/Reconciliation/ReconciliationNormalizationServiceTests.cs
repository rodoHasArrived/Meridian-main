using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Coverage for the FX-unified normalization seam of the canonical daily pipeline: conversions run
/// through the fail-closed <see cref="IReconciliationFxRateProvider"/> (the same contract the
/// statement lane uses), unconvertible lines keep their source currency, and accounting periods
/// derive from the entry's own posting timestamp through the business calendar.
/// </summary>
public sealed class ReconciliationNormalizationServiceTests
{
    private static readonly DateTimeOffset RunTimestamp = new(2026, 6, 1, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NormalizeCashEntry_WithKnownRate_ConvertsToBaseCurrencyAndKeepsPostingTime()
    {
        var service = CreateService(new TableReconciliationFxRateProvider(
            [new ReconciliationFxQuote("EUR", "USD", 1.10m, new DateOnly(2026, 5, 1))]));
        var postedAt = new DateTimeOffset(2026, 5, 29, 14, 30, 0, TimeSpan.Zero);
        var input = CreateCash("c1", 1000m, "EUR", postedAt);

        var normalized = service.NormalizeCashEntry(input, "USD", RunTimestamp);

        normalized.AmountBase.Should().Be(1100m);
        normalized.BaseCurrency.Should().Be("USD");
        normalized.PostedAtUtc.Should().Be(postedAt, "the posting timestamp is evidence and must survive normalization");
        normalized.AccountingPeriod.Should().Be(new DateOnly(2026, 5, 29));
    }

    [Fact]
    public void NormalizeCashEntry_WithoutRate_FailsClosedToSourceCurrency()
    {
        var service = CreateService(IdentityReconciliationFxRateProvider.Instance);
        var input = CreateCash("c1", 1000m, "EUR", new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero));

        var normalized = service.NormalizeCashEntry(input, "USD", RunTimestamp);

        normalized.AmountBase.Should().Be(1000m, "no rate means no conversion");
        normalized.BaseCurrency.Should().Be("EUR", "the line stays in its source currency so the matcher fails closed");
    }

    [Fact]
    public void NormalizeCashEntry_WeekendPosting_ResolvesToNextBusinessPeriod()
    {
        var service = CreateService(IdentityReconciliationFxRateProvider.Instance);
        var input = CreateCash("c1", 500m, "USD", new DateTimeOffset(2026, 5, 30, 3, 0, 0, TimeSpan.Zero));

        var normalized = service.NormalizeCashEntry(input, "USD", RunTimestamp);

        normalized.AccountingPeriod.Should().Be(new DateOnly(2026, 6, 1), "Saturday postings recognize in Monday's period");
    }

    [Fact]
    public void NormalizePosition_WithKnownRate_ConvertsMarketValueAtPositionAsOfDate()
    {
        var provider = new TableReconciliationFxRateProvider(
        [
            new ReconciliationFxQuote("EUR", "USD", 1.10m, new DateOnly(2026, 5, 1)),
            new ReconciliationFxQuote("EUR", "USD", 1.20m, new DateOnly(2026, 5, 29))
        ]);
        var service = CreateService(provider);
        var input = CreatePosition("p1", "EUR", 2000m, new DateTimeOffset(2026, 5, 28, 18, 0, 0, TimeSpan.Zero));

        var normalized = service.NormalizePosition(input, "USD", RunTimestamp);

        normalized.MarketValue.Should().Be(2200m, "the position's own as-of date selects the 1.10 rate, not the newer run-date rate");
        normalized.Currency.Should().Be("USD");
        normalized.AsOfUtc.Should().Be(RunTimestamp);
        normalized.InstrumentCanonicalId.Should().Be("canon:SAP");
    }

    [Fact]
    public void NormalizePosition_WithoutRate_FailsClosedToSourceCurrency()
    {
        var service = CreateService(IdentityReconciliationFxRateProvider.Instance);
        var input = CreatePosition("p1", "EUR", 2000m, new DateTimeOffset(2026, 5, 28, 18, 0, 0, TimeSpan.Zero));

        var normalized = service.NormalizePosition(input, "USD", RunTimestamp);

        normalized.MarketValue.Should().Be(2000m);
        normalized.Currency.Should().Be("EUR");
    }

    private static ReconciliationNormalizationService CreateService(IReconciliationFxRateProvider fxRateProvider) =>
        new(new PrefixInstrumentMapping(), fxRateProvider, BusinessDayAccountingCalendar.Default);

    private static NormalizedCashEntry CreateCash(string id, decimal amount, string currency, DateTimeOffset postedAtUtc) =>
        new(id, "acct", amount, currency, 0m, string.Empty, postedAtUtc, default, id);

    private static NormalizedPosition CreatePosition(string id, string currency, decimal marketValue, DateTimeOffset asOfUtc) =>
        new(id, string.Empty, null, null, "SAP", null, 10m, marketValue / 10m, marketValue, currency, asOfUtc, id);

    private sealed class PrefixInstrumentMapping : IInstrumentMappingService
    {
        public string ResolveCanonicalId(string? cusip, string? isin, string? ticker, string? internalId) =>
            $"canon:{cusip ?? isin ?? ticker ?? internalId ?? "unknown"}";
    }
}
