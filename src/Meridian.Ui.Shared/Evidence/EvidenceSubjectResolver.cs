using Meridian.Contracts.Workstation;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Strategies.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Evidence;

public sealed class EvidenceSubjectResolver
{
    public const string StrategyRunKind = "strategy-run";
    public const string PaperReadinessKind = "paper-readiness";
    public const string ReconciliationReviewKind = "reconciliation-review";
    public const string StatementRunKind = "statement-run";
    public const string ReportPackKind = "report-pack";
    public const string ProviderTrustKind = "provider-trust";
    public const string AnalysisExportKind = "analysis-export";
    public const string SecurityMasterConflictKind = "security-master-conflict";
    public const string ApprovalKind = "approval";
    public const string AccountingRecordKind = "accounting-record";
    public const string PrivateCapitalFundEventKind = "private-capital-fund-event";
    public const string EvidenceVaultKind = "evidence-vault";

    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        StrategyRunKind,
        PaperReadinessKind,
        ReconciliationReviewKind,
        StatementRunKind,
        ReportPackKind,
        ProviderTrustKind,
        AnalysisExportKind,
        SecurityMasterConflictKind,
        ApprovalKind,
        AccountingRecordKind,
        PrivateCapitalFundEventKind,
        EvidenceVaultKind
    };

    private readonly IServiceProvider _services;

    public EvidenceSubjectResolver(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public bool IsSupportedKind(string? subjectKind)
        => !string.IsNullOrWhiteSpace(subjectKind) && SupportedKinds.Contains(subjectKind);

    public async Task<IReadOnlyList<EvidenceSubjectDto>> ListAsync(CancellationToken ct = default)
    {
        var subjects = new List<EvidenceSubjectDto>
        {
            new(
                SubjectId: "current",
                SubjectKind: PaperReadinessKind,
                Label: "Current paper trading readiness",
                Workspace: "Trading",
                Route: "/trading/readiness",
                PageTag: "TradingReadiness"),
            new(
                SubjectId: "current",
                SubjectKind: ReportPackKind,
                Label: "Current report-pack output",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            new(
                SubjectId: "dk1",
                SubjectKind: ProviderTrustKind,
                Label: "DK1 provider trust gate",
                Workspace: "Data",
                Route: "/data",
                PageTag: "ProviderHealth"),
            new(
                SubjectId: "open",
                SubjectKind: SecurityMasterConflictKind,
                Label: "Open Security Master conflicts",
                Workspace: "Data",
                Route: "/data/security-master?view=conflicts",
                PageTag: "SecurityMaster"),
            new(
                SubjectId: "current",
                SubjectKind: ApprovalKind,
                Label: "Current operations approval",
                Workspace: "Accounting",
                Route: "/accounting",
                PageTag: "OperationsContinuity"),
            new(
                SubjectId: "current",
                SubjectKind: AccountingRecordKind,
                Label: "Current accounting record",
                Workspace: "Accounting",
                Route: "/accounting",
                PageTag: "OperationsContinuity"),
            new(
                SubjectId: "lookup",
                SubjectKind: EvidenceVaultKind,
                Label: "Retained evidence vault lookup",
                Workspace: "Reporting",
                Route: "/reporting/evidence?view=vault",
                PageTag: "EvidenceWorkbench")
        };

        var runService = _services.GetService<StrategyRunReadService>();
        if (runService is not null)
        {
            var runs = await runService.GetRunsAsync(ct: ct).ConfigureAwait(false);
            subjects.AddRange(runs.Take(100).Select(run => new EvidenceSubjectDto(
                SubjectId: run.RunId,
                SubjectKind: StrategyRunKind,
                Label: $"{run.StrategyName} {run.Mode} run",
                Workspace: ResolveWorkspace(run.Mode),
                Route: $"/strategy?runId={Uri.EscapeDataString(run.RunId)}",
                PageTag: "StrategyRuns")));
        }

        var operationsService = _services.GetService<IOperationsContinuityWorkflowService>();
        if (operationsService is not null)
        {
            var workflows = await operationsService.ListAsync(ct: ct).ConfigureAwait(false);
            subjects.AddRange(workflows
                .OrderByDescending(static workflow => workflow.UpdatedAtUtc)
                .Take(25)
                .SelectMany(static workflow => new[]
                {
                    new EvidenceSubjectDto(
                        SubjectId: workflow.WorkflowId.ToString("D"),
                        SubjectKind: ApprovalKind,
                        Label: $"Operations approval {workflow.PeriodId}",
                        Workspace: "Accounting",
                        Route: $"/accounting?workflowId={workflow.WorkflowId:D}",
                        PageTag: "OperationsContinuity"),
                    new EvidenceSubjectDto(
                        SubjectId: workflow.WorkflowId.ToString("D"),
                        SubjectKind: AccountingRecordKind,
                        Label: $"Accounting record {workflow.PeriodId}",
                        Workspace: "Accounting",
                        Route: $"/accounting?workflowId={workflow.WorkflowId:D}",
                        PageTag: "OperationsContinuity")
                }));
        }

        var manualJournalService = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (manualJournalService is not null)
        {
            var fundProfileIds = await manualJournalService.ListFundProfileIdsAsync(ct).ConfigureAwait(false);
            var privateCapitalActivities = fundProfileIds.Count == 0
                ? [await manualJournalService.GetPrivateCapitalActivityAsync(ct: ct).ConfigureAwait(false)]
                : await Task.WhenAll(fundProfileIds.Select(fundProfileId =>
                    manualJournalService.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId: null, ct))).ConfigureAwait(false);
            subjects.AddRange(privateCapitalActivities
                .SelectMany(static activity => activity.FundEventRecords)
                .OrderByDescending(static record => record.EffectiveDate)
                .ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(static record => new EvidenceSubjectDto(
                    SubjectId: record.FundEventId,
                    SubjectKind: PrivateCapitalFundEventKind,
                    Label: $"Private-capital fund event {record.FundEventType}",
                    Workspace: "Accounting",
                    Route: $"/accounting/journal-entries?fundEventId={Uri.EscapeDataString(record.FundEventId)}",
                    PageTag: "AccountingJournalEntries")));
        }

        return subjects
            .OrderBy(static subject => subject.Workspace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static subject => subject.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<EvidenceSubjectDto?> ResolveAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
    {
        if (!IsSupportedKind(subjectKind) || string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        if (string.Equals(subjectKind, StrategyRunKind, StringComparison.OrdinalIgnoreCase))
        {
            var runService = _services.GetService<StrategyRunReadService>();
            if (runService is null)
            {
                return null;
            }

            var run = await runService.GetRunDetailAsync(subjectId, ct).ConfigureAwait(false);
            return run is null
                ? null
                : new EvidenceSubjectDto(
                    SubjectId: run.Summary.RunId,
                    SubjectKind: StrategyRunKind,
                    Label: $"{run.Summary.StrategyName} {run.Summary.Mode} run",
                    Workspace: ResolveWorkspace(run.Summary.Mode),
                    Route: $"/strategy?runId={Uri.EscapeDataString(run.Summary.RunId)}",
                    PageTag: "StrategyRuns");
        }

        if (string.Equals(subjectKind, PrivateCapitalFundEventKind, StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePrivateCapitalFundEventSubjectAsync(subjectId, ct).ConfigureAwait(false);
        }

        return subjectKind.ToLowerInvariant() switch
        {
            PaperReadinessKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: PaperReadinessKind,
                Label: "Current paper trading readiness",
                Workspace: "Trading",
                Route: "/trading/readiness",
                PageTag: "TradingReadiness"),
            ReconciliationReviewKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ReconciliationReviewKind,
                Label: $"Reconciliation review {subjectId}",
                Workspace: "Accounting",
                Route: "/accounting",
                PageTag: "FundReconciliation"),
            StatementRunKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: StatementRunKind,
                Label: $"Statement run {subjectId}",
                Workspace: "Accounting",
                Route: $"/accounting?statementRunId={Uri.EscapeDataString(subjectId)}",
                PageTag: "FundReconciliation"),
            ReportPackKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ReportPackKind,
                Label: "Report-pack output",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            ProviderTrustKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: ProviderTrustKind,
                Label: "Provider trust gate",
                Workspace: "Data",
                Route: "/data",
                PageTag: "ProviderHealth"),
            AnalysisExportKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: AnalysisExportKind,
                Label: $"Analysis export {subjectId}",
                Workspace: "Reporting",
                Route: "/reporting",
                PageTag: "ReportingShell"),
            SecurityMasterConflictKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: SecurityMasterConflictKind,
                Label: string.Equals(subjectId, "open", StringComparison.OrdinalIgnoreCase)
                    ? "Open Security Master conflicts"
                    : $"Security Master conflict {subjectId}",
                Workspace: "Data",
                Route: "/data/security-master?view=conflicts",
                PageTag: "SecurityMaster"),
            ApprovalKind => await ResolveApprovalSubjectAsync(subjectId, ct).ConfigureAwait(false),
            AccountingRecordKind => await ResolveAccountingRecordSubjectAsync(subjectId, ct).ConfigureAwait(false),
            EvidenceVaultKind => new EvidenceSubjectDto(
                SubjectId: subjectId,
                SubjectKind: EvidenceVaultKind,
                Label: string.Equals(subjectId, "lookup", StringComparison.OrdinalIgnoreCase)
                    ? "Retained evidence vault lookup"
                    : $"Retained evidence vault {subjectId}",
                Workspace: "Reporting",
                Route: $"/reporting/evidence?vaultId={Uri.EscapeDataString(subjectId)}",
                PageTag: "EvidenceWorkbench"),
            _ => null
        };
    }

    private async Task<EvidenceSubjectDto?> ResolvePrivateCapitalFundEventSubjectAsync(string subjectId, CancellationToken ct)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return null;
        }

        var fundProfileId = TryResolveFundProfileIdFromFundEventId(subjectId);
        var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId: null, ct).ConfigureAwait(false);
        var record = activity.FundEventRecords.FirstOrDefault(item =>
            string.Equals(item.FundEventId, subjectId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return null;
        }

        return new EvidenceSubjectDto(
            SubjectId: record.FundEventId,
            SubjectKind: PrivateCapitalFundEventKind,
            Label: $"Private-capital fund event {record.FundEventType}",
            Workspace: "Accounting",
            Route: $"/accounting/journal-entries?fundEventId={Uri.EscapeDataString(record.FundEventId)}",
            PageTag: "AccountingJournalEntries");
    }

    private async Task<EvidenceSubjectDto?> ResolveApprovalSubjectAsync(string subjectId, CancellationToken ct)
    {
        var service = _services.GetService<IOperationsContinuityWorkflowService>();
        if (service is not null && Guid.TryParse(subjectId, out var workflowId))
        {
            var workflow = await service.GetAsync(workflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            return new EvidenceSubjectDto(
                SubjectId: workflow.WorkflowId.ToString("D"),
                SubjectKind: ApprovalKind,
                Label: $"Operations approval {workflow.PeriodId}",
                Workspace: "Accounting",
                Route: $"/accounting?workflowId={workflow.WorkflowId:D}",
                PageTag: "OperationsContinuity");
        }

        return new EvidenceSubjectDto(
            SubjectId: subjectId,
            SubjectKind: ApprovalKind,
            Label: string.Equals(subjectId, "current", StringComparison.OrdinalIgnoreCase)
                ? "Current operations approval"
                : $"Operations approval {subjectId}",
            Workspace: "Accounting",
            Route: "/accounting",
            PageTag: "OperationsContinuity");
    }

    private async Task<EvidenceSubjectDto?> ResolveAccountingRecordSubjectAsync(string subjectId, CancellationToken ct)
    {
        var service = _services.GetService<IOperationsContinuityWorkflowService>();
        if (service is not null && Guid.TryParse(subjectId, out var workflowId))
        {
            var workflow = await service.GetAsync(workflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            return new EvidenceSubjectDto(
                SubjectId: workflow.WorkflowId.ToString("D"),
                SubjectKind: AccountingRecordKind,
                Label: $"Accounting record {workflow.PeriodId}",
                Workspace: "Accounting",
                Route: $"/accounting?workflowId={workflow.WorkflowId:D}",
                PageTag: "OperationsContinuity");
        }

        return new EvidenceSubjectDto(
            SubjectId: subjectId,
            SubjectKind: AccountingRecordKind,
            Label: string.Equals(subjectId, "current", StringComparison.OrdinalIgnoreCase)
                ? "Current accounting record"
                : $"Accounting record {subjectId}",
            Workspace: "Accounting",
            Route: "/accounting",
            PageTag: "OperationsContinuity");
    }

    private static string ResolveWorkspace(StrategyRunMode mode)
        => mode is StrategyRunMode.Paper or StrategyRunMode.Live ? "Trading" : "Strategy";

    private static string? TryResolveFundProfileIdFromFundEventId(string fundEventId)
    {
        if (string.IsNullOrWhiteSpace(fundEventId))
        {
            return null;
        }

        var parts = fundEventId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
               string.Equals(parts[0], "fund-event", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }
}
