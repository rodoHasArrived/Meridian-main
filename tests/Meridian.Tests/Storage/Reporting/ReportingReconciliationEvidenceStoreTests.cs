using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingReconciliationEvidenceStoreTests :
    IClassFixture<ReportingGovernanceDatabaseFixture>
{
    private readonly ReportingGovernanceDatabaseFixture _database;

    public ReportingReconciliationEvidenceStoreTests(ReportingGovernanceDatabaseFixture database)
    {
        _database = database;
    }

    [ReportingDatabaseFact]
    public async Task RetainAndRead_RoundTripsExactTextPayloadIdempotentlyAcrossStoreRestart()
    {
        var store = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var receipt = NewReceipt();

        var firstAlreadyExisted = await store.RetainAsync(receipt);
        var secondAlreadyExisted = await store.RetainAsync(receipt);
        var restarted = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var retained = await restarted.GetExactAsync(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash);

        firstAlreadyExisted.Should().BeFalse();
        secondAlreadyExisted.Should().BeTrue();
        retained.Should().BeEquivalentTo(receipt, options => options.WithStrictOrdering());
        ReportingReconciliationEvidenceValidation.Validate(retained!);
    }

    [ReportingDatabaseFact]
    public async Task HardCloseBridge_RetainsDurableReceipt_ThatRestartedCertificationConsumes()
    {
        const string tenantId = "tenant-close";
        const string companyId = "company-close";
        const string fundId = "fund-close";
        var fundAccountId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var completedAt = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);
        var softClosed = new LedgerPeriodDto(
            periodId,
            bookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            LedgerPeriodStatusDto.SoftClosed,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            6);
        var hardClosed = softClosed with
        {
            Status = LedgerPeriodStatusDto.HardClosed,
            ClosedAt = completedAt,
            Version = 7
        };
        var summary = new LedgerPeriodSummaryDto(
            periodId,
            bookId,
            2026,
            6,
            "2026-06",
            TrialBalance: [],
            TotalDebits: 0m,
            TotalCredits: 0m,
            NetIncome: 0m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            LedgerPeriodSignoffStatusDto.SignedOff,
            completedAt);
        var ledgerBookService = Substitute.For<ILedgerBookService>();
        ledgerBookService.GetBookAsync(bookId, Arg.Any<CancellationToken>())
            .Returns(new LedgerBookDto(
                bookId,
                fundId,
                fundAccountId,
                FundStructureNodeKindDto.Account,
                "Fund close primary ledger",
                "USD",
                completedAt,
                completedAt));
        var periodReadCount = 0;
        ledgerBookService.ListPeriodsAsync(
                Arg.Any<LedgerPeriodQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ++periodReadCount <= 3
                ? new[] { softClosed }
                : new[] { hardClosed });
        ledgerBookService.GetPeriodSummaryAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(summary);
        ledgerBookService.ClosePeriodAsync(
                periodId,
                Arg.Any<CloseLedgerPeriodRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new LedgerPeriodCloseResultDto(
                hardClosed,
                summary,
                new OperatorWorkItemDto(
                    $"period-close:{periodId:D}",
                    OperatorWorkItemKindDto.LedgerPeriodClose,
                    "Period hard closed",
                    "The reporting close completed.",
                    OperatorWorkItemToneDto.Success,
                    completedAt)));

        var workbench = Substitute.For<IManualJournalEntryWorkbenchService>();
        workbench.GetWorkbenchAsync(
                fundId,
                bookId,
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(new ManualJournalEntryWorkbenchDto(
                fundId,
                bookId,
                completedAt,
                [],
                [],
                [],
                []));
        var lifecycle = Substitute.For<IManualJournalEntryLifecycleService>();
        var intake = new AutomatedJournalDraftIntakeService(
            workbench,
            Substitute.For<IManualJournalEntryDraftStore>(),
            Substitute.For<IAccountingConfigurationService>());
        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: ledgerBookService);
        var authoritativeSource = new CloseAuthoritativeSource(
            tenantId,
            companyId,
            fundId,
            bookId,
            periodId,
            completedAt);
        var durableStore = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var failOnceStore = new FailOnceRetentionStore(durableStore);
        var retention = new ReportingReconciliationEvidenceRetentionService(
            failOnceStore,
            authoritativeSource);
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(fundId, tenantId, companyId));
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            workbench,
            lifecycle,
            ledgerBookService,
            retention,
            tenancy);

        var closeContext = new AccountingClosePostingContext(
                Guid.NewGuid(),
                fundAccountId,
                bookId,
                periodId.ToString("D"),
                "USD");
        var closeCommand = new AccountingClosePostingCommand(
                "fund-controller",
                "Finalize the exact retained close.",
                [$"evidence://period/{periodId:D}/hard-close"],
                OperationsActionOriginDto.HumanOperator,
                Role: "Fund Controller");

        Func<Task> firstAttempt = async () =>
            await bridge.FinalizeHardCloseAsync(closeContext, closeCommand);
        var pending = (await firstAttempt.Should()
            .ThrowAsync<ReportingCloseEvidenceHandoffException>()).Which;
        pending.HardClosedPeriod.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        pending.CompletionCheckpointId.Should().Be($"hard-close-{periodId:N}-v7");

        var result = await bridge.FinalizeHardCloseAsync(closeContext, closeCommand);

        result.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        await ledgerBookService.Received(1).ClosePeriodAsync(
            periodId,
            Arg.Any<CloseLedgerPeriodRequest>(),
            Arg.Any<CancellationToken>());
        var restartedStore = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var restartedEvidenceSource = new ReportingReconciliationEvidenceSource(restartedStore);
        var parameters = CloseAuthoritativeSource.Parameters(fundId, bookId, periodId);
        var readiness = new ReportingRunReadinessDto(
            "close-readiness",
            completedAt,
            new VersionedReportTemplateIdDto("close-report", 1),
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "close",
                    "Close",
                    ReportingRunReadinessStatusDto.Ready,
                    "The hard close is retained.",
                    0,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    EvidenceReferences: [$"ledger-period:{periodId:D}:hard-closed"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('a', 64));
        var certified = await new ReportingRunCertificationService(
                authoritativeSource,
                restartedEvidenceSource)
            .CertifyAsync(
                new ReportingTemplateMetadata(
                    "close-report",
                    ReportingTemplateFamily.CustomReport,
                    "Close report",
                    "1.0.0",
                    ["summary"],
                    ImmutableDictionary<string, string>.Empty,
                    AccessPolicy: new ReportAccessPolicyDto(
                        ReportAccessModeDto.CompanyWide,
                        CompanyId: companyId)),
                readiness,
                new ReportAccessQueryContext(
                    "report-reviewer",
                    CompanyId: companyId,
                    TenantId: tenantId,
                    RequireBoundScope: true));

        certified.Snapshot.ReconciliationCheckpointId.Should().NotBeNullOrWhiteSpace();
        certified.Snapshot.SourceCheckpointId.Should().Be(certified.AuthoritativeSource.CheckpointId);
        certified.Readiness.CanGenerateFinal.Should().BeTrue();
    }

    private static ReportingReconciliationEvidenceReceipt NewReceipt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var completedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "ledger-journal",
            $"ledger-source-{suffix}",
            $"tenant-{suffix}",
            $"organization-{suffix}",
            $"company-{suffix}",
            $"fund-{suffix}",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            "Gaap",
            new DateOnly(2026, 6, 30),
            completedAt,
            42,
            2,
            4,
            $"ledger-checkpoint-{suffix}",
            new string('a', 64),
            completedAt,
            ImmutableArray.Create($"ledger-sequence:{suffix}:42"));
        var completion = new ReportingReconciliationCompletionEvidence(
            $"hard-close-{suffix}",
            new string('b', 64),
            completedAt,
            HasOpenBreaks: false,
            ImmutableArray.Create($"period-close:{suffix}:hard-closed"));
        return ReportingReconciliationEvidenceValidation.CreateReceipt(source, completion);
    }

    private sealed class CloseAuthoritativeSource(
        string tenantId,
        string companyId,
        string fundId,
        Guid bookId,
        Guid periodId,
        DateTimeOffset capturedAt) : IReportingAuthoritativeSource
    {
        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            var hash = new string('d', 64);
            var checkpointId = $"ledger-checkpoint-{bookId:N}-{periodId:N}";
            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["debit"] = "0",
                    ["credit"] = "0",
                    ["netAmount"] = "0"
                });
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{bookId:D}:{periodId:D}",
                tenantId,
                "organization-close",
                companyId,
                fundId,
                bookId.ToString("D"),
                periodId.ToString("D"),
                "Primary",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                17,
                1,
                1,
                checkpointId,
                hash,
                capturedAt,
                [$"reporting-source-checkpoint:{checkpointId}:{hash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, rows));
        }

        public static ReportingRunParametersDto Parameters(
            string fundId,
            Guid bookId,
            Guid periodId) =>
            new(
                new ReportingRunScopeDto(fundId),
                periodId.ToString("D"),
                new DateOnly(2026, 6, 30),
                new ReportingLedgerBookSelectionDto(bookId),
                ReportingAccountingBasisDto.Management,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true);
    }

    private sealed class FailOnceRetentionStore(
        IReportingReconciliationEvidenceRetentionStore inner) :
        IReportingReconciliationEvidenceRetentionStore
    {
        private int _retainAttempts;

        public ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
            string tenantId,
            string organizationId,
            string? companyId,
            string fundId,
            string ledgerBookId,
            string accountingPeriodId,
            string accountingBasis,
            DateOnly asOfDate,
            string sourceCheckpointId,
            string sourceCheckpointHash,
            CancellationToken cancellationToken = default) =>
            inner.GetExactAsync(
                tenantId,
                organizationId,
                companyId,
                fundId,
                ledgerBookId,
                accountingPeriodId,
                accountingBasis,
                asOfDate,
                sourceCheckpointId,
                sourceCheckpointHash,
                cancellationToken);

        public ValueTask<bool> RetainAsync(
            ReportingReconciliationEvidenceReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _retainAttempts) == 1)
            {
                return ValueTask.FromException<bool>(
                    new IOException("Simulated post-commit reporting evidence outage."));
            }

            return inner.RetainAsync(receipt, cancellationToken);
        }
    }
}
