using System.Text.Json;
using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Moq;

namespace Meridian.Tests.Application;

[Trait("Category", "Integration")]
public sealed class OperationsContinuityPostgresRoundTripTests
{
    private static readonly Guid SecurityId = Guid.Parse("D3B32FA8-A6FD-4571-ACDA-56D5D6F6C92C");

    [LedgerDatabaseFact]
    public async Task PostgresOperationsContinuityStore_ShouldRejectBookScopedWorkflowWhenTenantCannotBeResolved()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05-invalid-book",
            securityMasterSnapshotId: Guid.NewGuid(),
            brokerSource: "postgres-fixture",
            now: DateTimeOffset.UtcNow,
            ledgerBookId: Guid.NewGuid());

        var act = () => database.OperationsStore.SaveAsync(workflow);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not resolve to a claimed fund tenant*");
    }

    [LedgerDatabaseFact]
    public async Task PostgresOperationsContinuityStore_ShouldRejectUnresolvedBookWhenStartingWorkflowTransactionally()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-05-invalid-book-transactional-start",
            securityMasterSnapshotId: Guid.NewGuid(),
            brokerSource: "postgres-fixture",
            now: DateTimeOffset.UtcNow,
            ledgerBookId: Guid.NewGuid());
        var auditDraft = new OperationsWorkflowAuditDraft(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            "workflow-started",
            OperationsWorkflowStatusDto.NotStarted,
            OperationsWorkflowStatusDto.NotStarted,
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.NotStarted,
            OperationsGateStatusDto.InProgress,
            "ops-user",
            "Reject an unresolved book before committing the workflow start.",
            "postgres-unresolved-book-transactional-start",
            EvidenceLinks());

        var act = () => database.OperationsStore.CommitWorkflowStartAsync(workflow, auditDraft);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not resolve to a claimed fund tenant*");

        (await database.OperationsStore.GetAsync(workflow.WorkflowId)).Should().BeNull();
        (await database.OperationsStore.GetTimelineAsync(workflow.WorkflowId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task PostgresOperationsContinuityStore_ShouldRejectUnmarkedFixtureWithoutCommittingJournal()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var service = CreateService(database);
        var context = await CreateLedgerContextAsync(database, "unmarked-fixture", "Open");
        var fundAccountId = Guid.NewGuid();
        var workflow = await CreateLedgerValidatedWorkflowAsync(
            service, fundAccountId, context.Period.PeriodId.ToString("D"), context.Book.LedgerBookId);
        var candidate = CreateJournalCandidate(
            context.Period.PeriodId, fundAccountId, context.Book.LedgerBookId,
            context.Period.Version, "unmarked-posting") with
        { Provenance = DataProvenance.Real };

        var result = await service.PostLedgerEntriesAsync(
            workflow.WorkflowId,
            new OperationsLedgerPostRequestDto(
                workflow.Version, "ops-user",
                LedgerBatchId: "unmarked-batch", PostingKind: "period-close", PeriodOpen: true,
                Rationale: "Verify that fixture evidence cannot enter the journal as real data.",
                EvidenceLinks: EvidenceLinks(), JournalCandidate: candidate));

        result.Success.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == "LEDGER_JOURNAL_APPEND_REJECTED");
        (await database.JournalStore.GetByPeriodAsync(context.Period.PeriodId)).Should().BeEmpty();
        (await database.OperationsStore.GetTimelineAsync(workflow.WorkflowId))
            .Should().NotContain(entry => entry.EventType == "ledger-posted");
    }

    [LedgerDatabaseFact]
    public async Task PostgresOperationsContinuityStore_ShouldRoundTripTransactionalLedgerPosting()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var service = CreateService(database);
        var tableNames = await database.GetTableNamesAsync();
        tableNames.Should().Contain(
        [
            "accounting_periods",
            "journal_entries",
            "journal_legs",
            "operations_continuity_workflows",
            "operations_continuity_audit"
        ]);

        var openContext = await CreateLedgerContextAsync(database, "posted", "Open");
        var openPeriod = openContext.Period;
        var fundAccountId = Guid.NewGuid();
        var workflow = await CreateLedgerValidatedWorkflowAsync(
            service,
            fundAccountId,
            openPeriod.PeriodId.ToString("D"),
            openContext.Book.LedgerBookId);
        var journalCandidate = CreateJournalCandidate(
            openPeriod.PeriodId,
            fundAccountId,
            openContext.Book.LedgerBookId,
            openPeriod.Version,
            "postgres-fixture-posting");

        var posted = await service.PostLedgerEntriesAsync(
            workflow.WorkflowId,
            new OperationsLedgerPostRequestDto(
                workflow.Version,
                "ops-user",
                LedgerBatchId: "ledger-batch-postgres-roundtrip",
                PostingKind: "period-close",
                PeriodOpen: true,
                Rationale: "Post validated journal candidate through the PostgreSQL transaction seam.",
                CorrelationId: "postgres-roundtrip-success",
                EvidenceLinks: EvidenceLinks(),
                JournalCandidate: journalCandidate));

        posted.Success.Should().BeTrue(DescribeFailure(posted));
        posted.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);
        posted.Workflow.LedgerBookId.Should().Be(openContext.Book.LedgerBookId);
        posted.Workflow.Gates
            .Single(static gate => gate.GateKey == OperationsGateKeyDto.LedgerPosting)
            .Status.Should().Be(OperationsGateStatusDto.Passed);

        var loadedWorkflow = await database.OperationsStore.GetAsync(workflow.WorkflowId);
        loadedWorkflow.Should().NotBeNull();
        loadedWorkflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);
        loadedWorkflow.LedgerPostingGate.Status.Should().Be(OperationsGateStatusDto.Passed);
        loadedWorkflow.Version.Should().BeGreaterThan(workflow.Version);

        var timeline = await database.OperationsStore.GetTimelineAsync(workflow.WorkflowId);
        timeline.Select(static entry => entry.EventType).Should().Contain("ledger-posted");
        AssertSingleHashChain(timeline);

        var journalEntries = await database.JournalStore.GetByPeriodAsync(openPeriod.PeriodId);
        journalEntries.Should().ContainSingle();
        var journalEntry = journalEntries.Single();
        journalEntry.Entry.JournalEntryId.Should().Be(journalCandidate.JournalEntryId!.Value);
        journalEntry.AggregateId.Should().Be(fundAccountId);
        journalEntry.Entry.IsBalanced.Should().BeTrue();
        journalEntry.Entry.Metadata.LedgerBook.Should().Be(openContext.Book.LedgerBookId.ToString("D"));
        journalEntry.Entry.Metadata.Tags.Should().ContainKey("securityMasterLineage");
        journalEntry.Entry.Metadata.Tags["dataProvenance"].Should().Be("SEEDED");
        journalEntry.Entry.Metadata.ActivityType.Should().Be("interest");
        journalEntry.Entry.Metadata.IdempotencyKey.Should().Be(journalCandidate.IdempotencyKey);
        journalEntry.Entry.Metadata.FundEventId.Should().BeNull();
        journalEntry.Entry.Metadata.FundEventType.Should().BeNull(
            "a generic source event type must not be reclassified as private-capital fund-event metadata");

        var rejectedContext = await CreateLedgerContextAsync(database, "rejected", "HardClosed");
        var hardClosedPeriod = rejectedContext.Period;
        var rejectedWorkflow = await CreateLedgerValidatedWorkflowAsync(
            service,
            Guid.NewGuid(),
            hardClosedPeriod.PeriodId.ToString("D"),
            rejectedContext.Book.LedgerBookId);
        var rejectedVersion = rejectedWorkflow.Version;

        var rejected = await service.PostLedgerEntriesAsync(
            rejectedWorkflow.WorkflowId,
            new OperationsLedgerPostRequestDto(
                rejectedWorkflow.Version,
                "ops-user",
                LedgerBatchId: "ledger-batch-postgres-rejected",
                PostingKind: "period-close",
                PeriodOpen: true,
                Rationale: "Exercise transaction rollback on storage-side period guard failure.",
                CorrelationId: "postgres-roundtrip-rejected",
                EvidenceLinks: EvidenceLinks(),
                JournalCandidate: CreateJournalCandidate(
                    hardClosedPeriod.PeriodId,
                    rejectedWorkflow.FundAccountId,
                    rejectedContext.Book.LedgerBookId,
                    hardClosedPeriod.Version,
                    "postgres-fixture-rejected")));

        rejected.Success.Should().BeFalse();
        rejected.ErrorCode.Should().Be("LEDGER_JOURNAL_APPEND_REJECTED");

        var persistedRejectedWorkflow = await database.OperationsStore.GetAsync(rejectedWorkflow.WorkflowId);
        persistedRejectedWorkflow.Should().NotBeNull();
        persistedRejectedWorkflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Validated);
        persistedRejectedWorkflow.LedgerPostingGate.Status.Should().Be(OperationsGateStatusDto.InProgress);
        persistedRejectedWorkflow.Version.Should().Be(rejectedVersion);

        var rejectedTimeline = await database.OperationsStore.GetTimelineAsync(rejectedWorkflow.WorkflowId);
        rejectedTimeline.Select(static entry => entry.EventType).Should().NotContain("ledger-posted");
        AssertSingleHashChain(rejectedTimeline);

        var rejectedJournalEntries = await database.JournalStore.GetByPeriodAsync(hardClosedPeriod.PeriodId);
        rejectedJournalEntries.Should().BeEmpty();
    }

    private static OperationsContinuityWorkflowService CreateService(LedgerPostgresTestDatabase database)
    {
        var securityMaster = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        var emptyObject = JsonSerializer.SerializeToElement(new { });
        securityMaster
            .Setup(service => service.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityDetailDto(
                SecurityId,
                "Equity",
                SecurityStatusDto.Active,
                "PostgreSQL fixture security",
                "USD",
                emptyObject,
                emptyObject,
                [],
                [],
                1,
                DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                null));

        return new OperationsContinuityWorkflowService(
            database.OperationsStore,
            database.OperationsStore,
            database.StatusDerivation,
            ledgerJournalStore: null,
            transactionalCommitStore: database.OperationsStore,
            securityMasterQueryService: securityMaster.Object);
    }

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerValidatedWorkflowAsync(
        OperationsContinuityWorkflowService service,
        Guid fundAccountId,
        string periodId,
        Guid ledgerBookId)
    {
        var start = RequireSuccess(await service.StartWorkflowAsync(
            new OperationsStartWorkflowRequestDto(
                fundAccountId,
                periodId,
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "postgres-fixture",
                Actor: "ops-user",
                Rationale: "Start PostgreSQL round-trip workflow.",
                CorrelationId: $"start-{periodId}",
                EvidenceLinks: EvidenceLinks(),
                LedgerBookId: ledgerBookId)));

        var imported = RequireSuccess(await service.ImportBrokerDataAsync(
            start.WorkflowId,
            TransitionRequest(start.Version, "import broker activity")));

        var normalized = RequireSuccess(await service.NormalizeBrokerTransactionsAsync(
            imported.WorkflowId,
            TransitionRequest(imported.Version, "normalize broker activity")));

        var resolved = RequireSuccess(await service.ResolveSecurityMasterMappingsAsync(
            normalized.WorkflowId,
            new OperationsSecurityMasterResolveRequestDto(
                normalized.Version,
                "ops-user",
                Rationale: "Resolve all instruments for posting.",
                CorrelationId: $"security-master-{periodId}",
                UnresolvedInstrumentCount: 0,
                OverrideRequestCount: 0,
                OverridesApproved: true,
                MissingAccountingTermCount: 0,
                EvidenceLinks: EvidenceLinks())));

        var drafted = RequireSuccess(await service.BuildLedgerDraftAsync(
            resolved.WorkflowId,
            new OperationsLedgerDraftRequestDto(
                resolved.Version,
                "ops-user",
                PreviewId: $"preview-{periodId}",
                IsBalanced: true,
                Rationale: "Build balanced ledger preview.",
                CorrelationId: $"ledger-draft-{periodId}",
                HasSecurityMasterProvenance: true,
                HasIdempotencyKey: true,
                LedgerBatchId: null,
                EvidenceLinks: EvidenceLinks())));

        return RequireSuccess(await service.ValidateLedgerDraftAsync(
            drafted.WorkflowId,
            new OperationsLedgerValidationRequestDto(
                drafted.Version,
                "ops-user",
                IsBalanced: true,
                PeriodOpen: true,
                HasDuplicatePostingCandidate: false,
                Rationale: "Validate balanced journal preview.",
                CorrelationId: $"ledger-validation-{periodId}",
                EvidenceLinks: EvidenceLinks())));
    }

    private static OperationsContinuityWorkflowDto RequireSuccess(OperationsTransitionResultDto result)
    {
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Workflow.Should().NotBeNull();
        return result.Workflow!;
    }

    private static OperationsTransitionRequestDto TransitionRequest(long version, string rationale)
        => new(
            version,
            "ops-user",
            Rationale: rationale,
            CorrelationId: $"postgres-roundtrip-{Guid.NewGuid():N}",
            EvidenceLinks: EvidenceLinks());

    private static OperationsLedgerJournalCandidateDto CreateJournalCandidate(
        Guid periodId,
        Guid aggregateId,
        Guid ledgerBookId,
        long expectedLedgerVersion,
        string description)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-15T16:00:00Z");
        var idempotencyKey = $"{SecurityId:N}:postgres:20260515:AccrueInterestIncome:test-source-hash";
        var provenance = $"security-master:{SecurityId:N};snapshot:test-source-hash;approved:true;status:active";

        return new OperationsLedgerJournalCandidateDto(
            journalEntryId,
            aggregateId,
            periodId,
            timestamp,
            description,
            Lines:
            [
                new OperationsLedgerJournalLineDto(
                    Guid.NewGuid(),
                    "Cash",
                    LedgerAccountType.Asset.ToString(),
                    Debit: 125m,
                    Credit: 0m),
                new OperationsLedgerJournalLineDto(
                    Guid.NewGuid(),
                    "Interest Income",
                    LedgerAccountType.Revenue.ToString(),
                    Debit: 0m,
                    Credit: 125m,
                    Symbol: "POSTGRES",
                    SecurityId: SecurityId,
                    SecurityMasterApproved: true,
                    SecurityMasterProvenance: provenance,
                    LedgerMappingReference: $"ledger-map:postgres-interest-income:{SecurityId:N}",
                    SecurityMasterApprovalReference: "sm-approval:postgres-controller",
                    SecurityMasterStatus: SecurityStatusDto.Active)
            ],
            CommandId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-postgres-fixture",
            RuleVersion: "v1",
            SourceEventId: Guid.NewGuid(),
            SourceJournalEntryId: null,
            PostingKind: LedgerPostingKindDto.Originating,
            AdjustmentApproval: null,
            Metadata: new OperationsJournalEntryMetadataDto(
                ActivityType: "interest",
                Symbol: "POSTGRES",
                SecurityId: SecurityId,
                LedgerBook: ledgerBookId.ToString("D"),
                Tags: new Dictionary<string, string>
                {
                    ["fixture"] = "operations-continuity-postgres"
                }),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: provenance,
            ExpectedLedgerVersion: expectedLedgerVersion)
        {
            Provenance = DataProvenance.Seeded
        };
    }

    private static async Task<(LedgerBookRecord Book, LedgerAccountingPeriod Period)> CreateLedgerContextAsync(
        LedgerPostgresTestDatabase database,
        string scenario,
        string periodStatus)
    {
        var fundProfileId = $"fund-operations-continuity-{scenario}";
        var recordedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var tenancy = new PostgresFundProfileTenancyRegistry(database.Options);
        await tenancy.BindAsync(
            fundProfileId,
            "tenant-operations-continuity",
            "company-operations-continuity");

        var book = await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            Guid.NewGuid(),
            fundProfileId,
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            $"Operations Continuity {scenario} book",
            "USD",
            recordedAt,
            recordedAt));

        var period = await database.JournalStore.SavePeriodAsync(
            new LedgerAccountingPeriod(
                Guid.NewGuid(),
                book.LedgerBookId,
                2026,
                5,
                $"2026-05-{scenario}",
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                periodStatus,
                recordedAt,
                string.Equals(periodStatus, "Open", StringComparison.Ordinal)
                    ? null
                    : DateTimeOffset.Parse("2026-05-31T23:59:59Z"),
                Version: 0),
            expectedVersion: 0);

        return (book, period);
    }

    private static string DescribeFailure(OperationsTransitionResultDto result)
        => string.Join(
            "; ",
            new[] { result.ErrorMessage }
                .Concat(result.Blockers.Select(static blocker => $"{blocker.Code}: {blocker.Message}"))
                .Where(static message => !string.IsNullOrWhiteSpace(message)));

    private static IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks() =>
    [
        new OperationsEvidenceLinkDto(
            "postgres-roundtrip-fixture",
            "PostgreSQL round-trip fixture",
            "fixture://operations-continuity/postgres-roundtrip",
            "test-fixture",
            DateTimeOffset.Parse("2026-05-15T16:00:00Z"))
    ];

    private static void AssertSingleHashChain(IReadOnlyList<OperationsWorkflowAuditDto> timeline)
    {
        timeline.Should().NotBeEmpty();
        timeline.Select(static entry => entry.CurrentHash).Should().OnlyHaveUniqueItems();

        var genesis = timeline.Should()
            .ContainSingle(static entry => entry.PreviousHash == null)
            .Which;
        var byPreviousHash = timeline
            .Where(static entry => entry.PreviousHash is not null)
            .ToDictionary(static entry => entry.PreviousHash!, StringComparer.Ordinal);

        var chain = new List<OperationsWorkflowAuditDto> { genesis };
        while (byPreviousHash.Remove(chain[^1].CurrentHash, out var next))
        {
            chain.Add(next);
        }

        chain.Should().HaveCount(timeline.Count);
        byPreviousHash.Should().BeEmpty();
    }

}
