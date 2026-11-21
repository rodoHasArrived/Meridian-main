using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

public sealed class PaperNativeSnapshotEvidenceTests
{
    private const string AccountId = "native-account";
    private const string RunId = "native-run";
    private static readonly Guid SecurityId = Guid.Parse("e93dff41-6249-4eb6-a152-77bfd6b58754");

    [Theory]
    [InlineData("cash")]
    [InlineData("margin")]
    [InlineData("margin-loss")]
    [InlineData("contracts")]
    [InlineData("face")]
    [InlineData("variation")]
    public void EveryFillPrefix_CapturesNativeQuantityBasisAndSameBookMoney(string scenario)
    {
        // Native paper journals keep their legacy shape; the synthetic book separately
        // declares its read-side currency, as retained native sessions do in production.
        var ledger = new Meridian.Ledger.Ledger();
        var margin = scenario.StartsWith("margin", StringComparison.Ordinal);
        var portfolio = new PaperTradingPortfolio([
            new(AccountId, "Native account", AccountKind.Brokerage, margin ? 200m : 100000m,
                margin ? MarginAccountType.RegT : MarginAccountType.Cash)
        ], ledger);
        portfolio.BindPositionAccountingEvidenceScope(AccountId, "USD");
        var fills = scenario switch
        {
            "margin" => new[] { (OrderSide.Buy, 5m, 100m), (OrderSide.Sell, 8m, 150m), (OrderSide.Buy, 4m, 170m) },
            "margin-loss" => [(OrderSide.Buy, 5m, 100m), (OrderSide.Sell, 7m, 1m), (OrderSide.Buy, 3m, 200m)],
            "contracts" => [(OrderSide.Buy, 2.5m, 2.505m), (OrderSide.Sell, 3.5m, 3.015m), (OrderSide.Buy, 1.5m, 2.025m)],
            "face" => [(OrderSide.Buy, 1000m, 101.234m), (OrderSide.Sell, 1250m, 98.765m), (OrderSide.Buy, 500m, 100.001m)],
            "variation" => [(OrderSide.Buy, 2m, 5000m), (OrderSide.Sell, 3m, 5010m), (OrderSide.Buy, 2m, 4990m)],
            _ => [(OrderSide.Buy, 2.25m, 100.005m), (OrderSide.Sell, 3.25m, 110.015m), (OrderSide.Buy, 2.5m, 90.025m)]
        };
        decimal signedQuantity = 0m;
        for (var index = 0; index < fills.Length; index++)
        {
            var (side, quantity, price) = fills[index];
            var fill = Fill(side, quantity, price, index) with
            {
                LotUnitKind = scenario == "face" ? SecurityLotUnitKind.FaceValue
                    : scenario is "contracts" or "variation" ? SecurityLotUnitKind.Contracts : SecurityLotUnitKind.Shares,
                LotMultiplier = scenario == "contracts" ? 100m : scenario == "variation" ? 50m : 1m,
                BookedMoneyMultiplier = scenario == "variation" ? 50m : null,
                BookedVariationMargin = scenario == "variation" ? true : null,
                UsesFaceValuePercentageOfPar = scenario == "face"
            };
            portfolio.ApplyFill(AccountId, fill, AccountId);
            signedQuantity += side == OrderSide.Buy ? quantity : -quantity;
            var capture = portfolio.CapturePositionAccountingEvidence();
            var row = capture.Positions.Should().ContainSingle().Which;
            row.Quantity.Should().Be(signedQuantity);
            row.QuantityUnit.Should().Be(fill.LotUnitKind.Value.ToString());
            row.Multiplier.Should().Be(fill.LotMultiplier);
            row.SecurityId.Should().Be(SecurityId);
            row.StrategyRunId.Should().Be(RunId);
            row.Currency.Should().Be("USD");
            row.FinancialAccountId.Should().Be(AccountId);
            row.CoverageFailure.Should().BeNull();
            row.MonetaryBasis.Should().Be(scenario == "variation" ? 0m
                : ledger.GetBalance(signedQuantity < 0m
                    ? LedgerAccounts.ShortSecuritiesPayable("TEST", AccountId)
                    : LedgerAccounts.Securities("TEST", AccountId)));
            var account = capture.Accounts.Single(item => item.AccountId == AccountId);
            account.Cash.Should().Be(portfolio.GetAccount(AccountId)!.Cash);
            account.AccountingShortMarketValue.Should().BeLessThanOrEqualTo(0m);
            account.AccountingEquity.Should().Be(account.Cash + account.AccountingLongMarketValue
                + account.AccountingShortMarketValue - account.MarginBalance + account.ShortMarginCollateral);
            capture.CoveredFinancialAccountIds.Should().Equal(AccountId);
            capture.AsOf.Should().BeOnOrAfter(fill.Timestamp);
            portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        }
    }

    [Fact]
    public void ShortMarketValueIsSignedAndCapturedReceiptsDoNotChangeWithLaterFills()
    {
        var portfolio = ScopedPortfolio();
        portfolio.ApplyFill(AccountId, Fill(OrderSide.Sell, 2m, 100m), AccountId);
        var before = portfolio.CapturePositionAccountingEvidence();
        before.Accounts.Single().AccountingShortMarketValue.Should().Be(-200m);
        portfolio.ApplyFill(AccountId, Fill(OrderSide.Buy, 1m, 80m, 1), AccountId);
        before.Positions.Single().Quantity.Should().Be(-2m);
        before.Positions.Single().MonetaryBasis.Should().Be(200m);
        portfolio.CapturePositionAccountingEvidence().Positions.Single().Quantity.Should().Be(-1m);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("run")]
    [InlineData("legacy")]
    public void MissingOrMixedAttributionCannotBecomeCleanUntilOldExposureIsFlat(string kind)
    {
        var portfolio = ScopedPortfolio();
        portfolio.ApplyFill(AccountId, Fill(OrderSide.Buy, 1m, 100m) with
        { AccountingSecurityId = kind == "legacy" ? null : SecurityId, AccountingRunId = kind == "legacy" ? null : RunId }, AccountId);
        portfolio.ApplyFill(AccountId, Fill(OrderSide.Buy, 1m, 120m, 1) with
        { AccountingSecurityId = kind == "identity" ? Guid.NewGuid() : SecurityId, AccountingRunId = kind == "run" ? "another-run" : RunId }, AccountId);
        var unknown = portfolio.CapturePositionAccountingEvidence().Positions.Single();
        unknown.CoverageFailure.Should().NotBeNullOrWhiteSpace();
        unknown.Quantity.Should().BeNull();
        unknown.MonetaryBasis.Should().BeNull();

        portfolio.ApplyFill(AccountId, Fill(OrderSide.Sell, 2m, 110m, 2), AccountId);
        portfolio.CapturePositionAccountingEvidence().Positions.Should().BeEmpty();
        portfolio.ApplyFill(AccountId, Fill(OrderSide.Buy, 1m, 130m, 3), AccountId);
        var fresh = portfolio.CapturePositionAccountingEvidence().Positions.Single();
        fresh.Quantity.Should().Be(1m);
        fresh.MonetaryBasis.Should().Be(130m);
        fresh.CoverageFailure.Should().BeNull();
    }

    [Fact]
    public void RegisteredAccountPartitionsKeepSeparateMoneyWithoutInventingBookCurrency()
    {
        var portfolio = new PaperTradingPortfolio([
            new(AccountId, "Native account", AccountKind.Brokerage, 1000m),
            new("foreign-account", "Other account", AccountKind.Brokerage, 7654m)
        ], new Meridian.Ledger.Ledger());
        var capture = portfolio.CapturePositionAccountingEvidence();
        capture.CoveredFinancialAccountIds.Should().BeEquivalentTo([AccountId, "foreign-account"]);
        capture.Accounts.Single(account => account.AccountId == AccountId).Cash.Should().Be(1000m);
        capture.Accounts.Single(account => account.AccountId == "foreign-account").Cash.Should().Be(7654m);
        capture.Accounts.Should().OnlyContain(account => account.FunctionalCurrency == null);
    }

    [Fact]
    public void SharedUnscopedCashAndUnboundCurrencyAreNotAttributedFromPositionOwnership()
    {
        var unscoped = new PaperTradingPortfolio(1000m, new Meridian.Ledger.Ledger());
        unscoped.ApplyFill(Fill(OrderSide.Buy, 1m, 100m), AccountId);
        var capture = unscoped.CapturePositionAccountingEvidence();
        capture.Positions.Single().FinancialAccountId.Should().Be(AccountId);
        capture.CoveredFinancialAccountIds.Should().BeEmpty();
        capture.Accounts.Should().BeEmpty("a position owner is not proof of whose shared cash is held");

        var noCurrency = new PaperTradingPortfolio([new AccountDefinition(AccountId, "Account", AccountKind.Brokerage, 1000m)], new Meridian.Ledger.Ledger());
        noCurrency.ApplyFill(AccountId, Fill(OrderSide.Buy, 1m, 100m), AccountId);
        var unknown = noCurrency.CapturePositionAccountingEvidence();
        unknown.Accounts.Single().FunctionalCurrency.Should().BeNull();
        unknown.Positions.Single().Currency.Should().BeNull();
        unknown.Positions.Single().MonetaryBasis.Should().BeNull();
    }

    [Fact]
    public async Task LegacySessionProjectionDoesNotInventCurrencyOrAccountCoverage()
    {
        await using var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance);
        var session = await service.CreateSessionAsync(new CreatePaperSessionDto("legacy", "Legacy paper", 1000m));
        await service.RecordFillAsync(session.SessionId, Fill(OrderSide.Buy, 1m, 100m) with
        { Timestamp = session.CreatedAt.AddSeconds(1) });
        var capture = service.GetActivePortfolio(session.SessionId)!.CapturePositionAccountingEvidence();
        capture.Positions.Should().ContainSingle().Which.Currency.Should().BeNull();
        capture.Positions.Single().MonetaryBasis.Should().BeNull();
        capture.CoveredFinancialAccountIds.Should().BeEmpty();
        capture.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSessionRestartPreservesAdmittedIdentityRunUnitsAndExactBasis()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-native-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance);
            string sessionId;
            PositionAccountingEvidenceSnapshot expected;
            await using (var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store))
            {
                var session = await service.CreateSessionAsync(new CreatePaperSessionDto("strategy", "Synthetic", 1000m));
                sessionId = session.SessionId;
                await service.RecordFillAsync(sessionId, Fill(OrderSide.Buy, .75m, 100.1234m) with
                { Timestamp = session.CreatedAt.AddSeconds(1) });
                expected = service.GetActivePortfolio(sessionId)!.CapturePositionAccountingEvidence();
            }
            await using var restarted = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
                new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance));
            await restarted.InitialiseAsync();
            var actual = restarted.GetActivePortfolio(sessionId)!.CapturePositionAccountingEvidence();
            actual.Positions.Should().BeEquivalentTo(expected.Positions);
            actual.Accounts.Single().Cash.Should().Be(expected.Accounts.Single().Cash);
            actual.Accounts.Single().FunctionalCurrency.Should().Be("USD");
            actual.Positions.Single().MonetaryBasis.Should().Be(.75m * 100.1234m);
            actual.CoveredFinancialAccountIds.Should().Equal(PaperTradingPortfolio.DefaultAccountId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static PaperTradingPortfolio ScopedPortfolio()
    {
        var portfolio = new PaperTradingPortfolio(
            [new AccountDefinition(AccountId, "Native account", AccountKind.Brokerage, 10000m)],
            new Meridian.Ledger.Ledger());
        portfolio.BindPositionAccountingEvidenceScope(AccountId, "USD");
        return portfolio;
    }

    private static ExecutionReport Fill(OrderSide side, decimal quantity, decimal price, int sequence = 0) => new()
    {
        OrderId = "native-" + sequence, Symbol = "TEST", Side = side,
        ReportType = ExecutionReportType.Fill, OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity, FilledQuantity = quantity, FillPrice = price,
        AccountingSecurityId = SecurityId, AccountingRunId = RunId,
        LotUnitKind = SecurityLotUnitKind.Shares, LotMultiplier = 1m,
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(sequence)
    };
}
