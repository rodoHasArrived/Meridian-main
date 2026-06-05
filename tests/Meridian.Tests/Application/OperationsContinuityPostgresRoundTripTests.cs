using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Tests.Storage;

namespace Meridian.Tests.Application;

[Trait("Category", "Integration")]
public sealed class OperationsContinuityPostgresRoundTripTests
{
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

        var openPeriod = await database.SavePeriodAsync(Guid.NewGuid(), "Open");
        var fundAccountId = Guid.NewGuid();
        var workflow = await CreateLedgerValidatedWorkflowAsync(
            service,
            fundAccountId,
            "2026-05-postgres-happy");
        var journalCandidate = CreateJournalCandidate(
            openPeriod.PeriodId,
            fundAccountId,
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

        posted.Success.Should().BeTrue(posted.ErrorMessage);
        posted.Workflow!.LedgerPostingState.Should().Be(OperationsLedgerPostingStateDto.Complete);
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

        var hardClosedPeriod = await database.SavePeriodAsync(Guid.NewGuid(), "HardClosed");
        var rejectedWorkflow = await CreateLedgerValidatedWorkflowAsync(
            service,
            Guid.NewGuid(),
            "2026-05-postgres-rejected");
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
        => new(
            database.OperationsStore,
            database.OperationsStore,
            database.StatusDerivation,
            ledgerJournalStore: null,
            transactionalCommitStore: database.OperationsStore);

    private static async Task<OperationsContinuityWorkflowDto> CreateLedgerValidatedWorkflowAsync(
        OperationsContinuityWorkflowService service,
        Guid fundAccountId,
        string periodId)
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
                EvidenceLinks: EvidenceLinks())));

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
        string description)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-15T16:00:00Z");
        var securityId = Guid.Parse("D3B32FA8-A6FD-4571-ACDA-56D5D6F6C92C");
        var idempotencyKey = $"{securityId:N}:postgres:20260515:AccrueInterestIncome:test-source-hash";
        var provenance = $"security-master:{securityId:N};snapshot:test-source-hash;approved:true;status:active";

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
                    Credit: 0m,
                    Symbol: "POSTGRES",
                    SecurityId: securityId,
                    SecurityMasterApproved: true,
                    SecurityMasterProvenance: provenance,
                    LedgerMappingReference: $"ledger-map:POSTGRES:{securityId:N}",
                    SecurityMasterApprovalReference: "sm-approval:postgres-controller",
                    SecurityMasterStatus: SecurityStatusDto.Active),
                new OperationsLedgerJournalLineDto(
                    Guid.NewGuid(),
                    "Interest Income",
                    LedgerAccountType.Revenue.ToString(),
                    Debit: 0m,
                    Credit: 125m,
                    Symbol: "POSTGRES",
                    SecurityId: securityId,
                    SecurityMasterApproved: true,
                    SecurityMasterProvenance: provenance,
                    LedgerMappingReference: $"ledger-map:POSTGRES:{securityId:N}",
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
                SecurityId: securityId,
                LedgerBook: "legacy",
                Tags: new Dictionary<string, string>
                {
                    ["fixture"] = "operations-continuity-postgres"
                }),
            IdempotencyKey: idempotencyKey,
            SecurityMasterProvenance: provenance);
    }

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
