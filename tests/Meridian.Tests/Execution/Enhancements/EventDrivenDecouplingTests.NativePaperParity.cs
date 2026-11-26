using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Events;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Theory]
    [InlineData("cash-crossings")]
    [InlineData("margin-crossings")]
    [InlineData("margin-severe-loss")]
    [InlineData("contracts")]
    [InlineData("bond-accrual")]
    [InlineData("variation-margin")]
    public async Task NativePaperCapturedEvidence_RebuildsTheSameLedgerAcrossSupportedFillEconomics(string scenario)
    {
        var native = new Meridian.Ledger.Ledger();
        var margin = scenario.StartsWith("margin-", StringComparison.Ordinal);
        var portfolio = new PaperTradingPortfolio(
            [new AccountDefinition("broker", "Synthetic broker", AccountKind.Brokerage, 1_000_000m,
                margin ? MarginAccountType.RegT : MarginAccountType.Cash,
                margin ? new RegTMarginModel() : null)], native);
        var openingJournals = native.Journal.ToArray();
        var balancesAfterFill = new List<IReadOnlyDictionary<LedgerAccount, decimal>>();
        var unit = scenario is "contracts" or "variation-margin" ? SecurityLotUnitKind.Contracts
            : scenario == "bond-accrual" ? SecurityLotUnitKind.FaceValue : SecurityLotUnitKind.Shares;
        var multiplier = scenario == "contracts" ? 100m : scenario == "variation-margin" ? 50m : 1m;
        var events = new List<TradeExecutedEvent>();

        void Fill(OrderSide side, decimal quantity, decimal price, decimal? accrued = null)
        {
            var id = Guid.NewGuid();
            var report = new ExecutionReport
            {
                OrderId = id.ToString("D"),
                Symbol = "AAPL",
                Side = side,
                ReportType = ExecutionReportType.Fill,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = quantity,
                FilledQuantity = quantity,
                FillPrice = price,
                Commission = 1m,
                Fees = 2m,
                SlippageCost = 3m,
                Timestamp = new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero).AddMinutes(events.Count),
                LotUnitKind = unit,
                LotMultiplier = multiplier,
                AssetClass = scenario == "contracts" ? "Option" : scenario == "variation-margin" ? "Future" : null,
                BookedMoneyMultiplier = multiplier,
                BookedVariationMargin = scenario == "variation-margin",
                UsesFaceValuePercentageOfPar = unit == SecurityLotUnitKind.FaceValue,
                AccruedInterestPer100 = accrued,
                // Admission stamps the identity the projection's gate resolves, so both books key
                // per-security accounts by the same SecurityId (Security Master review F2).
                AccountingSecurityId = PassingSecurityValidationGate.SecurityId
            };
            var result = portfolio.ApplyFillWithAccountingEvidence(report, ownerAccountId: "broker");
            result.Evidence.Should().NotBeNull();
            result.Evidence!.QuantityUnit.Should().Be(unit);
            result.Evidence.LotMultiplier.Should().Be(multiplier);
            events.Add(new(id, report.OrderId, report.Symbol, side, quantity, price, report.Commission.Value,
                result.RealizedPnl, result.NewCash, report.Timestamp, "broker",
                report.UsesFaceValuePercentageOfPar, multiplier, scenario == "variation-margin")
            { AccountingEvidenceVersion = 1, AccountingEvidence = result.Evidence });
            balancesAfterFill.Add(native.Accounts.ToDictionary(account => account, native.GetBalance));
        }

        if (scenario == "bond-accrual")
        { Fill(OrderSide.Buy, 100_000m, 101.25m, 1m); Fill(OrderSide.Sell, 40_000m, 102.25m, 1.5m); }
        else if (scenario == "variation-margin")
        { Fill(OrderSide.Buy, 2m, 6000m); Fill(OrderSide.Sell, 1m, 6010m); Fill(OrderSide.Sell, 2m, 6005m); }
        else if (scenario == "margin-severe-loss")
        { Fill(OrderSide.Buy, 10m, 100m); Fill(OrderSide.Sell, 10m, 20m); }
        else
        { Fill(OrderSide.Buy, 3m, 10m); Fill(OrderSide.Sell, 5m, 12m); Fill(OrderSide.Buy, 4m, 9m); }

        for (var prefix = 1; prefix <= events.Count; prefix++)
        {
            var projected = new Meridian.Ledger.Ledger();
            foreach (var opening in openingJournals)
                projected.Post(opening);
            var store = new InMemoryTradeFillPostingStore(TestPostingScope);
            var consumer = new LedgerPostingConsumer(new InMemoryTradeFillLedgerPostingTarget(),
                NullLogger<LedgerPostingConsumer>.Instance, store, TestPostingContext,
                projectionLedger: projected, securityValidationGate: new PassingSecurityValidationGate());
            foreach (var evt in events.Take(prefix))
            {
                var retained = JsonSerializer.Deserialize(JsonSerializer.Serialize(evt,
                    ExecutionJsonContext.Default.TradeExecutedEvent), ExecutionJsonContext.Default.TradeExecutedEvent)!;
                await consumer.PublishAsync(retained);
            }
            await consumer.DisposeAsync();

            (await store.LoadPendingAsync()).Should().BeEmpty();
            var expected = balancesAfterFill[prefix - 1];
            foreach (var account in expected.Keys.Union(projected.Accounts))
                projected.GetBalance(account).Should().Be(expected.GetValueOrDefault(account), $"{scenario}, fill {prefix}: {account}");
            projected.Journal.Should().OnlyContain(static entry => entry.IsBalanced);
            projected.Journal.Where(entry => entry.Metadata.ActivityType is "commission" or "trade-margin-financing" or "trade-short-collateral")
                .Should().OnlyContain(entry => entry.Metadata.Tags == null || !entry.Metadata.Tags.ContainsKey("positionEvidence.version"));
        }
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }
}
