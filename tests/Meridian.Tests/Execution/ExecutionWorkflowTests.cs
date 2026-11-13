using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Execution;

public sealed class ExecutionWorkflowTests
{
    private static readonly Guid Account = Guid.Parse("8c6b0029-9422-4961-ad61-a0ab6cbb242d");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-10T15:00:00Z");
    private static string Root() => Path.Combine(Path.GetTempPath(), "Meridian.Tests", "execution-workflows", Guid.NewGuid().ToString("N"));
    private static OrderRequest Request(string id = "order-1") => new()
    { ClientOrderId = id, FundAccountId = Account, Symbol = "SPY", Side = OrderSide.Buy, Type = OrderType.Limit, Quantity = 2m, LimitPrice = 100m };
    private static GatewayResolvedOrder Resolve(OrderRequest request)
    {
        ValidatedOrder.TryCreate(request, out var validated, out _).Should().BeTrue();
        GatewayResolvedOrder.TryResolve(validated!, Mock.Of<IExecutionGateway>(), out var resolved, out _).Should().BeTrue();
        return resolved!;
    }

    [Fact]
    public async Task Preview_DoesNotConsumeRateCapacity_AndSubmissionStillReserves()
    {
        var throttle = new OrderRateThrottle(1, NullLogger<OrderRateThrottle>.Instance);
        var validator = new CompositeRiskValidator([throttle], NullLogger<CompositeRiskValidator>.Instance);
        for (var index = 0; index < 3; index++)
        {
            var preview = await validator.PreviewResolvedOrderAsync(Resolve(Request()));
            preview.IsApproved.Should().BeTrue();
            preview.Reservations.Should().BeEmpty();
            throttle.CurrentUsage.Should().Be(0);
        }
        var admitted = await validator.ValidateOrderAsync(Request());
        throttle.CurrentUsage.Should().Be(1);
        var blockedPreview = await validator.PreviewResolvedOrderAsync(Resolve(Request("order-2")));
        blockedPreview.IsApproved.Should().BeFalse();
        foreach (var reservation in admitted.Reservations)
            reservation.Rollback();
        throttle.CurrentUsage.Should().Be(0);
    }

    [Fact]
    public async Task Preview_CriticalFindingDoesNotTripControls_AndEscalationDoesNotPark()
    {
        var trip = new Mock<ICircuitBreakerTripHandler>(MockBehavior.Strict);
        var critical = new Mock<IRiskRule>();
        critical.SetupGet(rule => rule.RuleName).Returns("Critical");
        critical.SetupGet(rule => rule.Severity).Returns(RiskRuleSeverity.Critical);
        critical.Setup(rule => rule.EvaluateAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RiskValidationResult.Rejected("Known critical breach"));
        var validator = new CompositeRiskValidator([critical.Object], NullLogger<CompositeRiskValidator>.Instance, tripHandler: trip.Object);
        (await validator.PreviewResolvedOrderAsync(Resolve(Request()))).IsApproved.Should().BeFalse();
        trip.VerifyNoOtherCalls();
        var escalation = new Mock<IRiskRule>();
        escalation.SetupGet(rule => rule.RuleName).Returns("Approval");
        escalation.SetupGet(rule => rule.Severity).Returns(RiskRuleSeverity.Escalate);
        escalation.Setup(rule => rule.EvaluateAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RiskValidationResult.Rejected("Needs approval"));
        var observing = new CompositeRiskValidator([escalation.Object], NullLogger<CompositeRiskValidator>.Instance);
        var result = await observing.PreviewResolvedOrderAsync(Resolve(Request()));
        result.RequiresApproval.Should().BeTrue();
        result.EscalationId.Should().BeNull();
    }

    [Fact]
    public async Task Journal_ReplaysDurably_DeduplicatesExactRetries_AndRejectsConflicts()
    {
        var root = Root();
        var entry = new ExecutionJournalEntry(1, "event-1", "paper:account-1", Account, "order-1", "Intent", At, Request: Request());
        await using (var journal = new ExecutionJournal(root))
        {
            await journal.AppendAsync(entry);
            await journal.AppendAsync(entry with { RecordedAt = At.AddSeconds(5) });
            (await journal.ReadAsync(Account)).Should().HaveCount(1);
            var conflicting = async () => await journal.AppendAsync(entry with { Request = Request() with { Quantity = 200m } });
            await conflicting.Should().ThrowAsync<InvalidDataException>();
        }
        await using var restarted = new ExecutionJournal(root);
        (await restarted.OrdersAsync(Account)).Should().ContainSingle().Which.Quantity.Should().Be(2m);
        (await restarted.ReadAsync(Guid.NewGuid())).Should().BeEmpty();
        await restarted.AppendAsync(entry with { Scope = "live:account-1" });
        (await restarted.ReadAsync(Account)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Journal_UnknownCostAndOpeningBasisDoNotBecomeZeroProfit()
    {
        await using var journal = new ExecutionJournal(Root());
        await journal.AppendAsync(new(1, "fill-1", "paper", Account, "sell", "Fill", At, Report: new()
        {
            OrderId = "sell",
            Symbol = "SPY",
            Side = OrderSide.Sell,
            FilledQuantity = 1m,
            FillPrice = 100m,
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            Timestamp = At
        }));
        var summary = await journal.SummaryAsync(Account);
        summary.RealizedPnl.Should().BeNull();
        summary.Commissions.Should().BeNull();
        summary.Fees.Should().BeNull();
        summary.Slippage.Should().BeNull();
        summary.Coverage.Should().Be("incomplete");
    }

    [Fact]
    public async Task DailyLoss_AdjustsExternalCashFlows_AndRetainsOneAlertAcrossRestart()
    {
        var root = Root();
        await using (var journal = new ExecutionJournal(root))
        {
            var alerts = new ExecutionAlertService(journal, new FrozenClock(At.AddSeconds(2)));
            await alerts.SaveSettingsAsync(new(Account, DailyLossLimitUsd: 100m), "paper");
            // A withdrawal is not a trading loss.
            await alerts.ObserveEquityAsync(new(Account, "paper", new(2026, 11, 10), At, 900m, 1000m, -100m, true));
            (await alerts.NotificationsAsync(Account, "paper")).Should().BeEmpty();
            await alerts.ObserveEquityAsync(new(Account, "paper", new(2026, 11, 10), At.AddSeconds(1), 799m, 1000m, -100m, true));
        }
        await using var restarted = new ExecutionJournal(root);
        var recovered = new ExecutionAlertService(restarted, new FrozenClock(At.AddSeconds(5)));
        await recovered.ObserveEquityAsync(new(Account, "paper", new(2026, 11, 10), At.AddSeconds(5), 700m, 1000m, -100m, true));
        (await recovered.NotificationsAsync(Account, "paper")).Should().ContainSingle(notification => notification.Title == "Daily loss threshold breached");
        (await recovered.NotificationsAsync(Account, "live")).Should().BeEmpty();
    }

    [Fact]
    public async Task DailyLoss_MissingBaselineRaisesMonitoringGap_InsteadOfClaimingSafety()
    {
        await using var journal = new ExecutionJournal(Root());
        var alerts = new ExecutionAlertService(journal, new FrozenClock(At));
        await alerts.SaveSettingsAsync(new(Account, DailyLossLimitUsd: 100m), "paper");
        await alerts.ObserveEquityAsync(new(Account, "paper", new(2026, 11, 10), At, 800m, null, 0m, false));
        (await alerts.NotificationsAsync(Account, "paper")).Should().ContainSingle(notification => notification.Title == "Daily loss monitoring incomplete");
        (await alerts.SettingsAsync(Account, "paper")).WebhookEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Journal_FifoUsesOpeningAttribution_AndIncludesAcquisitionAndExitCosts()
    {
        await using var journal = new ExecutionJournal(Root())
        { OpeningEvidenceSource = (_, _) => new(true, true, At.AddSeconds(-1), "test opening book") };
        async Task Fill(string id, OrderSide side, decimal quantity, decimal price, decimal commission, int seconds, string tag)
        {
            var request = Request(id) with { Side = side, Quantity = quantity, StrategyId = tag, Metadata = new Dictionary<string, string> { ["tag"] = tag } };
            await journal.AppendAsync(new(1, $"dispatch:{id}", "paper", Account, id, "Dispatch", At.AddSeconds(seconds), Request: request, ReportingAssetClass: "USD_US_EQUITY"));
            await journal.AppendAsync(new(1, $"fill:{id}", "paper", Account, id, "Fill", At.AddSeconds(seconds), Report: new()
            {
                OrderId = id,
                Symbol = "SPY",
                Side = side,
                OrderQuantity = quantity,
                FilledQuantity = quantity,
                FillPrice = price,
                Commission = commission,
                Fees = 0m,
                OrderStatus = OrderStatus.Filled,
                ReportType = ExecutionReportType.Fill,
                Timestamp = At.AddSeconds(seconds)
            }));
        }
        await Fill("buy-a", OrderSide.Buy, 2m, 100m, 2m, 0, "a");
        await Fill("buy-b", OrderSide.Buy, 2m, 120m, 2m, 1, "b");
        await Fill("sell-a", OrderSide.Sell, 3m, 130m, 3m, 2, "exit");
        await Fill("sell-b", OrderSide.Sell, 1m, 110m, 1m, 3, "exit");
        await journal.AppendAsync(new(1, "rejected-intent", "paper", Account, "refused", "Rejected", At, Request: Request("refused")));
        var summary = await journal.SummaryAsync(Account, scope: "paper");
        summary.RealizedPnl.Should().Be(60m);
        summary.NetRealizedPnl.Should().Be(52m);
        summary.Attributions.Should().Contain(row => row.Tag == "a" && row.GrossRealizedPnl == 60m && row.NetRealizedPnl == 56m);
        var window = await journal.SummaryAsync(Account, scope: "paper", from: At.AddSeconds(2), to: At.AddSeconds(3));
        window.RealizedPnl.Should().Be(70m);
        window.NetRealizedPnl.Should().Be(64m);
        await journal.AppendAsync(new(1, "split", "paper", Account, "", "EconomicCoverageGap", At.AddSeconds(4)));
        (await journal.SummaryAsync(Account, scope: "paper")).RealizedPnl.Should().BeNull();
    }

    [Fact]
    public async Task Journal_OrderAndSummaryDateBoundsIncludeStartAndExcludeEnd()
    {
        await using var journal = new ExecutionJournal(Root());
        var from = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        foreach (var (id, at) in new[]
        {
            ("before-from", from.AddTicks(-1)), ("at-from", from),
            ("before-to", to.AddTicks(-1)), ("at-to", to), ("after-to", to.AddTicks(1))
        })
            await journal.AppendAsync(new(1, $"rejected:{id}", "paper", Account, id, "Rejected", at, Request: Request(id)));

        var orders = await journal.OrdersAsync(Account, from: from, to: to, scope: "paper");
        orders.Select(order => order.OrderId).Should().BeEquivalentTo(new[] { "at-from", "before-to" });
        var summary = await journal.SummaryAsync(Account, scope: "paper", from: from, to: to);
        summary.Orders.Should().Be(orders.Count);
    }

    [Fact]
    public async Task Journal_UnpricedExecutionInvalidatesEconomics_EvenWithProvedFlatOpening()
    {
        await using var journal = new ExecutionJournal(Root())
        { OpeningEvidenceSource = (_, _) => new(true, true, At, "test opening book") };
        foreach (var side in new[] { OrderSide.Buy, OrderSide.Sell })
        {
            var id = side.ToString();
            await journal.AppendAsync(new(1, $"dispatch:{id}", "paper", Account, id, "Dispatch", At, Request: Request(id) with { Side = side, Quantity = 1m }, ReportingAssetClass: "USD_US_EQUITY"));
            await journal.AppendAsync(new(1, $"fill:{id}", "paper", Account, id, "Fill", At, Report: new()
            {
                OrderId = id,
                Symbol = "SPY",
                Side = side,
                OrderQuantity = 1m,
                FilledQuantity = 1m,
                FillPrice = side == OrderSide.Buy ? 100m : null,
                Commission = 0m,
                Fees = 0m,
                OrderStatus = OrderStatus.Filled,
                ReportType = ExecutionReportType.Fill,
                Timestamp = At
            }));
        }
        var result = await journal.SummaryAsync(Account, scope: "paper");
        result.RealizedPnl.Should().BeNull();
        result.NetRealizedPnl.Should().BeNull();
        result.Commissions.Should().BeNull();
        result.Coverage.Should().Be("incomplete");
    }

    [Fact]
    public async Task Alerts_ConcurrentConditionObservationsRetainExactlyOneNotification()
    {
        await using var journal = new ExecutionJournal(Root());
        var alerts = new ExecutionAlertService(journal);
        await Task.WhenAll(Enumerable.Range(0, 25).Select(index => alerts.PublishAsync(Account, "paper", "loss:date",
            "critical", "Threshold", $"Observation {index}", At.AddSeconds(index))));
        (await alerts.NotificationsAsync(Account, "paper")).Should().ContainSingle();
    }

    [Fact]
    public async Task WebhookOutbox_DeliveredFirstBatchDoesNotStarveLaterNotifications()
    {
        await using var journal = new ExecutionJournal(Root());
        var alerts = new ExecutionAlertService(journal);
        for (var index = 0; index < 7; index++)
            await alerts.PublishAsync(Account, "paper", $"fill:{index}", "success", "Fill", $"Execution {index}", At.AddSeconds(index));
        var first = await alerts.PendingWebhookDeliveriesAsync(Account, "paper");
        first.Should().HaveCount(4);
        foreach (var delivery in first)
            await journal.AppendAsync(new(1, $"delivered:{delivery.Notification.Id}", "paper", Account, "", "WebhookDelivered", At));
        var next = await alerts.PendingWebhookDeliveriesAsync(Account, "paper");
        next.Should().HaveCount(3);
        next.Should().NotContain(delivery => first.Any(prior => prior.Notification.Id == delivery.Notification.Id));
        (await alerts.PendingWebhookDeliveriesAsync(Account, "live")).Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewService_ApprovalRemainsSubmittable_AndFreshQuoteIsMandatory()
    {
        var rule = new Mock<IRiskRule>();
        rule.SetupGet(value => value.RuleName).Returns("Approval");
        rule.SetupGet(value => value.Severity).Returns(RiskRuleSeverity.Escalate);
        rule.Setup(value => value.EvaluateAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RiskValidationResult.Rejected("Price requires approval"));
        var risk = new CompositeRiskValidator([rule.Object], NullLogger<CompositeRiskValidator>.Instance);
        var prices = new Mock<IOrderMarketReferenceSource>();
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(new OrderMarketReference(100m, At, "test quote"));
        var coverage = new Mock<IOrderInstrumentCoverageSource>();
        coverage.Setup(source => source.IsUsdEquityAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new OrderPreviewService(Mock.Of<IExecutionGateway>(), Mock.Of<IOrderManager>(), risk,
            prices.Object, NullLogger<OrderPreviewService>.Instance, clock: new FrozenClock(At), coverage: coverage.Object);
        var preview = await service.PreviewAsync(Request());
        preview.RequiresApproval.Should().BeTrue();
        preview.CanSubmit.Should().BeTrue();
        preview.ExpiresAt.Should().Be(At.AddSeconds(30));
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(new OrderMarketReference(100m, At.AddMinutes(-1), "stale quote"));
        (await service.PreviewAsync(Request())).CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task SynchronousAcknowledgement_StopsAdmissionWhenJournalRetentionFails()
    {
        var journal = new ExecutionJournal(Root());
        var gateway = new Mock<IExecutionGateway>();
        gateway.SetupGet(value => value.GatewayId).Returns("paper");
        gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>())).Returns(EmptyReports());
        gateway.Setup(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (OrderRequest request, CancellationToken _) =>
            {
                await journal.DisposeAsync(); // Simulate loss of the retained-report sink after dispatch.
                return new ExecutionReport
                {
                    OrderId = request.ClientOrderId!,
                    ClientOrderId = request.ClientOrderId,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    ReportType = ExecutionReportType.Fill,
                    OrderStatus = OrderStatus.Filled,
                    OrderQuantity = request.Quantity,
                    FilledQuantity = request.Quantity,
                    FillPrice = 100m
                };
            });
        var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance, journal: journal);
        await oms.StartAsync();
        var outcome = await oms.PlaceOrderAsync(Request());
        outcome.Success.Should().BeFalse();
        var retry = async () => await oms.PlaceOrderAsync(Request("next"));
        await retry.Should().ThrowAsync<InvalidOperationException>();
        gateway.Verify(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var shutdown = async () => await oms.DisposeAsync();
        await shutdown.Should().ThrowAsync<OrderManagementSystem.ExecutionReportDeliveryException>();
    }
    private static async IAsyncEnumerable<ExecutionReport> EmptyReports() { await Task.CompletedTask; yield break; }

    [Fact]
    public async Task CancelAndCancelAll_ConfirmWorkingOrdersWhenJournalIsUnavailable()
    {
        var journal = new ExecutionJournal(Root());
        var gateway = new Mock<IExecutionGateway>();
        gateway.SetupGet(value => value.GatewayId).Returns("paper");
        gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>())).Returns(EmptyReports());
        gateway.Setup(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderRequest request, CancellationToken _) => new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity
            });
        gateway.Setup(value => value.CancelOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string orderId, CancellationToken _) => new ExecutionReport
            {
                OrderId = orderId,
                ClientOrderId = orderId,
                Symbol = "SPY",
                Side = OrderSide.Buy,
                ReportType = ExecutionReportType.Cancelled,
                OrderStatus = OrderStatus.Cancelled,
                OrderQuantity = 2m,
                FilledQuantity = orderId == "manual-cancel" ? 1m : 0m,
                FillPrice = orderId == "manual-cancel" ? 100m : null
            });
        var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance, journal: journal);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(Request("manual-cancel"))).Success.Should().BeTrue();
        (await oms.PlaceOrderAsync(Request("sweep-cancel"))).Success.Should().BeTrue();

        await journal.DisposeAsync();

        var manual = await oms.CancelOrderAsync("manual-cancel");
        manual.Success.Should().BeTrue();
        manual.OrderState!.Status.Should().Be(OrderStatus.Cancelled);
        manual.OrderState.FilledQuantity.Should().Be(1m);
        var sweep = await oms.CancelAllAsync();
        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Cancelled.Should().Be(1);
        sweep.StillWorking.Should().BeEmpty();
        gateway.Verify(value => value.CancelOrderAsync("manual-cancel", It.IsAny<CancellationToken>()), Times.Once);
        gateway.Verify(value => value.CancelOrderAsync("sweep-cancel", It.IsAny<CancellationToken>()), Times.Once);
        var submit = async () => await oms.PlaceOrderAsync(Request("new-order"));
        await submit.Should().ThrowAsync<InvalidOperationException>();
        var shutdown = async () => await oms.DisposeAsync();
        await shutdown.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void BrokerEquityEvidence_RejectsMalformedBalances_AndTransfersDuringValuation()
    {
        var balance = new AccountInfo { AccountId = "external", Equity = 800m, PreviousCloseEquity = 1000m, EquityEvidenceComplete = true };
        var close = At.AddHours(-18);
        var withdrawal = new BrokerageActivityEventDto("withdrawal", "CSD", BrokerageActivityCategory.Transfer,
            BrokerageActivitySubtype.CashWithdrawal, At.AddMinutes(-1), "USD", -200m);
        var activity = new BrokerageActivitySnapshotDto("alpaca", "external", At.AddSeconds(1), [], [], [],
            Activities: [withdrawal], Cursor: new(null, null, 1, 1, true));
        ExecutionEquityObservation Observe(AccountInfo info, BrokerageActivitySnapshotDto source) =>
            ExecutionEquityEvidence.FromBroker(Account, "paper", new(2026, 11, 10), At, At.AddSeconds(1), close, info, source);
        var valid = Observe(balance, activity);
        valid.IsComplete.Should().BeTrue();
        valid.NetExternalFlows.Should().Be(-200m);
        Observe(balance with { EquityEvidenceComplete = false, Equity = 0m }, activity).Equity.Should().BeNull();
        Observe(balance, activity with { Activities = [withdrawal with { EffectiveAt = At }] }).IsComplete.Should().BeFalse();
        Observe(balance, activity with { Cursor = null }).IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task DirectAdmission_RechecksQuotesButAllowsQuantityReductionWithoutFreshQuote()
    {
        var gateway = new Mock<IExecutionGateway>();
        gateway.SetupGet(value => value.GatewayId).Returns("paper");
        gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>())).Returns(EmptyReports());
        gateway.Setup(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderRequest request, CancellationToken _) => new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity
            });
        var coverage = new Mock<IOrderInstrumentCoverageSource>();
        coverage.Setup(source => source.IsUsdEquityAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var prices = new Mock<IOrderMarketReferenceSource>();
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(new OrderMarketReference(100m, DateTimeOffset.UtcNow.AddMinutes(-2), "stale"));
        await using var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance,
            marketReferences: prices.Object, instrumentCoverage: coverage.Object);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(Request("stale"))).Success.Should().BeFalse();
        gateway.Verify(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(() => new OrderMarketReference(100m, DateTimeOffset.UtcNow.AddMilliseconds(-1), "fresh"));
        (await oms.PlaceOrderAsync(Request("fresh"))).Success.Should().BeTrue();
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(new OrderMarketReference(100m, DateTimeOffset.UtcNow.AddMinutes(-2), "stale"));
        gateway.Setup(value => value.ModifyOrderAsync("fresh", It.IsAny<OrderModification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionReport
            {
                OrderId = "fresh",
                Symbol = "SPY",
                Side = OrderSide.Buy,
                ReportType = ExecutionReportType.Modified,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = 1m
            });
        (await oms.ModifyOrderAsync("fresh", new() { NewQuantity = 1m })).Success.Should().BeTrue();
    }

    [Fact]
    public async Task QuoteExpiringDuringAdmission_PreventsDispatchAndRollsBackCapacity()
    {
        var gateway = new Mock<IExecutionGateway>();
        gateway.SetupGet(value => value.GatewayId).Returns("paper");
        gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>())).Returns(EmptyReports());
        var coverage = new Mock<IOrderInstrumentCoverageSource>();
        coverage.Setup(source => source.IsUsdEquityAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var observations = 0;
        var prices = new Mock<IOrderMarketReferenceSource>();
        prices.Setup(source => source.GetReference("SPY", OrderSide.Buy)).Returns(() =>
            new OrderMarketReference(100m, DateTimeOffset.UtcNow.AddSeconds(++observations == 1 ? -1 : -31), "quote"));
        var throttle = new OrderRateThrottle(1, NullLogger<OrderRateThrottle>.Instance);
        var risk = new CompositeRiskValidator([throttle], NullLogger<CompositeRiskValidator>.Instance);
        await using var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance,
            riskValidator: risk, marketReferences: prices.Object, instrumentCoverage: coverage.Object);
        await oms.StartAsync();
        var result = await oms.PlaceOrderAsync(Request());
        result.Success.Should().BeFalse();
        result.OrderState.Should().BeOfType<OrderState>().Which.Status.Should().Be(OrderStatus.Rejected);
        throttle.CurrentUsage.Should().Be(0);
        gateway.Verify(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
