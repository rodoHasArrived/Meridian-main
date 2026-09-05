using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using System.Text.Json;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Guards the match-artifact schema widening that retains group-aware match records
/// (W9-INGEST-009): a legacy artifact written before groups existed must keep hash-verifying
/// against its recovery checkpoint, replays across the upgrade must converge without corruption
/// reports, and artifacts that assign the same number of matches through different memberships
/// must retain different bytes.
/// </summary>
public sealed class StatementRunMatchArtifactUpgradeTests : IDisposable
{
    private const string Header =
        "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-match-artifact-upgrade-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Serialize_LegacyArtifactWithoutGroups_OmitsThePropertyAndRoundTripsByteIdentically()
    {
        // The legacy shape is exactly "no MatchGroups": pre-upgrade writers never serialized the
        // property, and null must keep it out of the payload so a retained legacy artifact
        // reserializes to the same bytes its checkpoint hash was computed over.
        var legacy = Artifact(matchGroups: null);

        var json = JsonSerializer.Serialize(
            legacy,
            StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact);

        json.Should().NotContain("matchGroups");
        var roundTripped = JsonSerializer.Deserialize(
            json,
            StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact);
        roundTripped!.MatchGroups.Should().BeNull();
        JsonSerializer.Serialize(
                roundTripped,
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact)
            .Should().Be(json);
    }

    [Fact]
    public void Serialize_ArtifactWithGroups_RetainsThemAndHashesDifferentlyFromLegacy()
    {
        var withGroups = Artifact(matchGroups:
        [
            Group("group-1", ["IMP-1:1"], ["internal:journal:a", "internal:journal:b"])
        ]);

        var json = JsonSerializer.Serialize(
            withGroups,
            StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact);

        json.Should().Contain("matchGroups");
        StatementDurabilityHashing.Hash(
                withGroups,
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact)
            .Should().NotBe(StatementDurabilityHashing.Hash(
                withGroups with { MatchGroups = null },
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact));
    }

    [Fact]
    public void Hash_DiffersWhenGroupMembershipDiffers()
    {
        // The row's determinism criterion is only evidenceable if two different split assignments
        // of the same size retain different artifacts.
        var first = Artifact(matchGroups:
        [
            Group("group-1", ["IMP-1:1"], ["internal:journal:a", "internal:journal:b"])
        ]);
        var second = Artifact(matchGroups:
        [
            Group("group-1", ["IMP-1:1"], ["internal:journal:a", "internal:journal:c"])
        ]);

        StatementDurabilityHashing.Hash(
                first,
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact)
            .Should().NotBe(StatementDurabilityHashing.Hash(
                second,
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact));
    }

    [Fact]
    public async Task Workflow_RunCompletedByPreUpgradeCode_ReplaysWithoutCorruption()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("legacy-completed.csv");
        var request = Request(path);
        var completed = await CreateWorkflow().CreateAsync(request, timeout.Token);
        var runId = completed.Import.ImportId;
        var legacyArtifact = await RewriteRetainedArtifactAsLegacyAsync(runId, timeout.Token);

        // Point the retained checkpoint at the legacy artifact's hash, exactly as pre-upgrade code
        // recorded it (the legacy type serialized the same five properties in the same order). A
        // completed checkpoint is immutable through the repository, so rewrite the retained file
        // directly — this test fabricates the on-disk state the old code left behind.
        var repository = new FileStatementRunRecoveryRepository(_root);
        var checkpoint = await repository.GetAsync(runId, timeout.Token);
        var checkpointPath = Directory
            .EnumerateFiles(
                Path.Combine(_root, "reconciliation", "statement-runs"),
                "workflow-checkpoint.json",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        await StatementRunRecoveryJson.WriteAsync(
            checkpointPath,
            checkpoint! with
            {
                MatchArtifact = new StatementRunStageArtifact(
                    StatementDurabilityHashing.Hash(
                        legacyArtifact,
                        StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact),
                    legacyArtifact.MatchCount + legacyArtifact.Breaks.Count)
            },
            timeout.Token);

        var replayed = await CreateWorkflow().CreateAsync(request, timeout.Token);

        replayed.Import.ImportId.Should().Be(runId);
        var retained = await new FileStatementRunMatchArtifactStore(_root).GetAsync(runId, timeout.Token);
        retained!.MatchGroups.Should().BeNull("a legacy artifact is adopted as written, never rewritten under its retained hash");
    }

    [Fact]
    public async Task Workflow_LegacyArtifactRetainedBeforeMatchedCheckpoint_IsAdoptedOnReplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var path = await WriteStatementAsync("legacy-crashed.csv");
        var request = Request(path);
        var interrupted = CreateWorkflow(new ThrowOnceFaultInjector(StatementRunWorkflowFaultPoint.MatchArtifactVerified));

        var act = async () => await interrupted.CreateAsync(request, timeout.Token);

        // The artifact is retained but the Matched checkpoint is not: the crash window the upgrade
        // guidance calls out. Rewrite the retained artifact to the pre-upgrade shape and replay.
        await act.Should().ThrowAsync<InvalidOperationException>();
        var runId = (await new JsonCanonicalStatementStore(_root).ListImportsAsync(timeout.Token))
            .Should().ContainSingle().Subject.ImportId;
        var legacyArtifact = await RewriteRetainedArtifactAsLegacyAsync(runId, timeout.Token);

        var recovered = await CreateWorkflow().CreateAsync(request, timeout.Token);

        recovered.Import.ImportId.Should().Be(runId);
        var repository = new FileStatementRunRecoveryRepository(_root);
        var checkpoint = await repository.GetAsync(runId, timeout.Token);
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.MatchArtifact!.Sha256.Should().Be(
            StatementDurabilityHashing.Hash(
                legacyArtifact,
                StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact),
            "the replay must adopt the retained legacy artifact instead of overwriting it");
        var retained = await new FileStatementRunMatchArtifactStore(_root).GetAsync(runId, timeout.Token);
        retained!.MatchGroups.Should().BeNull();
    }

    /// <summary>
    /// Rewrites the retained match artifact for <paramref name="runId"/> to the pre-upgrade shape
    /// (no match groups) and returns the legacy artifact, simulating a run written before groups
    /// were retained.
    /// </summary>
    private async Task<StatementRunMatchArtifact> RewriteRetainedArtifactAsLegacyAsync(
        string runId,
        CancellationToken ct)
    {
        var store = new FileStatementRunMatchArtifactStore(_root);
        var current = await store.GetAsync(runId, ct);
        current!.MatchGroups.Should().NotBeNull("new runs must retain match groups");
        var legacy = current with { MatchGroups = null };
        var artifactPath = Path.Combine(
            _root,
            "reconciliation",
            "statement-runs",
            ReconciliationRecordFileName.For(runId),
            "workflow-match-artifact.json");
        File.Exists(artifactPath).Should().BeTrue();
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(legacy, StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact),
            ct);
        return legacy;
    }

    private StatementRunWorkflowService CreateWorkflow(IStatementRunWorkflowFaultInjector? faultInjector = null)
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
            new InMemoryStatementToleranceProfileProvider(),
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

    private static StatementRunMatchArtifact Artifact(IReadOnlyList<StatementRunMatchGroupRecord>? matchGroups)
        => new("IMP-1", "IMP-1", [], [], matchGroups?.Count ?? 1)
        {
            MatchGroups = matchGroups
        };

    private static StatementRunMatchGroupRecord Group(
        string groupId,
        IReadOnlyList<string> statementReferences,
        IReadOnlyList<string> internalReferences)
        => new(
            groupId,
            "Transaction",
            "Exact",
            ["statement-transaction-split-v1"],
            statementReferences,
            internalReferences,
            1.00m);

    private sealed class ThrowOnceFaultInjector(StatementRunWorkflowFaultPoint point) : IStatementRunWorkflowFaultInjector
    {
        private int _remaining = 1;

        public Task OnPointAsync(
            StatementRunWorkflowFaultPoint faultPoint,
            string runId,
            CancellationToken ct = default)
        {
            if (faultPoint == point && Interlocked.Exchange(ref _remaining, 0) == 1)
            {
                throw new InvalidOperationException($"Injected statement-run fault at {faultPoint}.");
            }

            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
