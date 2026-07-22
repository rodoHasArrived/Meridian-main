using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Storage.AssetOperations;

public sealed partial class InMemoryAssetOperationsProjectionStore :
    IAssetOperationsProjectionStore,
    IInstrumentPositionProjectionStore,
    IAssetAccountingEventProjectionStore
{
    private readonly ILedgerJournalStore? _assetAccountingJournalStore;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AssetOperationsDetailDto> _details = new();

    public InMemoryAssetOperationsProjectionStore(ILedgerJournalStore? assetAccountingJournalStore = null)
    {
        _assetAccountingJournalStore = assetAccountingJournalStore;
    }

    public Task<AssetOperationsDetailDto?> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var detail = _details.GetValueOrDefault(securityId);
            return Task.FromResult(detail is null ? null : ClonePayload(detail));
        }
    }

    public Task UpsertAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(approval);
        ct.ThrowIfCancellationRequested();
        projection = ClonePayload(projection);
        approval = ClonePayload(approval);

        lock (_gate)
        {
            var existing = _details.GetValueOrDefault(projection.Subject.SecurityId);
            var incomingLineages = projection.ProjectionLineages
                .Concat(projection.BookPositions
                    .Select(static position => position.ProjectionLineage)
                    .Where(static lineage => lineage is not null)!)
                .Concat(projection.PositionEconomicStates
                    .Select(static state => state.ProjectionLineage)
                    .Where(static lineage => lineage is not null)!)
                .Cast<ProjectionLineageDto>()
                .ToArray();
            var lineages = MergeProjectionLineageHistory(existing?.ProjectionLineages, incomingLineages);
            var economicStates = projection.PositionEconomicStates
                .Select(state => state.ProjectionLineage is not null
                    ? state
                    : state with
                    {
                        ProjectionLineage = lineages.FirstOrDefault(lineage =>
                            lineage.BookPositionId == state.PositionId &&
                            lineage.TriggerEvent.EventId == state.SourceEvent?.EventId)
                    })
                .ToArray();
            var detail = new AssetOperationsDetailDto(
                projection.Subject,
                projection.TermsHistory,
                projection.LifecycleEvents,
                projection.CashFlowProjectionRuns,
                projection.ProjectedCashFlows,
                projection.ActualActivity,
                projection.ReconciliationRuns,
                projection.ReconciliationResults,
                projection.LedgerProjections,
                projection.Readiness,
                projection.WorkflowAudit)
            {
                TermsObligationsTimeline = projection.TermsObligationsTimeline,
                InstrumentRoles = PreserveWhenEmpty(projection.InstrumentRoles, existing?.InstrumentRoles),
                BookPositions = PreserveWhenEmpty(projection.BookPositions, existing?.BookPositions),
                PositionEconomicStates = MergeEconomicStateHistory(
                    existing?.PositionEconomicStates,
                    economicStates),
                ProjectionLineages = lineages
            };
            ApplyLegacyInstrumentPositionProjection(detail, approval);
            var snapshot = BuildSecuritySnapshot(projection.Subject.SecurityId);
            detail = detail with
            {
                InstrumentRoles = snapshot.InstrumentRoles,
                BookPositions = snapshot.BookPositions,
                PositionEconomicStates = snapshot.PositionEconomicStates,
                ProjectionLineages = snapshot.ProjectionLineages
            };
            _details[projection.Subject.SecurityId] = detail;
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<T> PreserveWhenEmpty<T>(
        IReadOnlyList<T> incoming,
        IReadOnlyList<T>? existing)
        => incoming.Count == 0 && existing is { Count: > 0 }
            ? existing
            : incoming;

    private static IReadOnlyList<PositionEconomicStateDto> MergeEconomicStateHistory(
        IReadOnlyList<PositionEconomicStateDto>? existing,
        IReadOnlyList<PositionEconomicStateDto> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing ?? [];
        }

        var byId = (existing ?? [])
            .ToDictionary(static state => state.EconomicStateId);
        foreach (var state in incoming)
        {
            if (byId.TryGetValue(state.EconomicStateId, out var persisted) && persisted != state)
            {
                throw new InvalidOperationException(
                    $"Economic state '{state.EconomicStateId:D}' is append-only and cannot be replaced.");
            }

            byId[state.EconomicStateId] = state;
        }

        return byId.Values
            .OrderBy(static state => state.AsOfDate)
            .ThenBy(static state => state.Version)
            .ThenBy(static state => state.EconomicStateId)
            .ToArray();
    }

    private static IReadOnlyList<ProjectionLineageDto> MergeProjectionLineageHistory(
        IReadOnlyList<ProjectionLineageDto>? existing,
        IReadOnlyList<ProjectionLineageDto> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing ?? [];
        }

        var byId = (existing ?? [])
            .ToDictionary(static lineage => lineage.ProjectionRunId);
        foreach (var lineage in incoming)
        {
            if (byId.TryGetValue(lineage.ProjectionRunId, out var persisted) &&
                (persisted.ProjectionEventId != lineage.ProjectionEventId ||
                 persisted.TriggerEvent.EventId != lineage.TriggerEvent.EventId ||
                 !string.Equals(persisted.ModelKey, lineage.ModelKey, StringComparison.OrdinalIgnoreCase) ||
                 persisted.BookPositionId != lineage.BookPositionId))
            {
                throw new InvalidOperationException(
                    $"Projection lineage '{lineage.ProjectionRunId:D}' is append-only and cannot be replaced.");
            }

            byId[lineage.ProjectionRunId] = persisted ?? lineage;
        }

        return byId.Values
            .OrderBy(static lineage => lineage.ProjectionAsOfDate)
            .ThenBy(static lineage => lineage.GeneratedAtUtc)
            .ThenBy(static lineage => lineage.ProjectionRunId)
            .ToArray();
    }
}
