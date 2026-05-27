using System.Collections.Concurrent;

namespace Meridian.Application.Reconciliation;

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
    DateTimeOffset UpdatedAtUtc);

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
    StatementReconciliationService service,
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
        var lastCompleted = resume ? checkpoint?.LastCompletedStage ?? StatementReconciliationStage.NotStarted : StatementReconciliationStage.NotStarted;

        var state = new StatementReconciliationCheckpoint(
            accountId,
            sourceKind,
            sourcePath,
            lastCompleted,
            StatementReconciliationStage.Validate,
            "Running",
            null,
            checkpoint?.ImportId,
            checkpoint?.ImportedRowCount ?? 0,
            checkpoint?.MatchCount ?? 0,
            checkpoint?.UnresolvedCount ?? 0,
            DateTimeOffset.UtcNow);

        try
        {
            if (lastCompleted < StatementReconciliationStage.Validate)
            {
                await service.ValidateAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
                state = state with { LastCompletedStage = StatementReconciliationStage.Validate, CurrentStage = StatementReconciliationStage.Import, UpdatedAtUtc = DateTimeOffset.UtcNow };
                await checkpointStore.UpsertAsync(state, ct).ConfigureAwait(false);
            }

            if (state.LastCompletedStage < StatementReconciliationStage.Import)
            {
                var import = await service.ImportAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
                state = state with
                {
                    LastCompletedStage = StatementReconciliationStage.Import,
                    CurrentStage = StatementReconciliationStage.Reconcile,
                    ImportId = import.ImportId,
                    ImportedRowCount = import.RowCount,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await checkpointStore.UpsertAsync(state, ct).ConfigureAwait(false);
            }

            if (state.LastCompletedStage < StatementReconciliationStage.Reconcile)
            {
                var reconciliation = await service.ReconcileAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
                state = state with
                {
                    LastCompletedStage = StatementReconciliationStage.Reconcile,
                    CurrentStage = StatementReconciliationStage.Completed,
                    Status = "Completed",
                    ImportId = reconciliation.ImportId,
                    MatchCount = reconciliation.MatchCount,
                    UnresolvedCount = reconciliation.UnresolvedCount,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
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
}
