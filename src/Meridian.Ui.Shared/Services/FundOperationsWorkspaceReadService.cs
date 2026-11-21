using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared Accounting and fund-operations workspace projection used by
/// the local API and future cross-workspace surfaces.
/// </summary>
public sealed partial class FundOperationsWorkspaceReadService
{

    private readonly IFundAccountService _fundAccountService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;
    private readonly IReconciliationRunService? _strategyReconciliationService;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly NavAttributionService _navAttributionService;
    private readonly ReportGenerationService _reportGenerationService;
    private readonly IGovernanceReportPackRepository? _reportPackRepository;
    private readonly ReportPackValidationService _reportPackValidationService;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly IOperationsContinuityWorkflowService? _operationsContinuityWorkflowService;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;
    private readonly ReportPackDeliveryService? _reportPackDeliveryService;
    private readonly ReportingScheduleService? _reportingScheduleService;
    private readonly ReportPackRunReadService? _reportPackRunReadService;
    private readonly DirectLendingOperationsReadService? _directLendingOperationsReadService;
    private readonly ILedgerJournalStore? _ledgerJournalStore;

    public FundOperationsWorkspaceReadService(
        IFundAccountService fundAccountService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        NavAttributionService navAttributionService,
        ReportGenerationService reportGenerationService,
        ISecurityReferenceLookup? securityReferenceLookup = null,
        IReconciliationRunService? strategyReconciliationService = null,
        IReconciliationBreakQueueRepository? breakQueueRepository = null,
        IGovernanceReportPackRepository? reportPackRepository = null,
        ReportPackValidationService? reportPackValidationService = null,
        ISecurityValidationGateService? securityValidationGate = null,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        ReportPackDeliveryService? reportPackDeliveryService = null,
        ReportingScheduleService? reportingScheduleService = null,
        ReportPackRunReadService? reportPackRunReadService = null,
        DirectLendingOperationsReadService? directLendingOperationsReadService = null,
        ILedgerJournalStore? ledgerJournalStore = null)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _navAttributionService = navAttributionService ?? throw new ArgumentNullException(nameof(navAttributionService));
        _reportGenerationService = reportGenerationService ?? throw new ArgumentNullException(nameof(reportGenerationService));
        _securityReferenceLookup = securityReferenceLookup;
        _strategyReconciliationService = strategyReconciliationService;
        _breakQueueRepository = breakQueueRepository;
        _reportPackRepository = reportPackRepository;
        _reportPackValidationService = reportPackValidationService ?? new ReportPackValidationService();
        _securityValidationGate = securityValidationGate;
        _operationsContinuityWorkflowService = operationsContinuityWorkflowService;
        _reportPackWorkflowService = reportPackWorkflowService;
        _reportPackDeliveryService = reportPackDeliveryService;
        _reportingScheduleService = reportingScheduleService;
        _reportPackRunReadService = reportPackRunReadService;
        _directLendingOperationsReadService = directLendingOperationsReadService;
        _ledgerJournalStore = ledgerJournalStore;
    }

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        CancellationToken ct = default) =>
        await GetWorkspaceAsync(query, accessContext: null, ct).ConfigureAwait(false);

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var reconciliationQueueScope = ResolveReconciliationQueueScope(accessContext);
        var normalizedFundProfileId = query.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);

        var runsTask = LoadRunsAsync(normalizedFundProfileId, ct);
        var allRunsTask = LoadFundScopedRunsAsync(ct);
        var accountProjectionsTask = GetAccountProjectionsAsync(fundId, ct);
        var allAccountProjectionsTask = GetAllAccountProjectionsAsync(ct);
        var bankSnapshotsTask = GetBankSnapshotsAsync(fundId, ct);

        await Task.WhenAll(runsTask, accountProjectionsTask, bankSnapshotsTask).ConfigureAwait(false);

        var runs = await runsTask.ConfigureAwait(false);
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        if (selectedLedgerIds.Count > 0)
        {
            runs = runs.Where(run => selectedLedgerIds.Contains(run.RunId)).ToArray();
        }

        var accountProjections = await accountProjectionsTask.ConfigureAwait(false);
        var bankSnapshots = await bankSnapshotsTask.ConfigureAwait(false);
        var accountSummaries = accountProjections
            .Select(static projection => projection.Summary)
            .ToArray();

        var baseCurrency = ResolveCurrency(query.Currency, accountSummaries);
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var ledgerBook = await BuildLedgerBookAsync(normalizedFundProfileId, runs, asOf, ct).ConfigureAwait(false);
        var ledger = await BuildLedgerSummaryAsync(
            normalizedFundProfileId,
            displayName,
            query.ScopeKind,
            query.ScopeId,
            asOf,
            ledgerBook,
            ct).ConfigureAwait(false);
        var ledgerReconciliationSnapshot = ProjectReconciliationSnapshot(ledgerBook.ReconciliationSnapshot(asOf));

        var cashTask = BuildCashFinancingSummaryAsync(
            baseCurrency,
            accountProjections,
            runs,
            ct);
        var reconciliationTask = BuildReconciliationSummaryAsync(
            normalizedFundProfileId,
            accountProjections,
            runs,
            reconciliationQueueScope,
            ct);
        var navTask = BuildNavSummaryAsync(
            normalizedFundProfileId,
            baseCurrency,
            ledgerBook,
            asOf,
            ct);
        var directLendingOperationsTask = _directLendingOperationsReadService?.GetOperationsAsync(ct: ct);

        await Task.WhenAll(cashTask, reconciliationTask, navTask).ConfigureAwait(false);

        var cashBuild = await cashTask.ConfigureAwait(false);
        var cashFinancing = cashBuild.Summary;
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var directLendingOperations = directLendingOperationsTask is null
            ? null
            : await directLendingOperationsTask.ConfigureAwait(false);
        var allRuns = await allRunsTask.ConfigureAwait(false);
        var allAccountProjections = await allAccountProjectionsTask.ConfigureAwait(false);
        var crossFundConsolidations = await BuildCrossFundReportingConsolidationsAsync(
            normalizedFundProfileId,
            baseCurrency,
            allAccountProjections,
            allRuns,
            asOf,
            ct).ConfigureAwait(false);
        var reporting = BuildReportingSummary(
            normalizedFundProfileId,
            accountSummaries,
            ledger,
            ledgerReconciliationSnapshot,
            cashFinancing,
            nav,
            cashBuild.RunSources,
            crossFundConsolidations,
            asOf,
            accessContext);
        var governance = await BuildGovernanceLifecycleProjectionAsync(
            normalizedFundProfileId,
            accountSummaries,
            reconciliation,
            ct).ConfigureAwait(false);
        var workspace = BuildWorkspaceSummary(
            normalizedFundProfileId,
            displayName,
            baseCurrency,
            asOf,
            accountSummaries,
            cashFinancing,
            reconciliation,
            ledger);

        return new FundOperationsWorkspaceDto(
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            RecordedRunCount: runs.Count,
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            Workspace: workspace,
            Ledger: ledger,
            LedgerReconciliationSnapshot: ledgerReconciliationSnapshot,
            Accounts: accountSummaries,
            BankSnapshots: bankSnapshots,
            CashFinancing: cashFinancing,
            Reconciliation: reconciliation,
            Nav: nav,
            Reporting: reporting,
            Governance: governance,
            DirectLendingOperations: directLendingOperations);
    }

    private static HashSet<string> NormalizeSelectedLedgerIds(IReadOnlyList<string>? selectedLedgerIds) =>
        selectedLedgerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];

    private static FundWorkspaceSummary BuildWorkspaceSummary(
        string fundProfileId,
        string displayName,
        string baseCurrency,
        DateTimeOffset asOf,
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashFinancing,
        ReconciliationSummary reconciliation,
        FundLedgerSummary ledger)
    {
        var totalEquity = cashFinancing.TotalEquity != 0m
            ? cashFinancing.TotalEquity
            : accounts.Sum(static account => account.NetAssetValue);

        var securityResolvedCount = ledger.TrialBalance.Count(static line => line.Security is not null);
        var securityMissingCount = ledger.TrialBalance.Count(static line =>
            !string.IsNullOrWhiteSpace(line.Symbol) &&
            line.Security is null);

        return new FundWorkspaceSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            TotalAccounts: accounts.Count,
            BankAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Bank),
            BrokerageAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Brokerage),
            CustodyAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Custody),
            TotalCash: cashFinancing.TotalCash,
            GrossExposure: cashFinancing.GrossExposure,
            NetExposure: cashFinancing.NetExposure,
            TotalEquity: totalEquity,
            FinancingCost: cashFinancing.FinancingCost,
            PendingSettlement: cashFinancing.PendingSettlement,
            OpenReconciliationBreaks: reconciliation.OpenBreakCount,
            ReconciliationRuns: reconciliation.RunCount,
            JournalEntryCount: ledger.JournalEntryCount,
            TrialBalanceLineCount: ledger.TrialBalance.Count,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            SecurityCoverageIssues: reconciliation.SecurityCoverageIssueCount);
    }

    private async Task<GovernanceLifecycleProjectionDto> BuildGovernanceLifecycleProjectionAsync(
        string fundProfileId,
        IReadOnlyList<FundAccountSummary> accounts,
        ReconciliationSummary reconciliation,
        CancellationToken ct)
    {
        OperationsContinuityWorkflowSummaryDto? activeWorkflowSummary = null;
        OperationsContinuityWorkflowDto? activeWorkflow = null;
        IReadOnlyList<OperationsTimelineEntryDto> timeline = [];
        if (_operationsContinuityWorkflowService is not null)
        {
            var summaries = await _operationsContinuityWorkflowService
                .ListAsync(ct: ct)
                .ConfigureAwait(false);
            var accountIds = accounts
                .Select(static account => account.AccountId)
                .ToHashSet();
            var scopedSummaries = summaries
                .Where(summary => accountIds.Contains(summary.FundAccountId))
                .ToArray();

            activeWorkflowSummary = scopedSummaries
                .OrderByDescending(static item => item.UpdatedAtUtc)
                .FirstOrDefault();

            if (activeWorkflowSummary is not null)
            {
                activeWorkflow = await _operationsContinuityWorkflowService
                    .GetAsync(activeWorkflowSummary.WorkflowId, ct)
                    .ConfigureAwait(false);

                timeline = await _operationsContinuityWorkflowService
                    .GetTimelineAsync(activeWorkflowSummary.WorkflowId, ct)
                    .ConfigureAwait(false);
            }
        }

        FundReportPackHistoryItemDto? latestReportPack = null;
        if (_reportPackRepository is not null)
        {
            var history = await _reportPackRepository
                .GetHistoryAsync(fundProfileId, limit: 1, ct)
                .ConfigureAwait(false);
            latestReportPack = history.FirstOrDefault();
        }

        var evidenceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var traceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evidence in activeWorkflow?.EvidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(evidence.EvidenceId))
            {
                evidenceReferences.Add(evidence.EvidenceId);
            }

            if (!string.IsNullOrWhiteSpace(evidence.Route))
            {
                traceReferences.Add(evidence.Route);
            }
        }

        foreach (var entry in timeline)
        {
            traceReferences.Add(entry.AuditId.ToString("N"));
            traceReferences.Add(entry.CurrentHash);
            foreach (var reference in entry.References)
            {
                if (!string.IsNullOrWhiteSpace(reference.EvidenceId))
                {
                    evidenceReferences.Add(reference.EvidenceId);
                }
            }
        }

        foreach (var queueItem in reconciliation.BreakQueue?.Items ?? [])
        {
            if (!string.IsNullOrWhiteSpace(queueItem.EvidenceReference))
            {
                evidenceReferences.Add(queueItem.EvidenceReference);
            }

            if (!string.IsNullOrWhiteSpace(queueItem.WorkflowId))
            {
                traceReferences.Add(queueItem.WorkflowId);
            }
        }

        var decisionPosture = activeWorkflow?.ReconciliationState switch
        {
            OperationsReconciliationStateDto.ExceptionsOpen or OperationsReconciliationStateDto.InReview
                => "Reconciliation decisions are still open in the shared workflow queue.",
            OperationsReconciliationStateDto.Cleared or OperationsReconciliationStateDto.Complete
                => "Reconciliation decisions are cleared in shared lifecycle state.",
            _ when reconciliation.OpenBreakCount > 0
                => $"{reconciliation.OpenBreakCount} reconciliation break(s) still need shared decision records.",
            _ => "Reconciliation decision posture is aligned with shared Accounting state."
        };

        var signoffPosture = activeWorkflow?.ApprovalState switch
        {
            OperationsApprovalStateDto.Approved => "Sign-off is approved in shared continuity lifecycle.",
            OperationsApprovalStateDto.Rejected => "Sign-off is rejected; approval lifecycle requires remediation.",
            OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned
                => "Sign-off is submitted and awaiting reviewer decision in shared lifecycle.",
            _ => "Sign-off is pending shared approval evidence."
        };

        // This fund-wide query has no explicit account/book/entity/period close scope.
        // Only the shared command-center projection may attest readiness for a declared subject.
        const string closeReadiness = "Close readiness requires a scoped shared evaluation. Select the fund, book, account, entity, and period in the close command center.";

        var auditTraceability = timeline.Count > 0 || evidenceReferences.Count > 0
            ? $"Traceability is backed by {timeline.Count} lifecycle event(s) and {evidenceReferences.Count} evidence reference(s)."
            : "Traceability references are pending shared timeline and evidence responses.";

        return new GovernanceLifecycleProjectionDto(
            DecisionPosture: decisionPosture,
            SignoffPosture: signoffPosture,
            CloseReadiness: closeReadiness,
            AuditTraceability: auditTraceability,
            ActiveWorkflowId: activeWorkflowSummary?.WorkflowId.ToString(),
            WorkflowStatus: activeWorkflow?.Status ?? activeWorkflowSummary?.Status,
            ApprovalState: activeWorkflow?.ApprovalState,
            WorkflowUpdatedAtUtc: activeWorkflow?.UpdatedAtUtc ?? activeWorkflowSummary?.UpdatedAtUtc,
            TimelineEventCount: timeline.Count,
            EvidenceReferenceCount: evidenceReferences.Count,
            EvidenceReferences: evidenceReferences.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            AuditReferences: traceReferences.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            AccountingRecordSummary: activeWorkflow?.AccountingRecordSummary,
            EvidencePackages: activeWorkflow?.EvidencePackages,
            ReconciliationLanes: activeWorkflow?.ReconciliationLanes,
            BreakCases: activeWorkflow?.BreakCases,
            CloseChecklist: activeWorkflow?.CloseChecklist,
            Approvals: activeWorkflow?.Approvals,
            ReviewedAutomation: activeWorkflow?.ReviewedAutomation);
    }

    private static string NormalizeCutId(string value)
    {
        var normalized = new string(value
            .Where(static character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(static character => char.ToLowerInvariant(character))
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "unassigned" : normalized;
    }

    private static string ResolveDisplayName(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs)
        => runs
            .Select(static run => run.FundDisplayName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
            ?? fundProfileId;

    private static string ResolveCurrency(
        string? requestedCurrency,
        IReadOnlyList<FundAccountSummary> accounts)
        => !string.IsNullOrWhiteSpace(requestedCurrency)
            ? requestedCurrency.Trim().ToUpperInvariant()
            : accounts.Select(static account => account.BaseCurrency)
                .FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency))
            ?? "USD";

    private static Guid TranslateFundProfileId(string fundProfileId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId));
        return new Guid(bytes);
    }
}
