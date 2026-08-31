using System.Collections.Concurrent;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation;

public enum StatementReconciliationStage
{
    NotStarted,
    Validate,
    Import,
    Reconcile,
    Completed,
    Failed
}

public sealed record StatementReconciliationCheckpoint(
    Guid AccountId,
    string SourceKind,
    string SourcePath,
    StatementReconciliationStage LastCompletedStage,
    StatementReconciliationStage CurrentStage,
    string Status,
    string? LastError,
    string? ImportId,
    int ImportedRowCount,
    int MatchCount,
    int UnresolvedCount,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<StatementRunEvidenceLink>? EvidenceLinks = null);

public interface IStatementReconciliationCheckpointStore
{
    Task<StatementReconciliationCheckpoint?> GetAsync(Guid accountId, CancellationToken ct);
    Task UpsertAsync(StatementReconciliationCheckpoint checkpoint, CancellationToken ct);
}

public sealed class InMemoryStatementReconciliationCheckpointStore : IStatementReconciliationCheckpointStore
{
    private readonly ConcurrentDictionary<Guid, StatementReconciliationCheckpoint> _checkpoints = new();
    public Task<StatementReconciliationCheckpoint?> GetAsync(Guid accountId, CancellationToken ct) =>
        Task.FromResult(_checkpoints.TryGetValue(accountId, out var cp) ? cp : null);

    public Task UpsertAsync(StatementReconciliationCheckpoint checkpoint, CancellationToken ct)
    {
        _checkpoints[checkpoint.AccountId] = checkpoint;
        return Task.CompletedTask;
    }
}

public sealed class StatementReconciliationOrchestrator(
    IStatementReconciliationValidationService validationService,
    IDataIntegrationIngestionService ingestionService,
    IReconciliationCaseIntakeService intakeService,
    IStatementReconciliationCheckpointStore checkpointStore)
{
    public async Task<StatementReconciliationCheckpoint> RunAsync(
        Guid accountId,
        string sourceKind,
        string sourcePath,
        bool resume,
        CancellationToken ct)
    {
        var checkpoint = await checkpointStore.GetAsync(accountId, ct).ConfigureAwait(false);

        // A persisted checkpoint is keyed by account, so only resume from it when it describes the
        // same statement source. Otherwise a resume for a different file would inherit the prior
        // run's completed stage and skip validation/import/reconcile for the new statement.
        var resumeCheckpoint = resume
            && checkpoint is not null
            && string.Equals(checkpoint.SourceKind, sourceKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(checkpoint.SourcePath, sourcePath, StringComparison.Ordinal)
                ? checkpoint
                : null;
        var lastCompleted = resumeCheckpoint?.LastCompletedStage ?? StatementReconciliationStage.NotStarted;

        var state = new StatementReconciliationCheckpoint(
            accountId,
            sourceKind,
            sourcePath,
            lastCompleted,
            StatementReconciliationStage.Validate,
            "Running",
            null,
            resumeCheckpoint?.ImportId,
            resumeCheckpoint?.ImportedRowCount ?? 0,
            resumeCheckpoint?.MatchCount ?? 0,
            resumeCheckpoint?.UnresolvedCount ?? 0,
            DateTimeOffset.UtcNow);

        try
        {
            if (lastCompleted < StatementReconciliationStage.Validate)
            {
                await validationService.ValidateAsync(
                    new StatementReconciliationValidationRequest(sourceKind, sourcePath, MappingProfileId: null),
                    ct).ConfigureAwait(false);
                state = state with { LastCompletedStage = StatementReconciliationStage.Validate, CurrentStage = StatementReconciliationStage.Import, UpdatedAtUtc = DateTimeOffset.UtcNow };
                await checkpointStore.UpsertAsync(state, ct).ConfigureAwait(false);
            }

            if (state.LastCompletedStage < StatementReconciliationStage.Import)
            {
                var import = await ingestionService.IngestAsync(
                    new DataIntegrationIngestionRequest(sourceKind, sourcePath, MappingProfileId: null),
                    ct).ConfigureAwait(false);
                state = state with
                {
                    LastCompletedStage = StatementReconciliationStage.Import,
                    CurrentStage = StatementReconciliationStage.Reconcile,
                    ImportId = import.ImportId,
                    ImportedRowCount = import.ImportedRowCount,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await checkpointStore.UpsertAsync(state, ct).ConfigureAwait(false);
            }

            if (state.LastCompletedStage < StatementReconciliationStage.Reconcile)
            {
                var reconciliation = await intakeService.IntakeAsync(
                    new ReconciliationCaseIntakeRequest(sourceKind, sourcePath, MappingProfileId: null),
                    ct).ConfigureAwait(false);
                var completedAtUtc = DateTimeOffset.UtcNow;
                var sourceFileHash = await ComputeSourceFileHashAsync(sourcePath, ct).ConfigureAwait(false);
                var statementMetadata = ReadStatementMetadata(sourcePath, accountId, completedAtUtc);
                var caseIds = reconciliation.Cases.Select(static item => item.CaseId).ToArray();
                var breakIds = reconciliation.Cases
                    .SelectMany(static item => item.EvidenceReferences)
                    .DefaultIfEmpty()
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                state = state with
                {
                    LastCompletedStage = StatementReconciliationStage.Reconcile,
                    CurrentStage = StatementReconciliationStage.Completed,
                    Status = "Completed",
                    ImportId = reconciliation.ImportId,
                    MatchCount = reconciliation.MatchCount,
                    UnresolvedCount = reconciliation.Cases.Count,
                    UpdatedAtUtc = completedAtUtc,
                    EvidenceLinks =
                    [
                        new StatementRunEvidenceLink(
                            EvidenceId: $"statement-run:{reconciliation.ImportId}",
                            EvidenceRoute: $"/api/workstation/evidence/statement-run/{Uri.EscapeDataString(reconciliation.ImportId)}",
                            RunId: reconciliation.ImportId,
                            SourceFileHash: sourceFileHash,
                            BrokerCustodian: sourceKind,
                            Account: statementMetadata.Account,
                            StatementPeriodStart: statementMetadata.PeriodStart,
                            StatementPeriodEnd: statementMetadata.PeriodEnd,
                            MappingProfileId: "orchestrated-local",
                            MappingProfileVersion: StatementRunEvidenceLinkFactory.DefaultMappingProfileVersion,
                            ToleranceProfileId: StatementToleranceProfile.DefaultProfileId,
                            ToleranceProfileVersion: StatementToleranceProfile.DefaultProfileVersion,
                            ValidationSummary: new StatementRunValidationSummary(0, 0, 0, "Passed"),
                            MatchSummary: new StatementRunMatchEvidenceSummary(reconciliation.RowCount, reconciliation.MatchCount, reconciliation.Cases.Count, reconciliation.Cases.Count),
                            BreakIds: breakIds,
                            CaseIds: caseIds,
                            ImportedBy: "system",
                            ImportedAtUtc: completedAtUtc,
                            ReconciledBy: "system",
                            ReconciledAtUtc: completedAtUtc)
                    ]
                };
                await checkpointStore.UpsertAsync(state, ct).ConfigureAwait(false);
            }

            return state;
        }
        catch (Exception ex)
        {
            var failed = state with
            {
                CurrentStage = StatementReconciliationStage.Failed,
                Status = "Failed",
                LastError = ex.Message,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await checkpointStore.UpsertAsync(failed, ct).ConfigureAwait(false);
            return failed;
        }
    }

    private static (string Account, DateOnly PeriodStart, DateOnly PeriodEnd) ReadStatementMetadata(
        string sourcePath,
        Guid accountId,
        DateTimeOffset fallbackTimestamp)
    {
        var fallbackDate = DateOnly.FromDateTime(fallbackTimestamp.UtcDateTime);
        var account = accountId.ToString();
        var tradeDates = new List<DateOnly>();

        foreach (var line in File.ReadLines(sourcePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && account == accountId.ToString() && !string.IsNullOrWhiteSpace(parts[0]))
            {
                account = parts[0];
            }

            if (parts.Length > 6 && DateOnly.TryParse(parts[6], out var tradeDate))
            {
                tradeDates.Add(tradeDate);
            }
        }

        return tradeDates.Count == 0
            ? (account, fallbackDate, fallbackDate)
            : (account, tradeDates.Min(), tradeDates.Max());
    }

    private static async Task<string> ComputeSourceFileHashAsync(string sourcePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(sourcePath);
        // Same SourceFileHash family as the statement-import producers — canonical encoding keeps
        // the value consistent across every producer of this field (#2691).
        return await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false);
    }
}
