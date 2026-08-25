using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DailyAccrualWorkerTests
{
    [Fact]
    public async Task RunAccrualBatchAsync_PostsActiveLoanAccrual_WhenLedgerPeriodAllowsOriginatingPosting()
    {
        var loanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accrualDate = new DateOnly(2026, 3, 24);
        var period = BuildPeriod("Open");
        var commandService = Substitute.For<IDirectLendingCommandService>();
        commandService.PostDailyAccrualAsync(
                loanId,
                Arg.Is<PostDailyAccrualRequest>(request => request.AccrualDate == accrualDate),
                Arg.Any<DirectLendingCommandMetadataDto?>(),
                Arg.Any<CancellationToken>())
            .Returns(DirectLendingCommandResult<DailyAccrualEntryDto>.Success(BuildAccrual(accrualDate)));

        var worker = CreateWorker(
            loanId,
            BuildServicing(loanId, accrualDate.AddDays(-1)),
            commandService,
            BuildLedgerStore(loanId, period),
            Substitute.For<IOperatorInboxService>());

        await worker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        await commandService.Received(1).PostDailyAccrualAsync(
            loanId,
            Arg.Is<PostDailyAccrualRequest>(request => request.AccrualDate == accrualDate),
            Arg.Any<DirectLendingCommandMetadataDto?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAccrualBatchAsync_RoutesPeriodBlockedWorkItem_WhenOriginatingAccrualTargetsSoftClosedPeriod()
    {
        var loanId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var accrualDate = new DateOnly(2026, 3, 24);
        var period = BuildPeriod("SoftClosed");
        var commandService = Substitute.For<IDirectLendingCommandService>();
        var operatorInbox = Substitute.For<IOperatorInboxService>();
        OperatorWorkItemDto? capturedWorkItem = null;
        operatorInbox
            .When(service => service.UpsertItemAsync(Arg.Any<OperatorWorkItemDto>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedWorkItem = call.Arg<OperatorWorkItemDto>());

        var worker = CreateWorker(
            loanId,
            BuildServicing(loanId, accrualDate.AddDays(-1)),
            commandService,
            BuildLedgerStore(loanId, period),
            operatorInbox);

        await worker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        await commandService.DidNotReceive().PostDailyAccrualAsync(
            Arg.Any<Guid>(),
            Arg.Any<PostDailyAccrualRequest>(),
            Arg.Any<DirectLendingCommandMetadataDto?>(),
            Arg.Any<CancellationToken>());
        await operatorInbox.Received(1).UpsertItemAsync(
            Arg.Any<OperatorWorkItemDto>(),
            Arg.Any<CancellationToken>());
        capturedWorkItem.Should().NotBeNull();
        capturedWorkItem!.WorkItemId.Should().Be($"direct-lending-period-blocked:{loanId:D}:20260324");
        capturedWorkItem.Kind.Should().Be(OperatorWorkItemKindDto.LedgerPeriodClose);
        capturedWorkItem.Workspace.Should().Be("Accounting");
        capturedWorkItem.TargetRoute.Should().Be("/accounting/reconciliation");
        capturedWorkItem.TargetPageTag.Should().Be("FundReconciliation");
        capturedWorkItem.Scope.Should().Be("DirectLendingAccrual");
        capturedWorkItem.Detail.Should().ContainEquivalentOf("soft-closed");
    }

    [Fact]
    public async Task RunAccrualBatchAsync_CatchesUpEveryMissedAccrualDateInOrder_AfterWorkerDowntime()
    {
        var loanId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var accrualDate = new DateOnly(2026, 3, 24);
        var period = BuildPeriod("Open");
        var (commandService, postedDates) = BuildCapturingCommandService(loanId);

        var worker = CreateWorker(
            loanId,
            BuildServicing(loanId, accrualDate.AddDays(-3)),
            commandService,
            BuildLedgerStore(loanId, period),
            Substitute.For<IOperatorInboxService>());

        await worker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        postedDates.Should().Equal(
            accrualDate.AddDays(-2),
            accrualDate.AddDays(-1),
            accrualDate);
    }

    [Fact]
    public async Task RunAccrualBatchAsync_HaltsCatchUpAtBlockedMiddleDate_ThenRetriesItNextCycleOncePeriodReopens()
    {
        var loanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var accrualDate = new DateOnly(2026, 3, 24);
        var firstDay = BuildPeriod("Open", Guid.Parse("03330001-3333-3333-3333-333333333333"), new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22));
        var blockedMiddleDay = BuildPeriod("SoftClosed", Guid.Parse("03330002-3333-3333-3333-333333333333"), new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 23));
        var lastDay = BuildPeriod("Open", Guid.Parse("03330003-3333-3333-3333-333333333333"), new DateOnly(2026, 3, 24), new DateOnly(2026, 3, 24));
        var (commandService, postedDates) = BuildCapturingCommandService(loanId);
        var operatorInbox = Substitute.For<IOperatorInboxService>();
        var capturedWorkItems = new List<OperatorWorkItemDto>();
        operatorInbox
            .When(service => service.UpsertItemAsync(Arg.Any<OperatorWorkItemDto>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedWorkItems.Add(call.Arg<OperatorWorkItemDto>()));

        var blockedCycleWorker = CreateWorker(
            loanId,
            BuildServicing(loanId, accrualDate.AddDays(-3)),
            commandService,
            BuildLedgerStore(loanId, firstDay, [firstDay, blockedMiddleDay, lastDay]),
            operatorInbox);

        await blockedCycleWorker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        postedDates.Should().Equal(new DateOnly(2026, 3, 22));
        capturedWorkItems.Should().ContainSingle()
            .Which.WorkItemId.Should().Be($"direct-lending-period-blocked:{loanId:D}:20260323");

        // Next cycle: the successful 2026-03-22 accrual advanced servicing, the middle period has
        // reopened, and the blocked date is retried before later dates.
        var reopenedMiddleDay = blockedMiddleDay with { Status = "Open", ClosedAt = null };
        var retryCycleWorker = CreateWorker(
            loanId,
            BuildServicing(loanId, accrualDate.AddDays(-2)),
            commandService,
            BuildLedgerStore(loanId, firstDay, [firstDay, reopenedMiddleDay, lastDay]),
            operatorInbox);

        await retryCycleWorker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        postedDates.Should().Equal(
            new DateOnly(2026, 3, 22),
            new DateOnly(2026, 3, 23),
            new DateOnly(2026, 3, 24));
    }

    [Fact]
    public async Task RunAccrualBatchAsync_TruncatesCatchUpAtBoundWithWarning_WhenGapExceedsMaxCatchUpDays()
    {
        var loanId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var accrualDate = new DateOnly(2026, 3, 24);
        var lastAccrualDate = accrualDate.AddDays(-400);
        var (commandService, postedDates) = BuildCapturingCommandService(loanId);
        var ledgerJournalStore = Substitute.For<ILedgerJournalStore>();
        ledgerJournalStore.GetByAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns([]);
        var logger = new RecordingLogger<DailyAccrualWorker>();

        var worker = CreateWorker(
            loanId,
            BuildServicing(loanId, lastAccrualDate),
            commandService,
            ledgerJournalStore,
            Substitute.For<IOperatorInboxService>(),
            logger);

        await worker.RunAccrualBatchAsync(accrualDate, CancellationToken.None);

        postedDates.Should().HaveCount(DailyAccrualWorker.MaxCatchUpDays);
        postedDates.First().Should().Be(lastAccrualDate.AddDays(1));
        postedDates.Last().Should().Be(lastAccrualDate.AddDays(DailyAccrualWorker.MaxCatchUpDays));
        postedDates.Should().NotContain(accrualDate);
        logger.Entries.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("catch-up bound") &&
            entry.Message.Contains("400"));
    }

    private static (IDirectLendingCommandService CommandService, List<DateOnly> PostedDates) BuildCapturingCommandService(Guid loanId)
    {
        var postedDates = new List<DateOnly>();
        var commandService = Substitute.For<IDirectLendingCommandService>();
        commandService.PostDailyAccrualAsync(
                loanId,
                Arg.Any<PostDailyAccrualRequest>(),
                Arg.Any<DirectLendingCommandMetadataDto?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<PostDailyAccrualRequest>();
                postedDates.Add(request.AccrualDate);
                return DirectLendingCommandResult<DailyAccrualEntryDto>.Success(BuildAccrual(request.AccrualDate));
            });
        return (commandService, postedDates);
    }

    private static DailyAccrualWorker CreateWorker(
        Guid loanId,
        LoanServicingStateDto servicing,
        IDirectLendingCommandService commandService,
        ILedgerJournalStore ledgerJournalStore,
        IOperatorInboxService operatorInbox,
        ILogger<DailyAccrualWorker>? logger = null)
    {
        var operationsStore = Substitute.For<IDirectLendingOperationsStore>();
        operationsStore.GetLoanIdsAsync(Arg.Any<CancellationToken>())
            .Returns([loanId]);

        var queryService = Substitute.For<IDirectLendingQueryService>();
        queryService.GetServicingStateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns(servicing);

        return new DailyAccrualWorker(
            operationsStore,
            queryService,
            commandService,
            logger ?? NullLogger<DailyAccrualWorker>.Instance,
            ledgerJournalStore,
            operatorInbox);
    }

    private static ILedgerJournalStore BuildLedgerStore(
        Guid loanId,
        LedgerAccountingPeriod period,
        IReadOnlyList<LedgerAccountingPeriod>? allPeriods = null)
    {
        var ledgerJournalStore = Substitute.For<ILedgerJournalStore>();
        ledgerJournalStore.GetByAggregateAsync(loanId, Arg.Any<CancellationToken>())
            .Returns([BuildJournalRecord(loanId, period.PeriodId)]);
        ledgerJournalStore.GetPeriodAsync(period.PeriodId, Arg.Any<CancellationToken>())
            .Returns(period);
        ledgerJournalStore.ListPeriodsAsync(
                period.LedgerBookId,
                null,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(allPeriods ?? [period]);

        return ledgerJournalStore;
    }

    private static LoanServicingStateDto BuildServicing(Guid loanId, DateOnly lastAccrualDate) =>
        new(
            LoanId: loanId,
            Status: LoanStatus.Active,
            CurrentCommitment: 1_000_000m,
            TotalDrawn: 500_000m,
            AvailableToDraw: 500_000m,
            Balances: new OutstandingBalancesDto(
                PrincipalOutstanding: 500_000m,
                InterestAccruedUnpaid: 0m,
                CommitmentFeeAccruedUnpaid: 0m,
                FeesAccruedUnpaid: 0m,
                PenaltyAccruedUnpaid: 0m),
            DrawdownLots: [],
            CurrentRateReset: null,
            LastAccrualDate: lastAccrualDate,
            LastPaymentDate: null,
            ServicingRevision: 1,
            RevisionHistory: [],
            AccrualEntries: []);

    private static LedgerAccountingPeriod BuildPeriod(
        string status,
        Guid? periodId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null) =>
        new(
            PeriodId: periodId ?? Guid.Parse("33333333-3333-3333-3333-333333333333"),
            LedgerBookId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            FiscalYear: 2026,
            PeriodNo: 3,
            Label: "2026-P03",
            StartDate: startDate ?? new DateOnly(2026, 3, 1),
            EndDate: endDate ?? new DateOnly(2026, 3, 31),
            Status: status,
            OpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            ClosedAt: string.Equals(status, "Open", StringComparison.Ordinal)
                ? null
                : DateTimeOffset.Parse("2026-03-31T23:59:59Z"),
            Version: 1);

    private static LedgerJournalEntryRecord BuildJournalRecord(Guid loanId, Guid periodId)
    {
        var journalEntryId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var timestamp = DateTimeOffset.Parse("2026-03-22T00:00:00Z");
        const string description = "Loan drawdown";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Loans", LedgerAccountType.Asset),
                    100m,
                    0m,
                    description),
                new LedgerEntry(
                    Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Assets:Cash", LedgerAccountType.Asset),
                    0m,
                    100m,
                    description)
            ]);

        return new LedgerJournalEntryRecord(
            journal,
            AggregateId: loanId,
            PeriodId: periodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: 1,
            CreatedAt: timestamp);
    }

    private static DailyAccrualEntryDto BuildAccrual(DateOnly accrualDate) =>
        new(
            AccrualEntryId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            AccrualDate: accrualDate,
            InterestAmount: 12.50m,
            CommitmentFeeAmount: 3.25m,
            PenaltyAmount: 0m,
            AnnualRateApplied: 0.08m,
            RecordedAt: DateTimeOffset.Parse("2026-03-24T12:00:00Z"));
}
