using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Event-Driven Decoupling types (Phase 1).
/// Validates <see cref="TradeExecutedEvent"/> and <see cref="LedgerPostingConsumer"/>.
/// </summary>
public sealed class EventDrivenDecouplingTests
{
    // -------------------------------------------------------------------------
    // TradeExecutedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void TradeExecutedEvent_GrossValue_IsQuantityTimesPrice()
    {
        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 1m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        evt.GrossValue.Should().Be(100 * 150m);
    }

    [Fact]
    public void TradeExecutedEvent_FinancialAccountId_DefaultsToNull()
    {
        var evt = new TradeExecutedEvent(Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            100, 150m, 0m, 0m, 85_000m, DateTimeOffset.UtcNow);

        evt.FinancialAccountId.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // LedgerPostingConsumer — synchronous equivalence via async drain
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LedgerPostingConsumer_BuyEvent_PostsSecuritiesAndCashEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new PassingSecurityValidationGate());

        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 0m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        consumer.Publish(evt);
        await consumer.DisposeAsync(); // flushes and drains channel

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Securities("AAPL")).Debit.Should()
            .Be(100 * 150m);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Cash).Credit.Should()
            .Be(100 * 150m);
        entry.Metadata.SecurityId.Should().Be(PassingSecurityValidationGate.SecurityId);
        entry.Metadata.Symbol.Should().Be("AAPL");
        entry.Metadata.FillId.Should().Be(evt.FillId);
        entry.Metadata.Tags.Should().ContainKey("securityMaster.gate")
            .WhoseValue.Should().Be("resolved-approved-mapped");
    }

    [Fact]
    public async Task LedgerPostingConsumer_BuyWithCommission_PostsThreeEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            50, 200m, Commission: 5m, RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2); // buy + commission
        ledger.Journal.Should().Contain(e => e.Description.Contains("Commission"));
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithGain_PostsRealizedGainEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-2", "AAPL", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 160m, Commission: 0m,
            RealizedPnl: 1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedGain && l.Credit == 1_000m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithLoss_PostsRealizedLossEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-3", "MSFT", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 140m, Commission: 0m,
            RealizedPnl: -1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedLoss && l.Debit == 1_000m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityValidationBlocks_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new BlockingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-blocked", "AAPL", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("blocked Security Master validation must prevent ledger posting");
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityValidationGateMissing_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance);

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-ungated", "AAPL", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("ledger posting now requires a configured Security Master gate");
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityIsUnresolved_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new UnresolvedSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-unresolved", "MISSING", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("ledger posting requires a resolved Security Master identity");
    }

    // -------------------------------------------------------------------------
    // LedgerPostingConsumer — fill durability under backpressure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LedgerPostingConsumer_WhenChannelIsFull_BlocksPublisherInsteadOfDroppingFills()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var gate = new BlockableSecurityValidationGate();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            channelCapacity: 1,
            securityValidationGate: gate);

        consumer.Publish(MakeBuyEvent("AAPL"));
        await gate.FirstValidationStarted; // consumer is now stalled inside the gate
        consumer.Publish(MakeBuyEvent("MSFT")); // fills the single-slot channel

        var blockedPublish = Task.Run(() => consumer.Publish(MakeBuyEvent("GOOG")));
        var winner = await Task.WhenAny(blockedPublish, Task.Delay(TimeSpan.FromMilliseconds(250)));
        winner.Should().NotBe(
            blockedPublish,
            "a publish against a full channel must block for capacity rather than drop the fill");

        gate.Release();
        await blockedPublish;
        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(3, "every published fill must reach the ledger");
    }

    [Fact]
    public async Task LedgerPostingConsumer_PublishAfterDispose_ThrowsInsteadOfSilentlyDropping()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            securityValidationGate: new PassingSecurityValidationGate());

        await consumer.DisposeAsync();

        var publish = () => consumer.Publish(MakeBuyEvent("AAPL"));
        publish.Should().Throw<ChannelClosedException>(
            "a disposed consumer must reject fills loudly, never drop them");
    }

    [Fact]
    public async Task LedgerPostingConsumer_DisposeDuringSaturation_IsBoundedAndPreventsLatePosting()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var gate = new NonCooperativeSecurityValidationGate();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            channelCapacity: 1,
            securityValidationGate: gate,
            drainTimeout: TimeSpan.FromMilliseconds(25),
            cancellationTimeout: TimeSpan.FromMilliseconds(25));

        consumer.Publish(MakeBuyEvent("AAPL"));
        await gate.ValidationStarted.WaitAsync(TimeSpan.FromSeconds(1));
        consumer.Publish(MakeBuyEvent("MSFT"));
        var blockedPublish = Task.Run(() => consumer.Publish(MakeBuyEvent("GOOG")));
        await Task.Delay(25);
        blockedPublish.IsCompleted.Should().BeFalse("the channel is saturated before disposal begins");

        var elapsed = Stopwatch.StartNew();
        await consumer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(750),
            "shutdown has finite drain and cancellation phases even when a dependency ignores cancellation");
        Func<Task> blockedPublishAction = async () => await blockedPublish;
        await blockedPublishAction.Should().ThrowAsync<ChannelClosedException>(
            "a publisher waiting for capacity must fail deterministically when disposal closes the channel");

        await consumer.DisposeAsync();
        gate.Release();
        Func<Task> processingAction = async () => await consumer.ProcessingCompletion;
        await processingAction.Should().ThrowAsync<OperationCanceledException>();

        ledger.Journal.Should().BeEmpty(
            "an in-flight validation that returns after the disposal boundary cannot mutate the ledger");
        var publishAfterDispose = () => consumer.Publish(MakeBuyEvent("NVDA"));
        publishAfterDispose.Should().Throw<ChannelClosedException>();
    }

    private static TradeExecutedEvent MakeBuyEvent(string symbol) => new(
        Guid.NewGuid(), "ord-bp", symbol, OrderSide.Buy,
        FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
        RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow);

    /// <summary>
    /// Passing gate whose first validation stalls until <see cref="Release"/> is called,
    /// letting tests hold the consumer mid-event while the channel backs up.
    /// </summary>
    private sealed class BlockableSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstValidationStarted => _firstValidationStarted.Task;

        public void Release() => _released.TrySetResult();

        public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            _firstValidationStarted.TrySetResult();
            await _released.Task.WaitAsync(ct);
            return await _inner.ValidateSymbolAsync(symbol, workflow, workflowReference, actor, persistSnapshot, ct);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(securityId, workflow, workflowReference, actor, persistSnapshot, symbol, ct);
    }

    /// <summary>
    /// Deliberately ignores the supplied cancellation token while validation is blocked. This
    /// models a provider or remote dependency that does not cooperate with shutdown promptly.
    /// </summary>
    private sealed class NonCooperativeSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _validationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ValidationStarted => _validationStarted.Task;

        public void Release() => _release.TrySetResult();

        public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            _validationStarted.TrySetResult();
            await _release.Task;
            return await _inner.ValidateSymbolAsync(
                symbol,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                CancellationToken.None);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(
                securityId,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                symbol,
                CancellationToken.None);
    }

    private sealed class PassingSecurityValidationGate : ISecurityValidationGateService
    {
        public static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId,
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: false,
                CriticalIssueCount: 0,
                ErrorIssueCount: 0,
                Issues: []);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                report.SecurityId,
                IsResolved: true,
                IsBlocked: false,
                Report: report,
                Snapshot: null);
        }
    }

    private sealed class BlockingSecurityValidationGate : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                        "Accounting classification is missing",
                        "The record does not expose an accounting classification for ledger posting.",
                        ["commonTerms.accountingClassification"],
                        "Attach the ledger/reporting accounting classification.",
                        [])
                ]);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                report.SecurityId,
                IsResolved: true,
                IsBlocked: true,
                Report: report,
                Snapshot: null);
        }
    }

    private sealed class UnresolvedSecurityValidationGate : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId: null,
                Scope: "SecurityMasterResolution",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_SYMBOL_UNRESOLVED",
                        "Security Master identity is unresolved",
                        $"Ticker '{symbol}' could not be resolved.",
                        ["symbol"],
                        "Resolve the instrument before posting.",
                        [])
                ]);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                SecurityId: null,
                IsResolved: false,
                IsBlocked: true,
                Report: report,
                Snapshot: null);
        }
    }
}
