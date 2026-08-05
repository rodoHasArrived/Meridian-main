using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Loads the <see cref="DemoTenantBlueprint"/> into the real, desk-read workstation stores so a
/// user who chooses "Use sample data" lands in a genuinely populated workspace rather than an empty
/// one behind a badge. Seeding is best-effort and idempotent: a re-run never duplicates casework or
/// runs, and a store that is unavailable in a lightweight host is skipped without failing onboarding.
/// </summary>
/// <remarks>
/// Only clearly-labelled, non-authoritative operator stores are seeded (reconciliation casework and
/// a paper strategy run). No live-trading, ledger, banking, or production-accounting state is ever
/// fabricated. These stores are keyed by the deployment data root rather than by user, so the demo
/// tenant is shared by every operator of that install — matching the single-workspace desk model.
/// </remarks>
public sealed class DemoTenantProvisioner(
    IReconciliationBreakQueueRepository? reconciliationBreaks = null,
    IStrategyRepository? strategyRuns = null,
    ILogger<DemoTenantProvisioner>? logger = null)
{
    public async Task<DemoTenantProvisioningReport> ProvisionAsync(CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var (breaksSeeded, reconciliationLoaded) = await SeedReconciliationBreaksAsync(warnings, ct).ConfigureAwait(false);
        var strategyRunLoaded = await SeedStrategyRunAsync(warnings, ct).ConfigureAwait(false);
        return new DemoTenantProvisioningReport(breaksSeeded, reconciliationLoaded, strategyRunLoaded, warnings);
    }

    private async Task<(int Seeded, bool Loaded)> SeedReconciliationBreaksAsync(List<string> warnings, CancellationToken ct)
    {
        if (reconciliationBreaks is null)
        {
            return (0, false);
        }

        var now = DateTimeOffset.UtcNow;
        var authorityScope = new ReconciliationBreakQueueScope(
            DemoTenantBlueprint.TenantId,
            DemoTenantBlueprint.CompanyId);
        var seeded = 0;
        var loaded = true;
        foreach (var definition in DemoTenantBlueprint.BreakDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var item = new ReconciliationBreakQueueItem(
                    BreakId: definition.Id,
                    RunId: DemoTenantBlueprint.StrategyRunId,
                    StrategyName: DemoTenantBlueprint.PortfolioName,
                    Category: definition.Category,
                    Status: ReconciliationBreakQueueStatus.Open,
                    Variance: definition.Variance,
                    Reason: definition.Summary,
                    AssignedTo: null,
                    DetectedAt: now,
                    LastUpdatedAt: now,
                    Severity: definition.Severity,
                    ExplainabilitySummary: definition.Summary,
                    SourceType: DemoTenantBlueprint.SeededSourceType,
                    SourceSystem: DemoTenantBlueprint.SeededSourceSystem)
                {
                    TenantId = DemoTenantBlueprint.TenantId,
                    CompanyId = DemoTenantBlueprint.CompanyId
                };

                if (await reconciliationBreaks
                        .CreateIfMissingAsync(authorityScope, item, ct)
                        .ConfigureAwait(false))
                {
                    seeded++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                loaded = false;
                logger?.LogWarning(ex, "Failed to seed sample reconciliation break {BreakId}.", definition.Id);
                warnings.Add($"Reconciliation break {definition.Id} could not be seeded: {ex.Message}");
            }
        }

        return (seeded, loaded);
    }

    private async Task<bool> SeedStrategyRunAsync(List<string> warnings, CancellationToken ct)
    {
        if (strategyRuns is null)
        {
            return false;
        }

        try
        {
            var existing = await strategyRuns.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                // A prior run that recorded the Started event but was interrupted before Completed
                // would otherwise be left permanently incomplete. Finish it so re-provisioning
                // converges on a completed run instead of silently skipping. Either way the run is
                // present, so the Strategy desk is loaded.
                if (existing.EndedAt is null)
                {
                    await strategyRuns.RecordRunAsync(existing.Complete(metrics: null), ct).ConfigureAwait(false);
                }

                return true;
            }

            // Record the run through its real lifecycle (Started → Completed) so the durable,
            // hash-chained case store accepts it. Metrics are intentionally null: a completed paper
            // run is enough to light up the Strategy desk and the Portfolio run-linked panels
            // without fabricating a full backtest result.
            var started = StrategyRunEntry.Start(
                DemoTenantBlueprint.StrategyId,
                DemoTenantBlueprint.StrategyName,
                RunType.Paper,
                DemoTenantBlueprint.StrategyRunId,
                datasetReference: "SAMPLE",
                engine: "BrokerPaper");

            await strategyRuns.RecordRunAsync(started, ct).ConfigureAwait(false);
            await strategyRuns.RecordRunAsync(started.Complete(metrics: null), ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to seed sample strategy run {RunId}.", DemoTenantBlueprint.StrategyRunId);
            warnings.Add($"Sample strategy run could not be seeded: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Summary of what the demo-tenant provisioning step wrote. The <c>Loaded</c> flags report whether
/// each surface is present in its desk store after provisioning (freshly seeded or already there),
/// which is what onboarding advertises — distinct from <see cref="ReconciliationBreaksSeeded"/>,
/// which counts only the breaks created on this run.
/// </summary>
public sealed record DemoTenantProvisioningReport(
    int ReconciliationBreaksSeeded,
    bool ReconciliationLoaded,
    bool StrategyRunLoaded,
    IReadOnlyList<string> Warnings);
