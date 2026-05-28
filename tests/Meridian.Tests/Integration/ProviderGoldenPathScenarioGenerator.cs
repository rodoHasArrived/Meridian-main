using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Ledger;
using LedgerBook = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Integration;

internal static class ProviderGoldenPathScenarioGenerator
{
    public static GeneratedProviderGoldenPathScenario Create(ProviderGoldenPathScenarioOptions options)
    {
        if (options.Quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(options), "Quantity must be positive.");
        if (options.AveragePrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(options), "Average price must be positive.");
        if (options.FillSplits.Count == 0)
            throw new ArgumentException("At least one fill split is required.", nameof(options));
        if (options.FillSplits.Sum() != options.Quantity)
            throw new ArgumentException("Fill splits must sum to the scenario quantity.", nameof(options));

        var notional = decimal.Round(options.Quantity * options.AveragePrice, 2, MidpointRounding.AwayFromZero);
        var cashCredit = notional + options.Commission;
        var sourcePrefix = options.ProviderId.Replace("-", ".", StringComparison.OrdinalIgnoreCase);
        var fills = options.FillSplits.Select((quantity, index) =>
        {
            var isLast = index == options.FillSplits.Count - 1;
            return new GeneratedExecutionFill(
                FillId: $"{sourcePrefix}.fill.{index + 1:000}",
                Quantity: quantity,
                Price: options.AveragePrice,
                Commission: isLast ? options.Commission : 0m,
                Timestamp: options.Start.AddSeconds(2 + index));
        }).ToList();

        var ledgerLines = new List<GeneratedLedgerLine>
        {
            new("Securities", LedgerAccountType.Asset, options.Symbol, notional, 0m, $"{sourcePrefix}.ledger.security"),
            new("Cash", LedgerAccountType.Asset, null, 0m, cashCredit, $"{sourcePrefix}.ledger.cash")
        };

        if (options.Commission > 0m)
            ledgerLines.Insert(1, new GeneratedLedgerLine("Commissions", LedgerAccountType.Expense, options.Symbol, options.Commission, 0m, $"{sourcePrefix}.ledger.commission"));

        var materiality = Math.Abs(options.Quantity - options.StatementQuantity);
        return new GeneratedProviderGoldenPathScenario(
            ProviderId: options.ProviderId,
            Symbol: options.Symbol,
            Quantity: options.Quantity,
            AveragePrice: options.AveragePrice,
            Commission: options.Commission,
            StatementQuantity: options.StatementQuantity,
            Start: options.Start,
            Fills: fills,
            LedgerLines: ledgerLines,
            ExpectedBreak: new GeneratedReconciliationBreak(
                BreakId: $"{sourcePrefix}.break.position.quantity",
                Category: "PositionQuantityMismatch",
                Severity: materiality == 0m ? "None" : options.BreakSeverity,
                Materiality: materiality,
                ExpectedQuantity: options.Quantity,
                ActualQuantity: options.StatementQuantity,
                EvidenceIds:
                [
                    $"{sourcePrefix}.provider.market",
                    fills.Last().FillId,
                    ledgerLines[0].EvidenceId,
                    $"{sourcePrefix}.statement.position"
                ]));
    }

    public static LedgerBook ReplayLedger(GeneratedProviderGoldenPathScenario scenario)
    {
        var ledger = new LedgerBook();
        ledger.PostLines(
            scenario.Start.AddSeconds(10),
            $"Generated {scenario.ProviderId} {scenario.Symbol} fill",
            scenario.LedgerLines
                .Select(line => (
                    account: new LedgerAccount(line.AccountName, line.AccountType, line.Symbol),
                    debit: line.Debit,
                    credit: line.Credit))
                .ToList());

        return ledger;
    }

    public static IReadOnlyList<MarketEvent> GenerateMarketEvents(
        GeneratedProviderGoldenPathScenario scenario,
        int eventCount,
        decimal priceStep = 0.01m)
    {
        if (eventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(eventCount), "Event count must be positive.");

        var events = new List<MarketEvent>(eventCount);
        for (var i = 0; i < eventCount; i++)
        {
            var sequence = i + 1L;
            var timestamp = scenario.Start.AddTicks(i + 1);
            var price = scenario.AveragePrice + ((i % 17) - 8) * priceStep;

            if (i % 2 == 0)
            {
                var trade = new Trade(
                    Timestamp: timestamp,
                    Symbol: scenario.Symbol,
                    Price: price,
                    Size: 100 + i % 400,
                    Aggressor: AggressorSide.Unknown,
                    SequenceNumber: sequence,
                    StreamId: scenario.ProviderId,
                    Venue: scenario.ProviderId == "interactive-brokers" ? "SMART" : "V");

                events.Add(MarketEvent.Trade(timestamp, scenario.Symbol, trade, source: scenario.ProviderId));
            }
            else
            {
                var bid = price - 0.01m;
                var ask = price + 0.01m;
                var quote = new BboQuotePayload(
                    Timestamp: timestamp,
                    Symbol: scenario.Symbol,
                    BidPrice: bid,
                    BidSize: 100 + i % 250,
                    AskPrice: ask,
                    AskSize: 100 + (i + 17) % 250,
                    MidPrice: (bid + ask) / 2m,
                    Spread: ask - bid,
                    SequenceNumber: sequence,
                    StreamId: scenario.ProviderId,
                    Venue: scenario.ProviderId == "interactive-brokers" ? "SMART" : "V");

                events.Add(MarketEvent.BboQuote(timestamp, scenario.Symbol, quote, source: scenario.ProviderId));
            }
        }

        return events;
    }
}

public sealed record ProviderGoldenPathScenarioOptions(
    string ProviderId,
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal Commission,
    decimal StatementQuantity,
    IReadOnlyList<decimal> FillSplits,
    DateTimeOffset Start,
    string BreakSeverity = "High");

public sealed record GeneratedProviderGoldenPathScenario(
    string ProviderId,
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal Commission,
    decimal StatementQuantity,
    DateTimeOffset Start,
    IReadOnlyList<GeneratedExecutionFill> Fills,
    IReadOnlyList<GeneratedLedgerLine> LedgerLines,
    GeneratedReconciliationBreak ExpectedBreak);

public sealed record GeneratedExecutionFill(
    string FillId,
    decimal Quantity,
    decimal Price,
    decimal Commission,
    DateTimeOffset Timestamp);

public sealed record GeneratedLedgerLine(
    string AccountName,
    LedgerAccountType AccountType,
    string? Symbol,
    decimal Debit,
    decimal Credit,
    string EvidenceId);

public sealed record GeneratedReconciliationBreak(
    string BreakId,
    string Category,
    string Severity,
    decimal Materiality,
    decimal ExpectedQuantity,
    decimal ActualQuantity,
    IReadOnlyList<string> EvidenceIds);
