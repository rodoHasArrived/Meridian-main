using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
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
/// Only clearly-labelled operator stores are seeded, and every record carries the seeded
/// provenance mark so no figure can pass for real (W9-TRUTH-001): reconciliation casework, a paper
/// strategy run, the sample fund account with a durable position snapshot, draft (never posted)
/// manual journal entries, and a governed sample report pack. No live-trading or
/// production-accounting posting is ever fabricated — accounting records seed as drafts for human
/// review. These stores are keyed by the deployment data root rather than by user, so the demo
/// tenant is shared by every operator of that install — matching the single-workspace desk model.
/// </remarks>
public sealed class DemoTenantProvisioner(
    IReconciliationBreakQueueRepository? reconciliationBreaks = null,
    IStrategyRepository? strategyRuns = null,
    ILogger<DemoTenantProvisioner>? logger = null,
    IFundAccountService? fundAccounts = null,
    IPositionSnapshotStore? positionSnapshots = null,
    IManualJournalEntryDraftStore? journalDrafts = null,
    IGovernanceReportPackRepository? reportPacks = null)
{
    public async Task<DemoTenantProvisioningReport> ProvisionAsync(CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var (breaksSeeded, reconciliationLoaded) = await SeedReconciliationBreaksAsync(warnings, ct).ConfigureAwait(false);
        var strategyRunLoaded = await SeedStrategyRunAsync(warnings, ct).ConfigureAwait(false);
        var fundAccountLoaded = await SeedFundAccountAsync(warnings, ct).ConfigureAwait(false);
        var portfolioLoaded = await SeedPortfolioSnapshotAsync(warnings, ct).ConfigureAwait(false);
        var journalDraftsSeeded = await SeedJournalDraftsAsync(warnings, ct).ConfigureAwait(false);
        var reportPackLoaded = await SeedReportPackAsync(warnings, ct).ConfigureAwait(false);
        return new DemoTenantProvisioningReport(
            breaksSeeded,
            reconciliationLoaded,
            strategyRunLoaded,
            warnings,
            fundAccountLoaded,
            portfolioLoaded,
            journalDraftsSeeded,
            reportPackLoaded);
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
                    SourceSystem: DemoTenantBlueprint.SeededSourceSystem,
                    DataProvenanceToken: DemoTenantBlueprint.SeededSourceType)
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
            // without fabricating a full backtest result. The seeded provenance token is a
            // blocking simulation mark: promotion evidence rejects this run outright.
            var started = StrategyRunEntry.Start(
                DemoTenantBlueprint.StrategyId,
                DemoTenantBlueprint.StrategyName,
                RunType.Paper,
                DemoTenantBlueprint.StrategyRunId,
                datasetReference: "SAMPLE",
                engine: "BrokerPaper") with
            {
                DataProvenanceToken = DemoTenantBlueprint.SeededSourceType
            };

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

    private async Task<bool> SeedFundAccountAsync(List<string> warnings, CancellationToken ct)
    {
        if (fundAccounts is null)
        {
            return false;
        }

        try
        {
            var existing = await fundAccounts.GetAccountAsync(DemoTenantBlueprint.FundAccountId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return true;
            }

            await fundAccounts.CreateAccountAsync(
                new CreateAccountRequest(
                    DemoTenantBlueprint.FundAccountId,
                    AccountTypeDto.Brokerage,
                    DemoTenantBlueprint.FundAccountCode,
                    DemoTenantBlueprint.FundAccountDisplayName,
                    DemoTenantBlueprint.BaseCurrency,
                    EffectiveFrom: DateTimeOffset.UtcNow,
                    CreatedBy: DemoTenantBlueprint.SeededSourceSystem,
                    Institution: DemoTenantBlueprint.SeededSourceSystem,
                    PortfolioId: DemoTenantBlueprint.PortfolioName,
                    StrategyId: DemoTenantBlueprint.StrategyId,
                    RunId: DemoTenantBlueprint.StrategyRunId),
                ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to seed the sample fund account.");
            warnings.Add($"Sample fund account could not be seeded: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SeedPortfolioSnapshotAsync(List<string> warnings, CancellationToken ct)
    {
        if (positionSnapshots is null)
        {
            return false;
        }

        try
        {
            var positions = DemoTenantBlueprint.Holdings
                .Select(static holding => new PositionRecord(
                    holding.Symbol,
                    holding.Quantity,
                    CostBasis: holding.AveragePrice,
                    UnrealisedPnl: holding.UnrealizedPnl,
                    RealisedPnl: 0m))
                .ToArray();

            // A fixed AsOf makes re-seeding idempotent: the snapshot store treats an
            // equivalent (run, account, AsOf) retry as a no-op. The snapshot is written in
            // the unowned (run, account) scope the paper-session flow uses — the store's
            // accounting owner scope is all-or-nothing and the demo does not fabricate a
            // ledger-book identity.
            var snapshot = new AccountSnapshotRecord(
                RunId: DemoTenantBlueprint.StrategyRunId,
                AccountId: DemoTenantBlueprint.FundAccountId.ToString("D"),
                AccountDisplayName: DemoTenantBlueprint.FundAccountDisplayName,
                AccountKind: "Paper",
                Cash: DemoTenantBlueprint.Cash,
                MarginBalance: 0m,
                UnrealisedPnl: DemoTenantBlueprint.UnrealizedPnl,
                RealisedPnl: 0m,
                Positions: positions,
                AsOf: new DateTimeOffset(2026, 1, 2, 21, 0, 0, TimeSpan.Zero));

            await positionSnapshots.SaveSnapshotAsync(snapshot, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to seed the sample portfolio position snapshot.");
            warnings.Add($"Sample portfolio positions could not be seeded: {ex.Message}");
            return false;
        }
    }

    private async Task<int> SeedJournalDraftsAsync(List<string> warnings, CancellationToken ct)
    {
        if (journalDrafts is null)
        {
            return 0;
        }

        var seeded = 0;
        foreach (var draft in BuildJournalDrafts())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var existing = await journalDrafts
                    .GetAsync(DemoTenantBlueprint.FundProfileId, draft.JournalEntryId, ct)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    seeded++;
                    continue;
                }

                await journalDrafts.SaveAsync(draft, ct).ConfigureAwait(false);
                seeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "Failed to seed sample journal draft {DraftId}.", draft.JournalEntryId);
                warnings.Add($"Sample journal draft {draft.Memo} could not be seeded: {ex.Message}");
            }
        }

        return seeded;
    }

    private static IEnumerable<ManualJournalEntryDraftDto> BuildJournalDrafts()
    {
        var createdAt = new DateTimeOffset(2026, 1, 2, 21, 0, 0, TimeSpan.Zero);
        var accountingDate = new DateOnly(2026, 1, 2);

        yield return BuildBalancedDraft(
            DemoTenantBlueprint.AccruedFeeDraftId,
            accountingDate,
            createdAt,
            memo: "SAMPLE — Accrue custodian fee for January",
            amount: 42.50m,
            debitAccountPath: "Expenses:CustodianFees",
            creditAccountPath: "Liabilities:AccruedExpenses");

        yield return BuildBalancedDraft(
            DemoTenantBlueprint.DividendDraftId,
            accountingDate,
            createdAt,
            memo: "SAMPLE — Dividend receivable (SPY)",
            amount: 187.20m,
            debitAccountPath: "Assets:DividendsReceivable",
            creditAccountPath: "Income:Dividends");
    }

    private static ManualJournalEntryDraftDto BuildBalancedDraft(
        Guid journalEntryId,
        DateOnly accountingDate,
        DateTimeOffset createdAt,
        string memo,
        decimal amount,
        string debitAccountPath,
        string creditAccountPath)
        => new(
            JournalEntryId: journalEntryId,
            Status: ManualJournalEntryStatusDto.Draft,
            FundProfileId: DemoTenantBlueprint.FundProfileId,
            LedgerBookId: null,
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingDate: accountingDate,
            PeriodId: null,
            EntityId: null,
            FundNodeId: null,
            Currency: DemoTenantBlueprint.BaseCurrency,
            Memo: memo,
            PreparedBy: DemoTenantBlueprint.SeededSourceSystem,
            CreatedAtUtc: createdAt,
            UpdatedAtUtc: createdAt,
            Version: 1,
            Lines:
            [
                new ManualJournalEntryLineDto(
                    LineId: $"{journalEntryId:N}-d",
                    Side: AccountingTemplateLineSideDto.Debit,
                    Amount: amount,
                    Currency: DemoTenantBlueprint.BaseCurrency,
                    AccountPath: debitAccountPath,
                    Description: memo),
                new ManualJournalEntryLineDto(
                    LineId: $"{journalEntryId:N}-c",
                    Side: AccountingTemplateLineSideDto.Credit,
                    Amount: amount,
                    Currency: DemoTenantBlueprint.BaseCurrency,
                    AccountPath: creditAccountPath,
                    Description: memo)
            ],
            EvidenceLinks: [$"seeded-demo:{DemoTenantBlueprint.SeededSourceSystem}"],
            ValidationIssues: [],
            TotalDebits: amount,
            TotalCredits: amount,
            Imbalance: 0m);

    private async Task<bool> SeedReportPackAsync(List<string> warnings, CancellationToken ct)
    {
        if (reportPacks is null)
        {
            return false;
        }

        try
        {
            var existing = await reportPacks
                .GetAsync(DemoTenantBlueprint.ReportPackId, ct)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return true;
            }

            var generatedAt = new DateTimeOffset(2026, 1, 2, 21, 30, 0, TimeSpan.Zero);
            var snapshot = new FundReportPackSnapshotDto(
                ReportId: DemoTenantBlueprint.ReportPackId,
                FundProfileId: DemoTenantBlueprint.FundProfileId,
                DisplayName: DemoTenantBlueprint.ReportPackDisplayName,
                ReportKind: GovernanceReportKindDto.HoldingsReport,
                Currency: DemoTenantBlueprint.BaseCurrency,
                AsOf: generatedAt,
                GeneratedAt: generatedAt,
                TotalNetAssets: DemoTenantBlueprint.PortfolioValue,
                AuditActor: DemoTenantBlueprint.SeededSourceSystem,
                CorrelationId: $"seeded-demo-{DemoTenantBlueprint.ReportPackId:N}",
                DecisionRationale: "Seeded sample report pack for evaluation; not a governed production deliverable.",
                Provenance: new FundReportPackProvenanceDto(
                    RelatedRunIds: [DemoTenantBlueprint.StrategyRunId],
                    JournalEntryCount: 0,
                    LedgerEntryCount: 0,
                    TrialBalanceLineCount: 0,
                    ReconciliationRunCount: 0,
                    OpenReconciliationBreakCount: DemoTenantBlueprint.BreakDefinitions.Count,
                    SecurityResolvedCount: DemoTenantBlueprint.Holdings.Count,
                    SecurityMissingCount: 0,
                    LineagePointers: [],
                    SourceSnapshotHash: $"seeded:{DemoTenantBlueprint.ReportPackId:N}",
                    DataProvenanceToken: DemoTenantBlueprint.SeededSourceType),
                Artifacts: [],
                Warnings: ["Seeded demo data — every figure derives from the sample blueprint."])
            {
                // A seeded pack reads as review-required, never as an approved deliverable:
                // simulated figures must not present as governed production output.
                Status = GovernanceReportPackStatusDto.ReviewRequired,
                LifecycleEvents =
                [
                    new FundReportPackLifecycleEventDto(
                        FromStatus: null,
                        ToStatus: GovernanceReportPackStatusDto.ReviewRequired,
                        ChangedAt: generatedAt,
                        Actor: DemoTenantBlueprint.SeededSourceSystem,
                        Reason: "Seeded demo report pack; review-required by construction.",
                        CorrelationId: $"seeded-demo-{DemoTenantBlueprint.ReportPackId:N}")
                ]
            };

            var summaryJson = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                title = DemoTenantBlueprint.ReportPackDisplayName,
                provenance = DemoTenantBlueprint.SeededSourceType,
                portfolio = DemoTenantBlueprint.PortfolioName,
                totalNetAssets = DemoTenantBlueprint.PortfolioValue,
                note = "Seeded demo data - not provider-backed operational records."
            });

            await reportPacks.SaveAsync(
                snapshot,
                [
                    new GovernanceReportPackArtifactContent(
                        "summary",
                        GovernanceReportArtifactFormatDto.Json,
                        "summary.json",
                        summaryJson)
                ],
                ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Failed to seed the sample report pack.");
            warnings.Add($"Sample report pack could not be seeded: {ex.Message}");
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
    IReadOnlyList<string> Warnings,
    bool FundAccountLoaded = false,
    bool PortfolioPositionsLoaded = false,
    int JournalDraftsSeeded = 0,
    bool ReportPackLoaded = false);
