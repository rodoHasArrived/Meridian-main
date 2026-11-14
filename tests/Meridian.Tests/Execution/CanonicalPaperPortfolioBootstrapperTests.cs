using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

public sealed class CanonicalPaperPortfolioBootstrapperTests
{
    [Fact]
    public async Task Configured_canonical_account_has_no_default_alias_and_requires_authoritative_cash_before_admission()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        portfolio.PrimaryAccountId.Should().Be(fixture.AccountId);
        portfolio.Cash.Should().Be(0m);
        portfolio.GetAccountSnapshot("default").Should().BeNull();
        portfolio.GetAccountSnapshot(fixture.AccountId).Should().NotBeNull();
        portfolio.CanAdmitCanonicalOrders(DateTimeOffset.UtcNow, out _).Should().BeFalse();

        await fixture.Bootstrap(portfolio).InitializeAsync();

        portfolio.Cash.Should().Be(100000m);
        portfolio.CanAdmitCanonicalOrders(DateTimeOffset.UtcNow, out _).Should().BeTrue();
        portfolio.Accounts.Should().ContainSingle().Which.AccountId.Should().Be(fixture.AccountId);
        foreach (var (path, type) in new[]
        {
            (fixture.Options.CashAccountPath, "Asset"), (fixture.Options.SecuritiesAccountPath, "Asset"),
            (fixture.Options.RealizedGainAccountPath, "Revenue"), (fixture.Options.RealizedLossAccountPath, "Expense"),
            (fixture.Options.CommissionAccountPath, "Expense"), (fixture.Options.FeeAccountPath, "Expense")
        })
            await fixture.Accounts.Received().ResolveAsync(Arg.Is<ConfiguredLedgerAccountRequestDto>(request =>
                request.AccountPath == path && request.ExpectedAccountType == type && request.FinancialAccountId == fixture.AccountId
                    && request.ConfigurationVersion == fixture.Grant.MappingRevision), Arg.Any<CancellationToken>());
        await fixture.Journals.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());
        new PaperTradingPortfolio(100000m).GetAccountSnapshot("default")!.Cash.Should().Be(100000m,
            "legacy paper composition retains its established default account and balance");
    }

    [Fact]
    public async Task Actual_OMS_default_fill_uses_configured_account_and_accounting_source_without_an_alias()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance,
            options: new PaperTradingGatewayOptions { AllowScaffoldMarketFills = true },
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions { CommissionRate = 0m });
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, tradeEventPublisher: publisher);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            LimitPrice = 100m
        });

        result.Success.Should().BeTrue();
        var accepted = publisher.Events.Should().ContainSingle().Which;
        accepted.FinancialAccountId.Should().Be(fixture.AccountId);
        accepted.SourceMode.Should().Be("Synthetic");
        accepted.FillPrice.Should().Be(100m);
        accepted.AccountingEvidenceVersion.Should().Be(1);
        accepted.AccountingEvidence.Should().NotBeNull();
        accepted.AccountingEvidence!.FinancialAccountId.Should().Be(fixture.AccountId);
        accepted.AccountingEvidence.Opening!.Kind.Should().Be(SecurityTradePostingKind.OpenLong);
        accepted.AccountingEvidence.Opening.GrossValue.Should().Be(1000m);
        var snapshot = portfolio.GetAccountSnapshot(fixture.AccountId)!;
        snapshot.Cash.Should().Be(99000m);
        snapshot.Positions.Should().ContainSingle().Which.Quantity.Should().Be(10);
        snapshot.Positions.Single().OwnerQuantities[fixture.AccountId].Should().Be(10m);
        portfolio.GetAccountSnapshot("default").Should().BeNull();
    }

    [Fact]
    public async Task Native_gateway_without_canonical_binding_does_not_treat_a_market_limit_as_an_observation()
    {
        await using var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance,
            options: new PaperTradingGatewayOptions { AllowScaffoldMarketFills = false });
        var report = await gateway.SubmitOrderAsync(new OrderRequest
        { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10m, LimitPrice = 100m });
        report.OrderStatus.Should().Be(OrderStatus.Rejected);
        report.FillPrice.Should().BeNull();
    }

    [Fact]
    public async Task Normal_nonflat_restart_restores_cash_quantities_and_exact_basis_without_replaying_economics()
    {
        var fixture = new Fixture(withHolding: true);
        var original = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(original).InitializeAsync();
        var restarted = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        var recovery = fixture.Bootstrap(restarted);

        await recovery.InitializeAsync();
        await recovery.InitializeAsync();

        var account = restarted.GetAccountSnapshot(fixture.AccountId)!;
        account.Cash.Should().Be(90000m);
        account.Positions.Should().ContainSingle();
        account.Positions[0].Quantity.Should().Be(100);
        account.Positions[0].AverageCostBasis.Should().Be(100m);
        account.Positions[0].MarkObservedAtUtc.Should().Be(fixture.ObservedAt);
        account.Positions[0].MarkSource.Should().Be("RetainedExecutionFill");
        account.Positions[0].OwnerQuantities[fixture.AccountId].Should().Be(100m);
        restarted.GetPositionLots("AAPL").Should().ContainSingle().Which.LotId.Should().Be(fixture.Lot.LotId);
        restarted.PortfolioValue.Should().Be(original.PortfolioValue);
        await fixture.Journals.DidNotReceive().AppendAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>());

        // Exact retained basis, rather than a reconstructed rounded quote, is consumed after recovery.
        restarted.ApplyFill(new ExecutionReport
        {
            OrderId = "post-restart-sale",
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 40m,
            FilledQuantity = 40m,
            FillPrice = 110m,
            Timestamp = DateTimeOffset.UtcNow
        });
        restarted.GetAccountSnapshot(fixture.AccountId)!.Cash.Should().Be(94400m);
        restarted.RealisedPnl.Should().Be(400m);
        restarted.GetPositionLots("AAPL").Single().OpenQuantity.Should().Be(60m);
    }

    [Fact]
    public async Task Canonical_cash_book_refuses_native_financing_and_scaled_fill_mutations()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var report = new ExecutionReport
        {
            OrderId = "unsupported-canonical-fill",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m
        };
        var scaled = () => portfolio.ApplyFill(report, fixture.AccountId, contractMultiplier: 2m);
        scaled.Should().Throw<InvalidOperationException>().WithMessage("*without financing or variation margin*");
        var variation = () => portfolio.ApplyFill(report with { BookedVariationMargin = true }, fixture.AccountId);
        variation.Should().Throw<InvalidOperationException>();
        var interest = () => portfolio.AccrueMarginInterest(0.1m);
        interest.Should().Throw<InvalidOperationException>().WithMessage("*unsupported*");
        var settlement = () => portfolio.ApplyVariationMarginSettlement(fixture.AccountId, "AAPL", 100m, DateTimeOffset.UtcNow);
        settlement.Should().Throw<InvalidOperationException>().WithMessage("*unsupported*");
        portfolio.Cash.Should().Be(100000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Should().BeEmpty();
    }

    [Fact]
    public void Recovered_lot_consumption_uses_retained_total_basis_instead_of_rounded_unit_quote()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        portfolio.RestoreCanonicalState(fixture.AccountId, 1000m, 0m,
        [
            new CanonicalPaperPositionRecovery("AAPL",
                [new CanonicalPaperLotRecovery("exact-basis", 3m, decimal.Round(20m / 3m, 12), 20m, fixture.ObservedAt, Guid.NewGuid())],
                10m, fixture.ObservedAt, "RetainedExecutionFill")
        ], fixture.Context.PostingScope);
        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "close-restored-lot",
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 3m,
            FilledQuantity = 3m,
            FillPrice = 10m,
            Timestamp = DateTimeOffset.UtcNow
        });
        portfolio.RealisedPnl.Should().Be(10m);
        portfolio.Cash.Should().Be(1030m);
        portfolio.GetPositionLots("AAPL").Should().BeEmpty();
    }

    [Fact]
    public async Task Retained_fill_price_does_not_become_a_fresh_mark_on_restart_and_live_observation_keeps_its_timestamp()
    {
        var fixture = new Fixture(withHolding: true);
        var cache = new LiveMarketDataCache();
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options, cache);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        portfolio.CanAdmitCanonicalOrders(now, out var reason).Should().BeFalse();
        reason.Should().Contain("fresh retained market observation");
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions[0].MarkObservedAtUtc.Should().Be(fixture.ObservedAt);

        var observed = now.AddSeconds(-10);
        cache.RecordQuote("AAPL", new BboQuotePayload(observed, "AAPL", 110m, 100, 110m, 100, 110m, 0m, 1));
        portfolio.CanAdmitCanonicalOrders(now, out _).Should().BeTrue();
        var marked = portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions[0];
        marked.MarkObservedAtUtc.Should().Be(observed);
        marked.MarkSource.Should().Be("MarketDataCache");
        marked.UnrealisedPnl.Should().Be(1000m);
        portfolio.CanAdmitCanonicalOrders(now.AddMinutes(6), out _).Should().BeFalse();
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions[0].MarkObservedAtUtc.Should().Be(observed,
            "reading the cached quote again cannot refresh its observation time");
    }

    [Fact]
    public async Task Missing_price_observation_does_not_use_cost_as_a_market_mark()
    {
        var fixture = new Fixture(withHolding: true);
        fixture.Snapshot = fixture.Snapshot with
        {
            Journals = fixture.Snapshot.Journals.Select(record =>
            record.Entry.Metadata.ActivityType == "execution.trade-fill"
                ? record with
                {
                    Entry = new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp,
                    record.Entry.Description, record.Entry.Lines, record.Entry.Metadata with { Tags = null })
                } : record).ToArray()
        };
        fixture.Receipt = fixture.Receipt with { Journal = fixture.Snapshot.Journals.Single(record => record.Entry.Metadata.ActivityType == "execution.trade-fill") };
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);

        var recovery = () => fixture.Bootstrap(portfolio).InitializeAsync();

        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no retained actual fill-price observation*");
        portfolio.Cash.Should().Be(0m);
        portfolio.CanAdmitCanonicalOrders(DateTimeOffset.UtcNow, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Unrelated_account_cash_cannot_initialize_the_configured_account()
    {
        var fixture = new Fixture(withHolding: false);
        fixture.Options.Grant = fixture.Grant with { FinancialAccountId = Guid.NewGuid().ToString("D") };
        ((IAutomatedPostingGrantStore)fixture.Journals).GetAsync(fixture.Grant.GrantId, fixture.Grant.TenantId, Arg.Any<CancellationToken>())
            .Returns(fixture.ActiveGrant with { Reference = fixture.Options.Grant });
        fixture.Journals.GetLedgerBookAsync(fixture.Context.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(fixture.Book with { FundStructureNodeId = Guid.Parse(fixture.Options.Grant.FinancialAccountId) });
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        var recovery = () => fixture.Bootstrap(portfolio).InitializeAsync();

        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*typed USD opening cash for its exact account*");
        portfolio.Cash.Should().Be(0m);
    }

    [Fact]
    public async Task Pending_WAL_and_fallback_receipts_are_recovered_before_the_runtime_snapshot_is_captured()
    {
        var fixture = new Fixture(withHolding: true);
        var root = Path.Combine(Path.GetTempPath(), "canonical-host-recovery", Guid.NewGuid().ToString("N"));
        var fill = new TradeExecutedEvent(fixture.FillId, "recovered-order", "AAPL", OrderSide.Buy, 100m, 100m,
            0m, 0m, 90000m, fixture.ObservedAt, fixture.AccountId)
        { SourceMode = "Synthetic", SettlementCurrency = "USD" };
        try
        {
            await using var store = new WalTradeFillPostingStore(new(root, fixture.Context), NullLogger<WalTradeFillPostingStore>.Instance);
            await store.AcceptAsync(fill);
            await using var fallback = new AtomicTradeFillHandoffFailureStore(new(root, fixture.Context));
            await fallback.RetainAsync(fill, "synthetic loss of primary acceptance acknowledgement");
            var completedReadbacks = 0;
            var accounting = Substitute.For<IAutomatedTradeFillAccountingService>();
            accounting.TryRecoverCompletedAsync(Arg.Any<TradeExecutedEvent>(), fixture.Context, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Interlocked.Increment(ref completedReadbacks);
                    return Task.FromResult<IReadOnlyList<JournalEntry>?>([fixture.TradeJournal.Entry]);
                });
            await using var consumer = LedgerPostingConsumer.CreateWithAccountingAdapter(Substitute.For<ITradeFillLedgerPostingTarget>(),
                NullLogger<LedgerPostingConsumer>.Instance, store, fixture.Context, requireSecurityMasterPostingGate: false,
                requireCurrentAccountingEvidence: false, automatedAccounting: accounting);
            var capture = (ILedgerOpenPositionReportingSnapshotSource)fixture.Journals;
            capture.CaptureOpenPositionReportingSnapshotAsync(Arg.Any<LedgerJournalEntryQuery>(), fixture.Context.PeriodId, Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    completedReadbacks.Should().BeGreaterThan(0);
                    (await store.LoadPendingAsync()).Should().BeEmpty();
                    (await fallback.LoadPendingAsync()).Should().BeEmpty();
                    return fixture.Snapshot;
                });
            var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);

            await fixture.Bootstrap(portfolio, consumer, fallback).InitializeAsync();

            portfolio.GetAccountSnapshot(fixture.AccountId)!.Cash.Should().Be(90000m);
            portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Single().Quantity.Should().Be(100);
            await accounting.DidNotReceive().PostAsync(Arg.Any<TradeExecutedEvent>(), Arg.Any<TradeFillLedgerPostingContext>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OMS_refuses_an_explicit_other_account_instead_of_relabelling_its_fill()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance,
            options: new PaperTradingGatewayOptions { AllowScaffoldMarketFills = true },
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions { CommissionRate = 0m });
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio, tradeEventPublisher: publisher);
        await oms.StartAsync();
        var refused = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            LimitPrice = 100m,
            FundAccountId = Guid.NewGuid()
        });
        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("configured accounting account");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
    }

    [Fact]
    public async Task Bootstrap_refuses_changed_posting_context_before_loading_another_scope()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        var bootstrap = new CanonicalPaperPortfolioBootstrapper(fixture.Options, fixture.Context with { PeriodId = Guid.NewGuid() },
            portfolio, fixture.Journals, fixture.Accounts, fixture.Securities);
        var initialize = () => bootstrap.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>().WithMessage("*context differs*");
        portfolio.Cash.Should().Be(0m);
    }

    [Fact]
    public async Task Account_inventory_coverage_failure_cannot_restore_an_apparently_empty_portfolio()
    {
        var fixture = new Fixture(withHolding: false);
        fixture.Snapshot = fixture.Snapshot with
        {
            OpenPositions = fixture.Snapshot.OpenPositions! with
            {
                CoverageBlockers = [new LedgerReportingInventoryCoverageBlocker(
                new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL", fixture.AccountId), "unproven holding basis")]
            }
        };
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        var initialize = () => fixture.Bootstrap(portfolio).InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inventory coverage is incomplete*");
        portfolio.Cash.Should().Be(0m);
    }

    [Fact]
    public async Task OMS_refuses_a_buy_that_needs_more_than_proven_cash_including_explicit_costs()
    {
        var fixture = new Fixture(withHolding: false);
        fixture.Costs.CommissionKind = PaperCommissionKind.FixedPerOrder;
        fixture.Costs.CommissionRate = 2m;
        fixture.Costs.FeePerOrder = 3m;
        fixture.Costs.SlippageBasisPoints = 1m;
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(1000m));

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("cash");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_costed_OMS_fill_preserves_each_identified_expense_and_the_actual_cash_movement()
    {
        var fixture = new Fixture(withHolding: false);
        fixture.Costs.CommissionKind = PaperCommissionKind.FixedPerOrder;
        fixture.Costs.CommissionRate = 2m;
        fixture.Costs.FeePerOrder = 3m;
        fixture.Costs.SlippageBasisPoints = 1m;
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(Buy(100m));

        result.Success.Should().BeTrue();
        var fill = publisher.Events.Should().ContainSingle().Which;
        fill.FinancialAccountId.Should().Be(fixture.AccountId);
        fill.Commission.Should().Be(2m);
        fill.Fees.Should().HaveCount(2);
        fill.Fees.Should().ContainSingle(fee => fee.Kind == "SyntheticModelFee" && fee.Amount == 3m
            && fee.FeeId == $"{fill.FillId:D}:paper-fees" && fee.Currency == "USD");
        fill.Fees.Should().ContainSingle(fee => fee.Kind == "SyntheticSlippageCost" && fee.Amount == 1m
            && fee.FeeId == $"{fill.FillId:D}:paper-slippage" && fee.Currency == "USD");
        fill.NewCash.Should().Be(89994m);
        portfolio.Cash.Should().Be(fill.NewCash);
        fill.GrossValue.Should().Be(10000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Single().AverageCostBasis.Should().Be(100m,
            "the unit cost of long synthetic holdings excludes separately expensed commission and fees");
    }

    [Fact]
    public async Task OMS_refuses_an_oversell_of_the_exact_account_long_holding()
    {
        var fixture = new Fixture(withHolding: true);
        var portfolio = fixture.CreateWithFreshQuote();
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(101m) with { Side = OrderSide.Sell });

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("long");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(90000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Single().Quantity.Should().Be(100L);
    }

    [Theory]
    [InlineData("Bond", "USD", SecurityStatusDto.Active)]
    [InlineData("Equity", "EUR", SecurityStatusDto.Active)]
    [InlineData("Equity", "USD", SecurityStatusDto.Inactive)]
    public async Task OMS_refuses_unsupported_or_inactive_security_master_instruments_before_fills(
        string assetClass, string currency, SecurityStatusDto status)
    {
        var fixture = new Fixture(withHolding: false);
        fixture.Security = fixture.Security with { AssetClass = assetClass, Currency = currency, Status = status };
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(1m));

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("active USD cash-equity");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
    }

    [Theory]
    [InlineData("resting")]
    [InlineData("option")]
    [InlineData("legs")]
    [InlineData("unpriced")]
    public async Task OMS_refuses_order_shapes_that_cannot_complete_as_one_supported_fill(string shape)
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();
        var request = shape switch
        {
            "resting" => Buy(1m) with { Type = OrderType.Limit },
            "option" => Buy(1m) with { OptionContract = new() { Multiplier = "100" } },
            "legs" => Buy(1m) with { Legs = [new() { Symbol = "AAPL", Side = OrderSide.Buy, RatioQuantity = 1m }] },
            _ => Buy(1m) with { LimitPrice = null }
        };

        var refused = await oms.PlaceOrderAsync(request);

        refused.Success.Should().BeFalse();
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
    }

    [Fact]
    public async Task An_external_cash_adjustment_requires_recovery_before_another_order_is_admitted()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        fixture.Snapshot = fixture.Snapshot with
        {
            Journals = [.. fixture.Snapshot.Journals,
            fixture.CashAdjustment(10m)]
        };
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(1m));

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("differ from the runtime projection");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
    }

    [Fact]
    public async Task A_lot_basis_adjustment_requires_recovery_even_when_cash_and_quantity_are_unchanged()
    {
        var fixture = new Fixture(withHolding: true);
        var portfolio = fixture.CreateWithFreshQuote();
        await fixture.Bootstrap(portfolio).InitializeAsync();
        fixture.Snapshot = fixture.Snapshot with
        {
            OpenPositions = fixture.Snapshot.OpenPositions! with
            {
                Lots = [fixture.Lot with { UnitCost = 110m, Acquisition = fixture.Lot.Acquisition! with
                { TransactionCostBasis = 11000m, FunctionalCostBasis = 11000m } }]
            }
        };
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(1m));

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("exact basis differ from the runtime projection");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(90000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Single().AverageCostBasis.Should().Be(100m);
    }

    [Fact]
    public async Task Concurrent_orders_cannot_each_spend_the_same_authoritative_cash_before_accounting_catches_up()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var results = await Task.WhenAll(oms.PlaceOrderAsync(Buy(600m)), oms.PlaceOrderAsync(Buy(600m)));

        results.Count(result => result.Success).Should().Be(1);
        results.Single(result => !result.Success).ErrorMessage.Should().Contain("accounting catch-up");
        publisher.Events.Should().ContainSingle();
        portfolio.Cash.Should().Be(40000m);
        portfolio.GetAccountSnapshot(fixture.AccountId)!.Positions.Single().Quantity.Should().Be(600L);
    }

    [Fact]
    public async Task Changed_grant_authority_blocks_new_orders_before_economic_state_changes()
    {
        var fixture = new Fixture(withHolding: false);
        var portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        await fixture.Bootstrap(portfolio).InitializeAsync();
        ((IAutomatedPostingGrantStore)fixture.Journals).ValidateCurrentAsync(fixture.Grant, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Synthetic posting grant was revoked.")));
        var publisher = new RecordingPublisher(fixture.Context);
        await using var oms = fixture.CreateOms(portfolio, publisher);
        await oms.StartAsync();

        var refused = await oms.PlaceOrderAsync(Buy(1m));

        refused.Success.Should().BeFalse();
        refused.ErrorMessage.Should().Contain("grant was revoked");
        publisher.Events.Should().BeEmpty();
        portfolio.Cash.Should().Be(100000m);
    }

    [Fact]
    public void Same_day_FIFO_uses_identical_canonical_lot_order_before_and_after_restart()
    {
        var fixture = new Fixture(withHolding: false);
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var priorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var original = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        original.RestoreCanonicalState(fixture.AccountId, 100000m, 0m,
            [new("AAPL", [new("prior-lot", 10m, 10m, 100m, start, priorId)], 10m, start, "RetainedExecutionFill")],
            fixture.Context.PostingScope);
        var buys = new[] { Fill("same-day-buy-1", 20m, OrderSide.Buy, 10m, start.AddHours(1)),
            Fill("same-day-buy-2", 30m, OrderSide.Buy, 10m, start.AddHours(2)) };
        foreach (var buy in buys)
            original.ApplyFill(buy);
        var recovered = new List<CanonicalPaperLotRecovery> { new("prior-lot", 10m, 10m, 100m, start, priorId) };
        foreach (var buy in buys)
        {
            var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(buy);
            recovered.Add(new($"execution:{fillId:D}", 10m, buy.FillPrice!.Value, 10m * buy.FillPrice.Value, start,
                TradeFillAccountingIdentity.Create(fixture.Context.PostingScope, fillId, "lot")));
        }
        var restarted = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(fixture.Options);
        restarted.RestoreCanonicalState(fixture.AccountId, original.Cash, 0m,
            [new("AAPL", recovered, 30m, start.AddHours(2), "RetainedExecutionFill")], fixture.Context.PostingScope);
        var sale = Fill("same-day-partial-sale", 40m, OrderSide.Sell, 15m, start.AddHours(3));

        original.ApplyFill(sale);
        restarted.ApplyFill(sale);

        var ordered = recovered.OrderBy(lot => lot.CanonicalTaxLotRecordId).ToArray();
        var expectedBasis = ordered[0].ExactBasis + ordered[1].ExactBasis / 2m;
        original.RealisedPnl.Should().Be(600m - expectedBasis);
        restarted.RealisedPnl.Should().Be(original.RealisedPnl);
        restarted.Cash.Should().Be(original.Cash);
        restarted.GetPositionLots("AAPL").Select(lot => new { lot.LotId, lot.OpenQuantity }).Should().BeEquivalentTo(
            original.GetPositionLots("AAPL").Select(lot => new { lot.LotId, lot.OpenQuantity }));
    }

    private static ExecutionReport Fill(string orderId, decimal price, OrderSide side, decimal quantity, DateTimeOffset at) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = side,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Timestamp = at
    };

    private static OrderRequest Buy(decimal quantity) => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = quantity,
        LimitPrice = 100m
    };

    private sealed class RecordingPublisher(TradeFillLedgerPostingContext context) : IScopedTradeEventPublisher
    {
        internal readonly List<TradeExecutedEvent> Events = [];
        public string PostingScope => context.PostingScope;
        public TradeFillPostingScopeIdentity ScopeIdentity => TradeFillPostingScopeIdentity.FromContext(context);
        public void Publish(TradeExecutedEvent tradeEvent) { lock (Events) Events.Add(tradeEvent); }
    }

    private sealed class Fixture
    {
        internal readonly string AccountId = Guid.NewGuid().ToString("D");
        internal readonly Guid SecurityId = Guid.NewGuid();
        internal readonly Guid PositionId = Guid.NewGuid();
        internal readonly Guid FillId = Guid.NewGuid();
        internal readonly DateTimeOffset ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        internal readonly ILedgerJournalStore Journals = Substitute.For<ILedgerJournalStore, IAutomatedPostingGrantStore,
            ILedgerOpenPositionReportingSnapshotSource>();
        internal readonly IConfiguredLedgerAccountResolver Accounts = Substitute.For<IConfiguredLedgerAccountResolver>();
        internal readonly ISecurityMasterQueryService Securities = Substitute.For<ISecurityMasterQueryService>();
        internal readonly TradeFillLedgerPostingContext Context;
        internal readonly AutomatedPostingGrantReferenceDto Grant;
        internal readonly AutomatedPostingGrantDto ActiveGrant;
        internal readonly AutomatedTradeFillAccountingOptions Options;
        internal readonly LedgerTaxLotRecord Lot;
        internal readonly LedgerJournalEntryRecord TradeJournal;
        internal readonly LedgerBookRecord Book;
        internal readonly PaperTradingCostOptions Costs = new() { CommissionRate = 0m };
        internal SecurityDetailDto Security;
        internal AtomicTaxLotJournalResult Receipt;
        internal LedgerReportingSnapshot Snapshot;

        internal Fixture(bool withHolding)
        {
            var bookId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            Context = new("canonical-host/v2", bookId, periodId, bookId, "synthetic-policy", "v1", 3, 2);
            Grant = new(Guid.NewGuid(), 1, "synthetic-tenant", AccountId, bookId, periodId, Context.AccountingPolicyId,
                Context.AccountingPolicyVersion, "mapping-v1", "Synthetic");
            ActiveGrant = new(Grant, true, "opening-reviewer", ObservedAt)
            {
                PolicySnapshot = new AccountingPolicyDto(Grant.PolicyId, AccountingBasisKindDto.Primary,
                    Grant.PolicyVersion, "Synthetic", DateOnly.FromDateTime(ObservedAt.UtcDateTime), null, false, "{}", ObservedAt)
            };
            ((IAutomatedPostingGrantStore)Journals).GetAsync(Grant.GrantId, Grant.TenantId, Arg.Any<CancellationToken>()).Returns(ActiveGrant);
            ((IAutomatedPostingGrantStore)Journals).ValidateCurrentAsync(Arg.Any<AutomatedPostingGrantReferenceDto>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var book = new LedgerBookRecord(bookId, "synthetic-fund", Guid.Parse(AccountId), FundStructureNodeKindDto.Account,
                "Synthetic book", "USD", ObservedAt, ObservedAt, AccountingPolicyId: Grant.PolicyId, AccountingPolicyVersion: Grant.PolicyVersion);
            Book = book;
            Journals.GetLedgerBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(book);
            var period = new LedgerAccountingPeriod(periodId, bookId, ObservedAt.Year, ObservedAt.Month, "Synthetic period",
                DateOnly.FromDateTime(ObservedAt.AddDays(-30).UtcDateTime), DateOnly.FromDateTime(ObservedAt.AddDays(30).UtcDateTime),
                "Open", ObservedAt, null, 3);
            var cash = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: AccountId);
            var capital = new LedgerAccount("Capital", LedgerAccountType.Equity, FinancialAccountId: AccountId);
            var carrying = new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL", AccountId);
            var opening = Journal("opening-balance", ObservedAt.AddDays(-1), [(cash, 100000m, 0m), (capital, 0m, 100000m)]);
            TradeJournal = Journal("execution.trade-fill", ObservedAt, [(carrying, 10000m, 0m), (cash, 0m, 10000m)],
                new Dictionary<string, string>
                {
                    ["tradeFill.price"] = "100",
                    ["tradeFill.side"] = "Buy",
                    ["sourceEventType"] = "execution.trade-fill",
                    ["automation.actionOrigin"] = "AuthorizedSystem"
                });
            var lotId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            var evidence = new RetainedEvidenceIdentityDto("acquisition-evidence", "evidence://synthetic/acquisition", new string('a', 64),
                "Synthetic", FillId.ToString("D"), "Accepted", "synthetic-source-validation", ObservedAt,
                DateOnly.FromDateTime(ObservedAt.UtcDateTime), 1, ObservedAt, "synthetic-source-validation", "OpenLotAcquisition", lotId.ToString("D"));
            Lot = new(lotId, bookId, carrying, "retained-lot", DateOnly.FromDateTime(ObservedAt.UtcDateTime),
                100m, 100m, 100m, "USD", ObservedAt, ObservedAt, Version: 1,
                OriginatingMutationBatchId: batchId, LastMutationBatchId: batchId, SecurityId: SecurityId, BookPositionId: PositionId,
                Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 10000m, 10000m,
                    DateOnly.FromDateTime(ObservedAt.UtcDateTime), null, [evidence]));
            var executionEvidence = evidence with
            {
                EvidenceId = "execution-fill",
                SourceSystem = "SyntheticOMS",
                SubjectType = "ExecutionFill",
                SubjectId = FillId.ToString("D")
            };
            Receipt = new AtomicTaxLotJournalResult(batchId, AtomicTaxLotMutationKind.Acquisition, "sha256:" + new string('b', 64),
                true, TradeJournal, [Lot], [], [evidence, executionEvidence]);
            Journals.GetAtomicTaxLotPostingAsync(batchId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<AtomicTaxLotJournalResult?>(Receipt));
            Snapshot = new LedgerReportingSnapshot(withHolding ? [opening, TradeJournal] : [opening], [], period)
            {
                OpenPositions = new LedgerReportingOpenPositions(DateOnly.FromDateTime(ObservedAt.UtcDateTime), withHolding ? [Lot] : [], [])
            };
            ((ILedgerOpenPositionReportingSnapshotSource)Journals).CaptureOpenPositionReportingSnapshotAsync(
                Arg.Any<LedgerJournalEntryQuery>(), periodId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Snapshot));
            Accounts.ResolveAsync(Arg.Any<ConfiguredLedgerAccountRequestDto>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<ConfiguredLedgerAccountRequestDto>();
                    return Task.FromResult(new ChartOfAccountsNodeDto(request.AccountPath, request.AccountPath,
                        request.AccountPath switch
                        {
                            "Assets:Cash" => "Cash",
                            "Revenue:RealizedGain" => "Realized Gain",
                            "Expense:RealizedLoss" => "Realized Loss",
                            _ => request.AccountPath.Split(':').Last()
                        }, request.ExpectedAccountType));
                });
            var emptyJson = JsonSerializer.SerializeToElement(new { });
            Security = new SecurityDetailDto(SecurityId,
                "Equity", SecurityStatusDto.Active, "AAPL synthetic", "USD", emptyJson, emptyJson, [], [], 1, ObservedAt.AddYears(-1), null);
            Securities.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<SecurityDetailDto?>(Security));
            Securities.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", null, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(_ => Task.FromResult<SecurityDetailDto?>(Security));
            Options = new()
            {
                Grant = Grant,
                CashAccountPath = "Assets:Cash",
                SecuritiesAccountPath = "Assets:Securities",
                RealizedGainAccountPath = "Revenue:RealizedGain",
                RealizedLossAccountPath = "Expense:RealizedLoss",
                CommissionAccountPath = "Expense:Commission",
                FeeAccountPath = "Expense:Fee"
            };
        }

        internal CanonicalPaperPortfolioBootstrapper Bootstrap(PaperTradingPortfolio portfolio, LedgerPostingConsumer? consumer = null,
            ITradeFillHandoffFailureStore? fallback = null) => new(Options, Context, portfolio, Journals, Accounts, Securities, consumer, fallback, Costs);

        internal PaperTradingPortfolio CreateWithFreshQuote()
        {
            var cache = new LiveMarketDataCache();
            cache.RecordQuote("AAPL", new BboQuotePayload(DateTimeOffset.UtcNow.AddSeconds(-1), "AAPL", 100m, 100, 100m, 100, 100m, 0m, 1));
            return CanonicalPaperPortfolioBootstrapper.CreatePortfolio(Options, cache);
        }

        internal OrderManagementSystem CreateOms(PaperTradingPortfolio portfolio, ITradeEventPublisher publisher)
            => new(new ExecutionGateway(NullLogger<ExecutionGateway>.Instance,
                    options: new PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }, costOptions: Costs),
                NullLogger<OrderManagementSystem>.Instance, portfolioState: portfolio, tradeEventPublisher: publisher);

        internal LedgerJournalEntryRecord CashAdjustment(decimal amount) => Journal("approved-cash-adjustment", DateTimeOffset.UtcNow,
            [(new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: AccountId), amount, 0m),
                (new LedgerAccount("Capital", LedgerAccountType.Equity, FinancialAccountId: AccountId), 0m, amount)]);

        private LedgerJournalEntryRecord Journal(string activity, DateTimeOffset timestamp,
            IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> amounts, IReadOnlyDictionary<string, string>? tags = null)
        {
            var id = Guid.NewGuid();
            var entry = new JournalEntry(id, timestamp, activity, amounts.Select(amount => new LedgerEntry(Guid.NewGuid(), id,
                timestamp, amount.Account, amount.Debit, amount.Credit, activity,
                currency: new LedgerEntryCurrency("USD", "USD", amount.Debit, amount.Credit, 1m))).ToArray(),
                new JournalEntryMetadata(ActivityType: activity, Symbol: activity == "execution.trade-fill" ? "AAPL" : null,
                    SecurityId: activity == "execution.trade-fill" ? SecurityId : null, FillId: activity == "execution.trade-fill" ? FillId : null,
                    FinancialAccountId: AccountId, EffectiveDate: DateOnly.FromDateTime(timestamp.UtcDateTime), Tags: tags));
            return new(entry, Context.AggregateId, Context.PeriodId, null, null, 1, timestamp,
                AccountingPolicyId: Grant.PolicyId, AccountingPolicyVersion: Grant.PolicyVersion);
        }
    }
}
