using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using System.Text.Json;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Guards statement-onboarding recovery when the process stops between import, match, break, case,
/// and completion checkpoints, including duplicate concurrent intake of the same retained file.
/// </summary>
public sealed class StatementRunRecoveryTests : IDisposable
{
    private const string Header =
        "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-run-recovery-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(StatementRunWorkflowFaultPoint.ImportArtifactVerified)]
    [InlineData(StatementRunWorkflowFaultPoint.ImportedCheckpointRetained)]
    [InlineData(StatementRunWorkflowFaultPoint.MatchArtifactVerified)]
    [InlineData(StatementRunWorkflowFaultPoint.MatchedCheckpointRetained)]
    [InlineData(StatementRunWorkflowFaultPoint.BreakProjectionVerified)]
    [InlineData(StatementRunWorkflowFaultPoint.BreaksCheckpointRetained)]
    [InlineData(StatementRunWorkflowFaultPoint.CaseProjectionVerified)]
    [InlineData(StatementRunWorkflowFaultPoint.CasesCheckpointRetained)]
    [InlineData(StatementRunWorkflowFaultPoint.CompletedCheckpointRetained)]
    public async Task Scenario_ProcessStopsAtEveryStatementStage_ExactReplayConvergesWithoutDuplicateEvidence(
        StatementRunWorkflowFaultPoint faultPoint)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("recover.csv");
        var request = Request(path);
        var first = CreateWorkflow(new ThrowOnceStatementRunFaultInjector(faultPoint));

        var act = async () => await first.CreateAsync(request, timeout.Token);

        await act.Should().ThrowAsync<InjectedStatementRunFaultException>();
        var runId = await ResolveImportIdAsync(timeout.Token);
        var interrupted = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(runId, timeout.Token);
        if (interrupted is not null && faultPoint != StatementRunWorkflowFaultPoint.CompletedCheckpointRetained)
        {
            interrupted.Status.Should().NotBe(StatementRunRecoveryStatus.Completed,
                "a later-stage failure cannot promote an incomplete run to completed");
        }

        var recovered = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var checkpoint = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(recovered.Import.ImportId, timeout.Token);

        checkpoint.Should().NotBeNull();
        checkpoint!.SchemaVersion.Should().Be(StatementRunRecoveryCheckpoint.CurrentSchemaVersion);
        checkpoint.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
        checkpoint.ImportArtifact.Count.Should().Be(1);
        checkpoint.MatchArtifact.Should().NotBeNull();
        checkpoint.BreakArtifact.Should().NotBeNull();
        checkpoint.BreakArtifact!.Count.Should().Be(recovered.Breaks.Count);
        checkpoint.CaseArtifact.Should().NotBeNull();
        checkpoint.CaseArtifact!.Count.Should().Be(recovered.Cases.Count);
        recovered.Breaks.Should().ContainSingle();
        var recoveredCase = recovered.Cases.Should().ContainSingle().Subject;
        recoveredCase.CommentThreads.SelectMany(static thread => thread.Comments)
            .Should().ContainSingle();
        recoveredCase.AuditEvents.Should().ContainSingle();

        var retainedCase = await new JsonReconciliationCaseStore(_root)
            .GetAsync(recoveredCase.CaseId, timeout.Token);
        retainedCase.Should().BeEquivalentTo(recoveredCase);
        Directory.EnumerateFiles(
                Path.Combine(_root, "reconciliation", "cases"),
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Should().ContainSingle("replay must not mint duplicate cases");
    }

    [Fact]
    public async Task Scenario_ConcurrentDuplicateStatementIntake_OneImportAndOneEvidenceSetConverge()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("concurrent.csv");
        var request = Request(path);
        var first = CreateWorkflow();
        var second = CreateWorkflow();

        var results = await Task.WhenAll(
            first.CreateAsync(request, timeout.Token),
            second.CreateAsync(request, timeout.Token));

        results[0].Should().BeEquivalentTo(results[1]);
        (await new JsonCanonicalStatementStore(_root).ListImportsAsync(timeout.Token))
            .Should().ContainSingle();
        Directory.EnumerateFiles(
                Path.Combine(_root, "reconciliation", "statement-breaks"),
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Should().ContainSingle();
        Directory.EnumerateFiles(
                Path.Combine(_root, "reconciliation", "cases"),
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Scenario_SameImportWithDifferentRequestFingerprint_ResumeFailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("conflict.csv");
        var loose = new StatementToleranceProfile(
            "loose",
            1,
            [new CashToleranceRule("cash", 1m, null, TimeSpan.FromDays(5))],
            [new PositionToleranceRule("position", 1m, 1m, 1m)],
            [new TransactionToleranceRule("transaction", 1m, TimeSpan.FromDays(5), 1m)]);
        var profiles = new InMemoryStatementToleranceProfileProvider(
            [StatementToleranceProfile.Default, loose]);
        var workflow = CreateWorkflow(toleranceProfiles: profiles);
        await workflow.CreateAsync(Request(path), timeout.Token);

        var act = async () => await CreateWorkflow(toleranceProfiles: profiles)
            .CreateAsync(Request(path) with { ToleranceProfileId = "loose" }, timeout.Token);

        await act.Should().ThrowAsync<StatementRunRecoveryConflictException>();
        var runId = await ResolveImportIdAsync(timeout.Token);
        var checkpoint = await new FileStatementRunRecoveryRepository(_root).GetAsync(runId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
    }

    [Fact]
    public async Task Scenario_ConflictingLegacyBreakProjection_ResumeRejectsRatherThanAdopts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("legacy-conflict.csv");
        var request = Request(path);
        var first = CreateWorkflow(
            new ThrowOnceStatementRunFaultInjector(StatementRunWorkflowFaultPoint.MatchedCheckpointRetained));
        var firstAct = async () => await first.CreateAsync(request, timeout.Token);
        await firstAct.Should().ThrowAsync<InjectedStatementRunFaultException>();
        var runId = await ResolveImportIdAsync(timeout.Token);
        var artifact = await new FileStatementRunMatchArtifactStore(_root).GetAsync(runId, timeout.Token);
        var expectedBreak = artifact!.Breaks.Should().ContainSingle().Subject;
        await new JsonReconciliationBreakStore(_root)
            .WriteAsync([expectedBreak with { Status = "FabricatedLegacyStatus" }], timeout.Token);

        var act = async () => await CreateWorkflow().CreateAsync(request, timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var checkpoint = await new FileStatementRunRecoveryRepository(_root).GetAsync(runId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Matched);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Failed);
    }

    [Fact]
    public void Scenario_OldConstructorWithoutRecoveryAuthority_FailsBeforeImportCanBePersisted()
    {
        var imports = new JsonCanonicalStatementStore(_root);

        Action construct = () => _ = new StatementRunWorkflowService(
            imports,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));

        construct.Should().Throw<ArgumentNullException>()
            .WithParameterName("recoveryRepository");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-imports"))
            .Should().BeFalse("construction must fail before an import can be accepted without recovery authority");
    }

    [Fact]
    public void Scenario_ConstructorWithoutMatchArtifactAuthority_FailsBeforeImportCanBePersisted()
    {
        var imports = new JsonCanonicalStatementStore(_root);

        Action construct = () => _ = new StatementRunWorkflowService(
            imports,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            recoveryRepository: new FileStatementRunRecoveryRepository(_root));

        construct.Should().Throw<ArgumentNullException>()
            .WithParameterName("matchArtifactStore");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-imports"))
            .Should().BeFalse("construction must fail before an import can be accepted without immutable match authority");
    }

    [Fact]
    public void Scenario_ConstructorWithoutCaseworkCommitAuthority_FailsBeforeImportCanBePersisted()
    {
        var imports = new JsonCanonicalStatementStore(_root);

        Action construct = () => _ = new StatementRunWorkflowService(
            imports,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            recoveryRepository: new FileStatementRunRecoveryRepository(_root),
            matchArtifactStore: new FileStatementRunMatchArtifactStore(_root));

        construct.Should().Throw<ArgumentNullException>()
            .WithParameterName("caseworkCommitStore");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-imports"))
            .Should().BeFalse("construction must fail before an import can be accepted without source-commit authority");
    }

    [Theory]
    [InlineData(StatementRunProjectionTarget.Break)]
    [InlineData(StatementRunProjectionTarget.BreakAudit)]
    [InlineData(StatementRunProjectionTarget.Case)]
    [InlineData(StatementRunProjectionTarget.CaseAudit)]
    public async Task Scenario_CompletedCheckpointProjectionDeleted_ReplayRepairsFromImmutableArtifactWithoutStageRegression(
        StatementRunProjectionTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync($"delete-{target}.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var checkpointBefore = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        var projectionPath = ProjectionPath(target, completed);
        File.Delete(projectionPath);

        var replay = await CreateWorkflow().CreateAsync(request, timeout.Token);

        File.Exists(projectionPath).Should().BeTrue();
        var checkpointAfter = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        checkpointAfter.Should().BeEquivalentTo(checkpointBefore,
            "a completed checkpoint is immutable while missing projections are repaired idempotently");
        await AssertLiveProjectionsMatchAsync(replay, timeout.Token);
    }

    [Theory]
    [InlineData(StatementRunProjectionTarget.Break)]
    [InlineData(StatementRunProjectionTarget.BreakAudit)]
    [InlineData(StatementRunProjectionTarget.Case)]
    [InlineData(StatementRunProjectionTarget.CaseAudit)]
    public async Task Scenario_CompletedCheckpointProjectionTruncated_ReplayFailsClosedAndPreservesCompletedStage(
        StatementRunProjectionTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync($"truncate-{target}.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        await File.WriteAllTextAsync(ProjectionPath(target, completed), "{", timeout.Token);

        var replay = async () => await CreateWorkflow().CreateAsync(request, timeout.Token);

        await replay.Should().ThrowAsync<JsonException>();
        var checkpoint = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
    }

    [Theory]
    [InlineData(StatementRunProjectionTarget.Break)]
    [InlineData(StatementRunProjectionTarget.BreakAudit)]
    [InlineData(StatementRunProjectionTarget.Case)]
    [InlineData(StatementRunProjectionTarget.CaseAudit)]
    public async Task Scenario_CompletedCheckpointProjectionContentChanged_ReplayRejectsConflictAndPreservesCompletedStage(
        StatementRunProjectionTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync($"corrupt-{target}.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var projectionPath = ProjectionPath(target, completed);
        var content = await File.ReadAllTextAsync(projectionPath, timeout.Token);
        var corrupted = target is StatementRunProjectionTarget.BreakAudit or StatementRunProjectionTarget.CaseAudit
            ? CorruptAuditHash(content)
            : CorruptProjectionStatus(content);
        corrupted.Should().NotBe(content);
        await File.WriteAllTextAsync(projectionPath, corrupted, timeout.Token);

        var replay = async () => await CreateWorkflow().CreateAsync(request, timeout.Token);

        await replay.Should().ThrowAsync<InvalidOperationException>();
        var checkpoint = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
    }

    [Fact]
    public async Task Scenario_CompletedCheckpointWithLaterCasework_ReplayAdoptsLatestImmutableSourceCommitWithoutRegression()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("completed-with-casework.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var checkpointBefore = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        var sourceCommit = await RetainResolvedSourceCommitAsync(completed, timeout.Token);

        var replay = await CreateWorkflow().CreateAsync(request, timeout.Token);

        replay.Breaks.Should().ContainSingle().Which.Status.Should().Be("Resolved");
        var replayedCase = replay.Cases.Should().ContainSingle().Subject;
        replayedCase.Status.Should().Be("Resolved");
        replayedCase.AuditEvents.Should().Contain(audit =>
            audit.EventId == sourceCommit.CaseAudit!.EventId);
        (await new JsonReconciliationBreakStore(_root).GetCaseworkAuditAsync(
            sourceCommit.NextBreak.BreakId,
            sourceCommit.CommandId,
            timeout.Token))!.PreviousStatus.Should().Be("Open");
        (await new JsonReconciliationCaseStore(_root).GetCaseworkAuditAsync(
            sourceCommit.NextCase!.CaseId,
            sourceCommit.CommandId,
            timeout.Token)).Should().BeEquivalentTo(sourceCommit.CaseAudit);
        var checkpointAfter = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        checkpointAfter.Should().BeEquivalentTo(checkpointBefore,
            "later casework advances live source projections without rewriting completed run authority");
    }

    [Theory]
    [InlineData(StatementRunProjectionTarget.Break)]
    [InlineData(StatementRunProjectionTarget.Case)]
    public async Task Scenario_CompletedCheckpointWithLaterCasework_LiveProjectionOutsideSourceCommitChainIsRejected(
        StatementRunProjectionTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("completed-with-casework-drift.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var sourceCommit = await RetainResolvedSourceCommitAsync(completed, timeout.Token);
        if (target == StatementRunProjectionTarget.Break)
        {
            await new JsonReconciliationBreakStore(_root).WriteAsync(
                [sourceCommit.NextBreak with { Status = "Tampered" }],
                timeout.Token);
        }
        else
        {
            await new JsonReconciliationCaseStore(_root).SaveAsync(
                sourceCommit.NextCase! with { Status = "Tampered" },
                timeout.Token);
        }

        var replay = async () => await CreateWorkflow().CreateAsync(request, timeout.Token);

        await replay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside*authority chain*");
        var checkpoint = await new FileStatementRunRecoveryRepository(_root)
            .GetAsync(completed.Import.ImportId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
    }

    [Fact]
    public async Task Scenario_CompletedCheckpointWithPreparedSourceCommit_ReplayRepairsSourceProjectionsWithoutForgingCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("completed-with-prepared-casework.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var sourceCommit = await RetainResolvedSourceCommitAsync(
            completed,
            timeout.Token,
            materialize: false,
            complete: false);

        var replay = await CreateWorkflow().CreateAsync(request, timeout.Token);

        replay.Breaks.Should().ContainSingle().Which.Status.Should().Be("Resolved");
        replay.Cases.Should().ContainSingle().Which.Status.Should().Be("Resolved");
        var commitStore = new FileStatementCaseworkCommitStore(_root);
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        (await breakStore.GetCaseworkAuditAsync(
            sourceCommit.NextBreak.BreakId,
            sourceCommit.CommandId,
            timeout.Token)).Should().BeEquivalentTo(sourceCommit.BreakAudit);
        (await caseStore.GetCaseworkAuditAsync(
            sourceCommit.NextCase!.CaseId,
            sourceCommit.CommandId,
            timeout.Token)).Should().BeEquivalentTo(sourceCommit.CaseAudit);
        (await commitStore.IsCompletedAsync(
            sourceCommit.CommandId,
            sourceCommit.InputHashSha256,
            timeout.Token)).Should().BeFalse(
            "statement-run repair may converge source projections but cannot attest that the wider handoff completed");
    }

    private StatementRunWorkflowService CreateWorkflow(
        IStatementRunWorkflowFaultInjector? faultInjector = null,
        IStatementToleranceProfileProvider? toleranceProfiles = null)
    {
        var imports = new JsonCanonicalStatementStore(_root);
        return new StatementRunWorkflowService(
            imports,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            EmptyInternalReconciliationPopulationProvider.Instance,
            IdentityReconciliationFxRateProvider.Instance,
            toleranceProfiles ?? new InMemoryStatementToleranceProfileProvider(),
            new FileStatementRunRecoveryRepository(_root),
            new FileStatementRunMatchArtifactStore(_root),
            faultInjector,
            new FileStatementCaseworkCommitStore(_root));
    }

    private async Task<string> WriteStatementAsync(string fileName)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        await File.WriteAllLinesAsync(
            path,
            [Header, "EXT-1,SPY,10,500,5000,position,2026-05-28,,USD,,"]);
        return path;
    }

    private static StatementRunRequest Request(string path) => new(
        Broker: "custodian",
        SourceInstitution: "Sample Custodian",
        FundAccountId: "FUND-1",
        ExternalAccountId: "EXT-1",
        StatementPeriodStart: new DateOnly(2026, 5, 1),
        StatementPeriodEnd: new DateOnly(2026, 5, 31),
        SourcePath: path,
        OriginalFileName: Path.GetFileName(path),
        MappingProfileId: "canonical-csv-v1",
        ToleranceProfileId: StatementToleranceProfile.DefaultProfileId,
        ImportedBy: "ops-user",
        SourceFileHash: string.Empty);

    private async Task<string> ResolveImportIdAsync(CancellationToken ct)
        => (await new JsonCanonicalStatementStore(_root).ListImportsAsync(ct))
            .Should().ContainSingle().Subject.ImportId;

    private string ProjectionPath(
        StatementRunProjectionTarget target,
        StatementRunWorkflowResult result)
    {
        var retainedBreak = result.Breaks.Should().ContainSingle().Subject;
        var retainedCase = result.Cases.Should().ContainSingle().Subject;
        var runName = ReconciliationRecordFileName.For(result.Import.ImportId);
        return target switch
        {
            StatementRunProjectionTarget.Break => Path.Combine(
                _root,
                "reconciliation",
                "statement-breaks",
                $"{ReconciliationRecordFileName.For(retainedBreak.BreakId)}.json"),
            StatementRunProjectionTarget.BreakAudit => Path.Combine(
                _root,
                "reconciliation",
                "statement-breaks",
                "_run-projections",
                runName,
                $"{ReconciliationRecordFileName.For(retainedBreak.BreakId)}.json"),
            StatementRunProjectionTarget.Case => Path.Combine(
                _root,
                "reconciliation",
                "cases",
                $"{Uri.EscapeDataString(retainedCase.CaseId)}.json"),
            StatementRunProjectionTarget.CaseAudit => Path.Combine(
                _root,
                "reconciliation",
                "cases",
                "_run-projections",
                runName,
                $"{ReconciliationRecordFileName.For(retainedCase.CaseId)}.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };
    }

    private async Task AssertLiveProjectionsMatchAsync(
        StatementRunWorkflowResult expected,
        CancellationToken ct)
    {
        var expectedBreak = expected.Breaks.Should().ContainSingle().Subject;
        var expectedCase = expected.Cases.Should().ContainSingle().Subject;
        var breakStore = new JsonReconciliationBreakStore(_root);
        var caseStore = new JsonReconciliationCaseStore(_root);
        (await breakStore.GetAsync(expectedBreak.BreakId, ct)).Should().BeEquivalentTo(expectedBreak);
        (await caseStore.GetAsync(expectedCase.CaseId, ct)).Should().BeEquivalentTo(expectedCase);
        (await breakStore.GetRunProjectionAuditAsync(expected.Import.ImportId, expectedBreak.BreakId, ct))
            .Should().NotBeNull();
        (await caseStore.GetRunProjectionAuditAsync(expected.Import.ImportId, expectedCase.CaseId, ct))
            .Should().NotBeNull();
    }

    private async Task<StatementCaseworkCommitEnvelope> RetainResolvedSourceCommitAsync(
        StatementRunWorkflowResult completed,
        CancellationToken ct,
        bool materialize = true,
        bool complete = true)
    {
        var originalBreak = completed.Breaks.Should().ContainSingle().Subject;
        var originalCase = completed.Cases.Should().ContainSingle().Subject;
        var occurredAt = originalBreak.CreatedAtUtc.AddMinutes(5);
        var update = new StatementBreakCaseworkUpdate(
            originalBreak.BreakId,
            originalBreak.ImportId,
            "Resolved",
            "fund-ops",
            "Resolve",
            $"casework-{originalBreak.BreakId}",
            $"correlation-{originalBreak.ImportId}",
            "Reviewed against retained source evidence.",
            "Resolved",
            "controller",
            "approval://controller",
            null,
            ["evidence://statement-casework"],
            occurredAt);
        var inputHash = StatementBreakCaseworkFingerprint.Compute(update);
        var nextBreak = originalBreak with { Status = update.Status };
        var breakAudit = new StatementBreakCaseworkAuditEvent(
            $"statement-casework:{inputHash[..24]}",
            originalBreak.BreakId,
            originalBreak.ImportId,
            originalBreak.Status,
            nextBreak.Status,
            update.Actor,
            update.Action,
            update.CommandId,
            update.CorrelationId,
            update.Reason,
            update.Disposition,
            update.ApprovalActor,
            update.ApprovalReference,
            update.SupersedingBreakId,
            update.EvidenceLinks,
            occurredAt,
            inputHash);
        var caseAudit = new ReconciliationCaseAuditEvent(
            $"statement-casework-case:{inputHash[..24]}",
            "StatementBreakDisposed",
            occurredAt,
            update.Actor,
            "Resolved from retained statement casework source commit.");
        var nextCase = originalCase with
        {
            Status = "Resolved",
            LastUpdatedAtUtc = occurredAt,
            LastUpdatedBy = update.Actor,
            Disposition = "Resolved",
            History = originalCase.History.Concat(
            [
                new ReconciliationCaseHistoryEntry(
                    occurredAt,
                    originalCase.Status,
                    "Resolved",
                    update.Reason!)
                {
                    Actor = update.Actor,
                    EvidenceId = update.EvidenceLinks[0]
                }
            ]).ToArray(),
            AuditEvents = originalCase.AuditEvents.Concat([caseAudit]).ToArray(),
            DecisionNotes = originalCase.DecisionNotes.Concat(
            [
                new ReconciliationCaseDecisionNote(
                    $"statement-decision:{inputHash[..24]}",
                    update.Actor,
                    occurredAt,
                    update.Reason!,
                    update.EvidenceLinks)
            ]).ToArray()
        };
        var candidate = new StatementCaseworkCommitEnvelope(
            StatementCaseworkCommitEnvelope.CurrentSchemaVersion,
            update.CommandId,
            inputHash,
            originalBreak.ImportId,
            originalBreak,
            nextBreak,
            originalCase,
            nextCase,
            breakAudit,
            caseAudit,
            occurredAt,
            AdoptedLegacyReceipt: false);
        var commitStore = new FileStatementCaseworkCommitStore(_root);
        var retained = await commitStore.PrepareAsync(candidate, ct);
        if (materialize)
        {
            var breakStore = new JsonReconciliationBreakStore(_root);
            var caseStore = new JsonReconciliationCaseStore(_root);
            await breakStore.MaterializeCaseworkBreakAsync(
                commitStore,
                retained.CommandId,
                retained.InputHashSha256,
                ct);
            await breakStore.MaterializeCaseworkAuditAsync(
                commitStore,
                retained.CommandId,
                retained.InputHashSha256,
                ct);
            await caseStore.MaterializeCaseworkAsync(
                commitStore,
                retained.CommandId,
                retained.InputHashSha256,
                ct);
        }

        if (complete)
        {
            await commitStore.CompleteAsync(retained.CommandId, retained.InputHashSha256, ct);
        }

        return retained;
    }

    private static string CorruptProjectionStatus(string content)
        => content.Replace("\"status\": \"Open\"", "\"status\": \"Tampered\"", StringComparison.Ordinal);

    private static string CorruptAuditHash(string content)
    {
        const string marker = "\"artifactSha256\": \"";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return content;
        }

        start += marker.Length;
        return content[..start] + new string('f', 64) + content[(start + 64)..];
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ThrowOnceStatementRunFaultInjector(StatementRunWorkflowFaultPoint point)
        : IStatementRunWorkflowFaultInjector
    {
        private int _remaining = 1;

        public Task OnPointAsync(
            StatementRunWorkflowFaultPoint observed,
            string runId,
            CancellationToken ct = default)
        {
            if (observed == point && Interlocked.Exchange(ref _remaining, 0) == 1)
            {
                throw new InjectedStatementRunFaultException(observed);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InjectedStatementRunFaultException(StatementRunWorkflowFaultPoint point)
        : IOException($"Injected statement-run fault after {point}.")
    {
    }

    public enum StatementRunProjectionTarget
    {
        Break,
        BreakAudit,
        Case,
        CaseAudit
    }
}
