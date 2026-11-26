using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Moq;

namespace Meridian.Tests.Strategies;

public sealed class RetainedAccountingPopulationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanonicalDividend_PreservesPositionEvidenceThroughDeclarationSettlementAndSale(bool sellBeforeSettlement)
    {
        var date = new DateOnly(2026, 11, 20);
        var at = new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero);
        var securityId = Guid.NewGuid();
        var accountId = BacktestDefaults.DefaultBrokerageAccountId;
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(), 0, 0, ledger, at)
        { SecurityIdentity = _ => securityId };
        var action = new BacktestCanonicalAction("TEST", 1, new(Guid.NewGuid(), securityId,
            CorporateActionEventTypes.Dividend, date.AddDays(1), date.AddDays(3), 2m, "USD",
            null, null, null, null, null, null, null));
        portfolio.ConfigureCanonicalActions(new CanonicalBacktestActions(new(at.AddDays(3), "synthetic-snapshot", [action])));
        var receipts = new List<AssetEventCashFlow>();
        var cashFlows = new List<CashFlowEntry>();
        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "TEST", 10, 100m, 0m, at.AddHours(12)));
        portfolio.UpdateLastPrice("TEST", 100m);
        AssertPosition(0, 10m, 1000m);

        portfolio.ApplyCanonicalActions(date.AddDays(1));
        AssertPosition(1, 10m, 1000m);
        ledger.GetBalance(LedgerAccounts.DividendReceivable(LedgerSecurityKey.Unresolved("TEST"), accountId)).Should().Be(20m);
        if (sellBeforeSettlement)
        {
            portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "TEST", -4, 100m, 0m, at.AddDays(2).AddHours(12)));
            AssertPosition(2, 6m, 600m);
        }

        portfolio.ApplyCanonicalActions(date.AddDays(3));
        AssertPosition(3, sellBeforeSettlement ? 6m : 10m, sellBeforeSettlement ? 600m : 1000m);
        ledger.GetBalance(LedgerAccounts.DividendReceivable(LedgerSecurityKey.Unresolved("TEST"), accountId)).Should().Be(0m);
        cashFlows.OfType<DividendCashFlow>().Should().ContainSingle().Which.Amount.Should().Be(20m,
            "settlement retains the ex-date entitlement even after a partial sale");

        portfolio.ProcessFill(new(Guid.NewGuid(), Guid.NewGuid(), "TEST", sellBeforeSettlement ? -6 : -10,
            100m, 0m, at.AddDays(4).AddHours(12)));
        AssertPosition(4, 0m, 0m);
        ledger.Journal.Should().OnlyContain(journal => journal.IsBalanced);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();

        void AssertPosition(int day, decimal quantity, decimal basis)
        {
            var snapshot = portfolio.TakeSnapshot(at.AddDays(day).AddHours(20), date.AddDays(day));
            cashFlows.AddRange(snapshot.DayCashFlows);
            receipts.AddRange(snapshot.DayCashFlows.OfType<AssetEventCashFlow>());
            var position = StrategyPositionAccountingProjector.Project(ledger.Journal, receipts).Should().ContainSingle().Which;
            position.Quantity.Should().Be(quantity);
            position.MonetaryBasis.Should().Be(basis);
            position.SecurityId.Should().Be(securityId);
            position.FinancialAccountId.Should().Be(accountId);
            position.QuantityUnit.Should().Be("Shares");
            position.Multiplier.Should().Be(1m);
            position.Currency.Should().Be("USD");
            position.CoverageFailure.Should().BeNull();
        }
    }

    [Fact]
    public void UnversionedPositionTransformation_RemainsUnavailable()
    {
        var at = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        SecurityTradePosting[] legs = [new(SecurityTradePostingKind.OpenLong, 10m, 1000m)];
        var metadata = SecurityTradePositionEvidence.WithPostings(new(Symbol: "TEST", FinancialAccountId: "account"),
            legs, SecurityLotUnitKind.Shares, 1m);
        ledger.PostLines(at, "Synthetic source position", legs.SelectMany(leg =>
            SecurityTradePostingProjector.Project(leg, LedgerSecurityKey.From(metadata.SecurityId, "TEST"), "account")).ToArray(), metadata);
        var receipt = new AssetEventCashFlow(at.AddDays(1), 0m, "TEST", AssetEventType.Split, 10, 0m, PositionFactor: 2m)
        { AccountId = "account" };

        var position = StrategyPositionAccountingProjector.Project(ledger.Journal, [receipt]).Should().ContainSingle().Which;

        position.Quantity.Should().BeNull();
        position.CoverageFailure.Should().Be("A processed asset receipt lacks versioned native-unit or currency evidence.");
    }

    [Fact]
    public async Task LegacyPostedSourceDoesNotTurnMatchingActivityIntoProofOfCompleteAccountBalances()
    {
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        var snapshot = new PortfolioSnapshot(at, day, 1000m, 0m, 0m, 0m, 1000m, 0m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot>(), [])
        { AccountingPositions = [] };
        var request = new BacktestRequest(day, day, InitialCash: 1000m);
        var metrics = new BacktestResult(request, new HashSet<string>(), [snapshot], [], [],
            BacktestMetricsEngine.Compute([snapshot], [], [], request), new Meridian.Ledger.Ledger(), TimeSpan.Zero, 1);
        var run = StrategyRunEntry.Start("legacy-source", "Legacy source", RunType.Paper) with { Metrics = metrics, EndedAt = at };
        var posted = new Meridian.Ledger.Ledger();
        posted.PostLines(at, "Synthetic cash", [(LedgerAccounts.Cash, 1000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1000m)]);
        var store = new StrategyRunStore();
        await store.RecordRunAsync(run);
        var reads = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService(),
            postedLedgerSource: new LegacyPostedSource(posted));
        var service = new ReconciliationRunService(reads, new(), new InMemoryReconciliationRunRepository());

        var result = (await service.RunAsync(new(run.RunId), new StrategyRunReadScope("tenant", "company")))!;

        result.Breaks.Should().HaveCount(2);
        result.Breaks.Should().OnlyContain(item => item.Category == ReconciliationBreakCategory.ClassificationGap
            && item.Measures != null && item.Measures.All(measure => measure.Expected == null && measure.Actual == null
                && measure.Variance == null && measure.UnavailableReason != null));
        result.Matches.Should().NotContain(item => item.CheckId == "cash-balance" || item.CheckId == "net-equity");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CapturedEmptyRunPopulationDoesNotReuseSiblingAccountHoldings(bool receiptPresent)
    {
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        var positions = new Dictionary<string, Position> { ["SIBLING"] = new("SIBLING", 10, 100m, 0m, 0m) };
        var snapshot = new PortfolioSnapshot(at, day, 1000m, 0m, 1000m, 0m, 2000m, 0m, positions,
            new Dictionary<string, FinancialAccountSnapshot>
            {
                ["account"] = new("account", "Synthetic account", FinancialAccountKind.Brokerage, "Synthetic",
                    1000m, 0m, 1000m, 0m, 2000m, positions, new())
            }, [])
        { AccountingPositions = receiptPresent ? [] : null };
        var request = new BacktestRequest(day, day, ["SIBLING"], InitialCash: 2000m);
        var metrics = new BacktestResult(request, new HashSet<string> { "SIBLING" }, [snapshot], [], [],
            BacktestMetricsEngine.Compute([snapshot], [], [], request), new Meridian.Ledger.Ledger(), TimeSpan.Zero, 1);
        var run = StrategyRunEntry.Start("empty-accounting", "Empty accounting", RunType.Paper) with
        { Metrics = metrics, EndedAt = at };
        var store = new StrategyRunStore();
        await store.RecordRunAsync(run);
        var reads = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var detail = (await reads.GetRunDetailAsync(run.RunId))!;
        detail.Portfolio!.Positions.Should().ContainSingle().Which.Symbol.Should().Be("SIBLING");
        detail.Portfolio.HasRetainedAccountingPositionPopulation.Should().Be(receiptPresent);
        var portfolio = new StrategyPortfolioReconciliationSourceAdapter().Adapt(detail)!;
        var inputs = new ReconciliationNormalizedInputs(portfolio,
            new(at, 0m, 0m, []) { BalanceComparisonUnavailableReason = "Run activity does not establish account stock." }, [], []);
        var projection = new ReconciliationProjectionService();

        if (receiptPresent)
        {
            portfolio.AccountingPositions.Should().BeEmpty();
            projection.BuildChecks(inputs).Should().NotContain(check => check.CheckId == "long-SIBLING");
            projection.BuildPositionChecks(inputs, new(run.RunId)).Should().BeEmpty();
            var gate = new Mock<ISecurityValidationGateService>(MockBehavior.Strict);
            var service = new ReconciliationRunService(reads, new(), new InMemoryReconciliationRunRepository(),
                securityValidationGate: gate.Object);
            var reconciled = (await service.RunAsync(new(run.RunId)))!;
            reconciled.Summary.SecurityIssueCount.Should().Be(0, "sibling account holdings are outside the retained empty run population");
            gate.VerifyNoOtherCalls();
            var siblingReference = new WorkstationSecurityReference(Guid.NewGuid(), "Sibling display reference", "Equity", "USD",
                SecurityStatusDto.Active, "SIBLING");
            var enrichedDisplay = detail with
            {
                Portfolio = detail.Portfolio with { Positions = detail.Portfolio.Positions.Select(position => position with { Security = siblingReference }).ToArray() }
            };
            var securitySource = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
            (await new SecurityMasterAccountingEventSourceAdapter(securitySource.Object).BuildRequestAsync(enrichedDisplay, new(run.RunId)))
                .Should().BeNull("a captured empty run population cannot generate sibling corporate-action expectations");
            securitySource.VerifyNoOtherCalls();
            var bankInputs = inputs with { InternalCashMovements = [new("synthetic-bank-flow", 100m, at, false, "bank")] };
            var bankComparison = projection.BuildChecks(bankInputs).Should().ContainSingle(check => check.CheckId == "bank-net-vs-ledger-cash").Which;
            bankComparison.HasExpectedAmount.Should().BeFalse();
            bankComparison.HasActualAmount.Should().BeFalse();
            bankComparison.ActualPresent.Should().BeFalse("run activity cannot establish an account cash stock for bank reconciliation");
        }
        else
        {
            portfolio.AccountingPositions.Should().NotBeEmpty("legacy absence cannot prove an empty run population");
            projection.BuildChecks(inputs).Should().Contain(check => check.CheckId == "long-SIBLING");
            projection.BuildPositionChecks(inputs, new(run.RunId)).Should().OnlyContain(check => !check.IsMatch);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeLedgerIdentityNeverFallsBackToAnUnretainedCurrentTicker(bool identityRetained)
    {
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        Guid? retainedId = identityRetained ? Guid.NewGuid() : null;
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        SecurityTradePosting[] legs = [new(SecurityTradePostingKind.OpenLong, 10m, 1000m)];
        var metadata = SecurityTradePositionEvidence.WithPostings(new JournalEntryMetadata(Symbol: "TEST",
            FinancialAccountId: "account", SecurityId: retainedId), legs, SecurityLotUnitKind.Shares, 1m);
        ledger.PostLines(at, "Synthetic native trade", legs.SelectMany(leg => SecurityTradePostingProjector.Project(leg,
            LedgerSecurityKey.From(metadata.SecurityId, "TEST"), "account")).ToArray(), metadata);
        ledger.PostLines(at, "Synthetic commission", SecurityTradePostingProjector.Commission(1m, "account"),
            new JournalEntryMetadata(Symbol: "TEST", FinancialAccountId: "account"));
        var snapshot = new PortfolioSnapshot(at, day, 1000m, 0m, 0m, 0m, 1000m, 0m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot>(), []);
        var request = new BacktestRequest(day, day, InitialCash: 1000m);
        var result = new BacktestResult(request, new HashSet<string>(), [snapshot], [], [],
            BacktestMetricsEngine.Compute([snapshot], [], [], request), ledger, TimeSpan.Zero, 1);
        var run = StrategyRunEntry.Start("native-identity", "Native identity", RunType.Paper) with { Metrics = result, EndedAt = at };
        var lookup = new WrongTickerLookup();

        var summary = (await new LedgerReadService(lookup).BuildSummaryAsync(run))!;

        summary.TrialBalance.Where(static line => line.Symbol == "TEST").Should().OnlyContain(line => line.Security == null);
        summary.AccountingPositions.Should().ContainSingle().Which.SecurityId.Should().Be(retainedId);
        lookup.Calls.Should().Be(identityRetained ? 1 : 0);
    }

    private sealed class LegacyPostedSource(IReadOnlyLedger ledger) : IStrategyRunLedgerSource
    {
        public Task<IReadOnlyLedger?> ReadAsync(StrategyRunEntry run, StrategyRunReadScope scope, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyLedger?>(ledger);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorporateActionInputsUseTenNativeUnitsNeverOneThousandMonetaryBasis(bool nativeUnitsRetained)
    {
        var at = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        var securityId = Guid.NewGuid();
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        var metadata = new JournalEntryMetadata(Symbol: "TEST", FinancialAccountId: "account", SecurityId: securityId);
        SecurityTradePosting[] legs = [new(SecurityTradePostingKind.OpenLong, 10m, 1000m)];
        if (nativeUnitsRetained)
            metadata = SecurityTradePositionEvidence.WithPostings(metadata, legs, SecurityLotUnitKind.Shares, 1m);
        ledger.PostLines(at, "Synthetic source facts", legs.SelectMany(leg =>
            SecurityTradePostingProjector.Project(leg, LedgerSecurityKey.From(metadata.SecurityId, "TEST"), "account")).ToArray(), metadata);
        var snapshot = new PortfolioSnapshot(at, day, 0m, 0m, 1000m, 0m, 1000m, 0m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot>(), []);
        var request = new BacktestRequest(day, day, InitialCash: 1000m);
        var metrics = new BacktestResult(request, new HashSet<string>(), [snapshot], [], [],
            BacktestMetricsEngine.Compute([snapshot], [], [], request), ledger, TimeSpan.Zero, 1);
        var run = StrategyRunEntry.Start("ca-quantity", "CA quantity", RunType.Backtest) with { Metrics = metrics, EndedAt = at };
        var store = new StrategyRunStore();
        await store.RecordRunAsync(run);
        var detail = (await new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService()).GetRunDetailAsync(run.RunId))!;
        var reference = new WorkstationSecurityReference(securityId, "Retained identity", "Equity", "USD", SecurityStatusDto.Active, "TEST");
        detail = detail with
        {
            Ledger = detail.Ledger! with
            {
                TrialBalance = detail.Ledger!.TrialBalance.Select(line => line.Symbol == "TEST" ? line with { Security = reference } : line).ToArray()
            }
        };
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        if (nativeUnitsRetained)
            query.Setup(item => item.GetEconomicDefinitionByIdAsync(securityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((SecurityEconomicDefinitionRecord?)null);

        var source = (await new SecurityMasterAccountingEventSourceAdapter(query.Object).BuildRequestAsync(detail, new(run.RunId)))!;

        if (nativeUnitsRetained)
        {
            var position = source.Positions.Should().ContainSingle().Which;
            position.ParAmount.Should().Be(10m);
            position.SourceQuantityUnit.Should().Be(SecurityLotUnitKind.Shares);
            position.SourceQuantityMultiplier.Should().Be(1m);
            source.SourceCoverageIssues.Should().BeEmpty();
            query.Verify(item => item.GetEconomicDefinitionByIdAsync(securityId, It.IsAny<CancellationToken>()), Times.Once);
            var face = new StrategyPositionAccountingEvidence("TEST", "account", securityId, false, 10m, 1000m, "FaceValue", 1m, "USD");
            var conflicting = detail with
            {
                Portfolio = detail.Portfolio! with
                {
                    HasRetainedAccountingPositionPopulation = true,
                    AccountingPositions = [face, face with { QuantityUnit = "Contracts", Multiplier = 100m }]
                }
            };
            var strict = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
            var unavailable = (await new SecurityMasterAccountingEventSourceAdapter(strict.Object).BuildRequestAsync(conflicting, new(run.RunId)))!;
            unavailable.Positions.Should().BeEmpty();
            unavailable.SourceCoverageIssues.Should().Contain(issue => issue.Reason.Contains("conventions disagree"));
            strict.VerifyNoOtherCalls();
        }
        else
        {
            source.Positions.Should().BeEmpty();
            source.SourceCoverageIssues.Should().ContainSingle().Which.Code.Should().Be("SM_RECON_POSITION_QUANTITY_UNAVAILABLE");
            var calculated = new SecurityMasterAccountingEventService().Generate(source);
            calculated.ExpectedEvents.Should().BeEmpty();
            calculated.Issues.Should().Contain(issue => issue.Code == "SM_RECON_POSITION_QUANTITY_UNAVAILABLE");
            query.VerifyNoOtherCalls();
        }
    }

    [Theory]
    [InlineData("FaceValue", 1, "USD", true)]
    [InlineData("Contracts", 100, "USD", false)]
    [InlineData("Shares", 1, "USD", false)]
    [InlineData("FaceValue", 2, "USD", false)]
    [InlineData("FaceValue", 1, "EUR", false)]
    public void FixedIncomeCannotTreatOtherNativeUnitsOrCurrencyAsDeclaredPar(string unit, int multiplier, string currency, bool supported)
    {
        var id = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var security = new SecurityMasterAccountingSecurity(id, "BOND", "Bond", "USD",
            new SecurityFixedIncomeTerms(.06m, "Fixed", "ACT/365", 2,
                IssueDate: new(2020, 1, 1), NextCouponDate: new(2026, 1, 31),
                MaturityDate: new(2030, 1, 1), AccrualStartDate: start)
            { CouponRateUnit = RateUnit.Fraction },
            new SecurityAccountingRule("AvailableForSale", "GAAP"));
        var request = new SecurityMasterAccountingEventRequest("native-unit-coverage", start, new(2026, 2, 1), [security],
            [new("BOND", id, "account", 100000m)
            {
                SourceQuantityUnit = Enum.Parse<SecurityLotUnitKind>(unit),
                SourceQuantityMultiplier = multiplier,
                SourceQuantityCurrency = currency
            }]);

        var result = new SecurityMasterAccountingEventService().Generate(request);

        if (supported)
            result.AccrualCalculations.Should().NotBeEmpty();
        else
        {
            result.ExpectedEvents.Should().BeEmpty();
            result.AccrualCalculations.Should().BeEmpty();
            result.Issues.Should().Contain(issue => issue.Code == "SM_RECON_POSITION_QUANTITY_UNAVAILABLE");
        }
    }

    private sealed class WrongTickerLookup : ISecurityReferenceLookup
    {
        public int Calls { get; private set; }
        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<WorkstationSecurityReference?>(new(Guid.NewGuid(), "Unrelated current ticker", "Equity",
                "USD", SecurityStatusDto.Active, symbol));
        }
    }
}
