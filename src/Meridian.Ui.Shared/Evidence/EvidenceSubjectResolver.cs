using Meridian.Contracts.Workstation;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
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
    public const string PaymentIntentKind = "payment-intent";
    public const string ReportPackDeliveryKind = "report-pack-delivery";
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
        PaymentIntentKind,
        ReportPackDeliveryKind,
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
            var scope = ResolveStrategyRunReadScope();
            var runs = scope is null
                ? await runService.GetRunsAsync(ct: ct).ConfigureAwait(false)
                : await runService.GetRunsAsync(
                    strategyId: null,
                    runType: null,
                    scope: scope,
                    ct: ct).ConfigureAwait(false);
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
                        Route: BuildOperationsContinuityRoute(workflow.WorkflowId, workflow.LedgerBookId),
                        PageTag: "OperationsContinuity",
                        LedgerBookId: workflow.LedgerBookId),
                    new EvidenceSubjectDto(
                        SubjectId: workflow.WorkflowId.ToString("D"),
                        SubjectKind: AccountingRecordKind,
                        Label: $"Accounting record {workflow.PeriodId}",
                        Workspace: "Accounting",
                        Route: BuildOperationsContinuityRoute(workflow.WorkflowId, workflow.LedgerBookId),
                        PageTag: "OperationsContinuity",
                        LedgerBookId: workflow.LedgerBookId)
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
                .SelectMany(static activity => activity.FundEventRecords.Select(record => (activity, record)))
                .OrderByDescending(static item => item.record.EffectiveDate)
                .ThenBy(static item => item.record.FundEventId, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(static item => new EvidenceSubjectDto(
                    SubjectId: item.record.FundEventId,
                    SubjectKind: PrivateCapitalFundEventKind,
                    Label: $"Private-capital fund event {item.record.FundEventType}",
                    Workspace: "Accounting",
                    Route: BuildAccountingJournalEntriesRoute("fundEventId", item.record.FundEventId, item.activity.LedgerBookId),
                    PageTag: "AccountingJournalEntries",
                    LedgerBookId: item.activity.LedgerBookId)));
            subjects.AddRange(privateCapitalActivities
                .SelectMany(static activity => activity.PaymentIntents)
                .OrderByDescending(static workflow => workflow.ExpectedCashMovement.EffectiveDate)
                .ThenBy(static workflow => workflow.PaymentIntentId, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(static workflow => new EvidenceSubjectDto(
                    SubjectId: workflow.PaymentIntentId,
                    SubjectKind: PaymentIntentKind,
                    Label: $"Payment intent {workflow.StatusLabel}",
                    Workspace: "Accounting",
                    Route: BuildAccountingJournalEntriesRoute("paymentIntentId", workflow.PaymentIntentId, workflow.LedgerBookId),
                    PageTag: "AccountingJournalEntries",
                    LedgerBookId: workflow.LedgerBookId)));
        }

        var deliveryAttempts = ListReportPackDeliveryAttempts(100);
        if (deliveryAttempts.Count > 0)
        {
            subjects.AddRange(deliveryAttempts
                .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
                .ThenBy(static attempt => attempt.Recipient, StringComparer.OrdinalIgnoreCase)
                .Select(static attempt => new EvidenceSubjectDto(
                    SubjectId: BuildReportPackDeliverySubjectId(attempt),
                    SubjectKind: ReportPackDeliveryKind,
                    Label: $"Report-pack delivery {attempt.Recipient} {attempt.AttemptNumber}",
                    Workspace: "Reporting",
                    Route: BuildReportPackDeliveryRoute(attempt),
                    PageTag: "EvidenceWorkbench")));
        }

        return subjects
            .OrderBy(static subject => subject.Workspace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static subject => subject.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<EvidenceSubjectDto?> ResolveAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default,
        Guid? ledgerBookId = null)
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

            var scope = ResolveStrategyRunReadScope();
            var run = scope is null
                ? await runService.GetRunDetailAsync(subjectId, ct).ConfigureAwait(false)
                : await runService.GetRunDetailAsync(subjectId, scope, ct).ConfigureAwait(false);
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
            return await ResolvePrivateCapitalFundEventSubjectAsync(subjectId, ledgerBookId, ct).ConfigureAwait(false);
        }

        if (string.Equals(subjectKind, PaymentIntentKind, StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePaymentIntentSubjectAsync(subjectId, ledgerBookId, ct).ConfigureAwait(false);
        }

        if (string.Equals(subjectKind, ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveReportPackDeliverySubject(subjectId);
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
            ApprovalKind => await ResolveApprovalSubjectAsync(subjectId, ledgerBookId, ct).ConfigureAwait(false),
            AccountingRecordKind => await ResolveAccountingRecordSubjectAsync(subjectId, ledgerBookId, ct).ConfigureAwait(false),
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

    private StrategyRunReadScope? ResolveStrategyRunReadScope()
    {
        var httpContext = _services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var trustedScope = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        return string.IsNullOrWhiteSpace(trustedScope.TenantId)
            || string.IsNullOrWhiteSpace(trustedScope.CompanyId)
                ? null
                : new StrategyRunReadScope(trustedScope.TenantId, trustedScope.CompanyId);
    }

    private EvidenceSubjectDto? ResolveReportPackDeliverySubject(string subjectId)
    {
        var attempt = ResolveReportPackDeliveryAttempt(ListReportPackDeliveryAttempts(500), subjectId);
        return attempt is null
            ? null
            : new EvidenceSubjectDto(
                SubjectId: BuildReportPackDeliverySubjectId(attempt),
                SubjectKind: ReportPackDeliveryKind,
                Label: $"Report-pack delivery {attempt.Recipient} {attempt.AttemptNumber}",
                Workspace: "Reporting",
                Route: BuildReportPackDeliveryRoute(attempt),
                PageTag: "EvidenceWorkbench");
    }

    private async Task<EvidenceSubjectDto?> ResolvePrivateCapitalFundEventSubjectAsync(string subjectId, Guid? ledgerBookId, CancellationToken ct)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return null;
        }

        var canonicalSubjectId = StripQueryScope(subjectId);
        var fundProfileId = TryResolveFundProfileIdFromFundEventId(canonicalSubjectId);
        ledgerBookId ??= TryResolveLedgerBookId(subjectId);
        var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var record = activity.FundEventRecords.FirstOrDefault(item =>
            string.Equals(item.FundEventId, canonicalSubjectId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return null;
        }

        return new EvidenceSubjectDto(
            SubjectId: record.FundEventId,
            SubjectKind: PrivateCapitalFundEventKind,
            Label: $"Private-capital fund event {record.FundEventType}",
            Workspace: "Accounting",
            Route: BuildAccountingJournalEntriesRoute("fundEventId", record.FundEventId, activity.LedgerBookId),
            PageTag: "AccountingJournalEntries",
            LedgerBookId: activity.LedgerBookId);
    }

    private async Task<EvidenceSubjectDto?> ResolvePaymentIntentSubjectAsync(string subjectId, Guid? ledgerBookId, CancellationToken ct)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return null;
        }

        var canonicalSubjectId = StripQueryScope(subjectId);
        ledgerBookId ??= TryResolveLedgerBookId(subjectId);
        if (ledgerBookId.HasValue)
        {
            var fundProfileId = TryResolveFundProfileIdFromPaymentIntentId(canonicalSubjectId);
            var scopedActivity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
            var scopedWorkflow = scopedActivity.PaymentIntents
                .FirstOrDefault(item => string.Equals(item.PaymentIntentId, canonicalSubjectId, StringComparison.OrdinalIgnoreCase));
            if (scopedWorkflow is not null)
            {
                return new EvidenceSubjectDto(
                    SubjectId: scopedWorkflow.PaymentIntentId,
                    SubjectKind: PaymentIntentKind,
                    Label: $"Payment intent {scopedWorkflow.StatusLabel}",
                    Workspace: "Accounting",
                    Route: BuildAccountingJournalEntriesRoute("paymentIntentId", scopedWorkflow.PaymentIntentId, scopedWorkflow.LedgerBookId),
                    PageTag: "AccountingJournalEntries",
                    LedgerBookId: scopedWorkflow.LedgerBookId);
            }
        }

        var fundProfileIds = await service.ListFundProfileIdsAsync(ct).ConfigureAwait(false);
        var activities = fundProfileIds.Count == 0
            ? [await service.GetPrivateCapitalActivityAsync(ct: ct).ConfigureAwait(false)]
            : await Task.WhenAll(fundProfileIds.Select(fundProfileId =>
                service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId: null, ct))).ConfigureAwait(false);
        var workflow = activities
            .SelectMany(static activity => activity.PaymentIntents)
            .FirstOrDefault(item => string.Equals(item.PaymentIntentId, canonicalSubjectId, StringComparison.OrdinalIgnoreCase));
        if (workflow is null)
        {
            return null;
        }

        return new EvidenceSubjectDto(
            SubjectId: workflow.PaymentIntentId,
            SubjectKind: PaymentIntentKind,
            Label: $"Payment intent {workflow.StatusLabel}",
            Workspace: "Accounting",
            Route: BuildAccountingJournalEntriesRoute("paymentIntentId", workflow.PaymentIntentId, workflow.LedgerBookId),
            PageTag: "AccountingJournalEntries",
            LedgerBookId: workflow.LedgerBookId);
    }

    private static string BuildAccountingJournalEntriesRoute(string key, string value, Guid? ledgerBookId)
    {
        var query = new List<string>
        {
            $"{key}={Uri.EscapeDataString(value)}"
        };

        if (ledgerBookId.HasValue)
        {
            query.Add($"ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString("D"))}");
        }

        return $"/accounting/journal-entries?{string.Join("&", query)}";
    }

    private static string BuildOperationsContinuityRoute(Guid workflowId, Guid? ledgerBookId)
    {
        var query = new List<string>
        {
            $"workflowId={workflowId:D}"
        };

        if (ledgerBookId.HasValue)
        {
            query.Add($"ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString("D"))}");
        }

        return $"/accounting?{string.Join("&", query)}";
    }

    private static string StripQueryScope(string subjectId)
    {
        var queryStart = subjectId.IndexOf('?', StringComparison.Ordinal);
        return queryStart < 0
            ? subjectId.Trim()
            : subjectId[..queryStart].Trim();
    }

    private static Guid? TryResolveLedgerBookId(string subjectId)
    {
        var queryStart = subjectId.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == subjectId.Length - 1)
        {
            return null;
        }

        var query = subjectId[(queryStart + 1)..];
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 &&
                string.Equals(Uri.UnescapeDataString(parts[0]), "ledgerBookId", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(Uri.UnescapeDataString(parts[1]), out var ledgerBookId))
            {
                return ledgerBookId;
            }
        }

        return null;
    }

    private async Task<EvidenceSubjectDto?> ResolveApprovalSubjectAsync(string subjectId, Guid? ledgerBookId, CancellationToken ct)
    {
        var service = _services.GetService<IOperationsContinuityWorkflowService>();
        ledgerBookId ??= TryResolveLedgerBookId(subjectId);
        var canonicalSubjectId = StripQueryScope(subjectId);
        if (service is not null && Guid.TryParse(canonicalSubjectId, out var workflowId))
        {
            var workflow = await service.GetAsync(workflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            if (ledgerBookId.HasValue && workflow.LedgerBookId != ledgerBookId.Value)
            {
                return null;
            }

            return new EvidenceSubjectDto(
                SubjectId: workflow.WorkflowId.ToString("D"),
                SubjectKind: ApprovalKind,
                Label: $"Operations approval {workflow.PeriodId}",
                Workspace: "Accounting",
                Route: BuildOperationsContinuityRoute(workflow.WorkflowId, workflow.LedgerBookId),
                PageTag: "OperationsContinuity",
                LedgerBookId: workflow.LedgerBookId);
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

    private async Task<EvidenceSubjectDto?> ResolveAccountingRecordSubjectAsync(string subjectId, Guid? ledgerBookId, CancellationToken ct)
    {
        var service = _services.GetService<IOperationsContinuityWorkflowService>();
        ledgerBookId ??= TryResolveLedgerBookId(subjectId);
        var canonicalSubjectId = StripQueryScope(subjectId);
        if (service is not null && Guid.TryParse(canonicalSubjectId, out var workflowId))
        {
            var workflow = await service.GetAsync(workflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            if (ledgerBookId.HasValue && workflow.LedgerBookId != ledgerBookId.Value)
            {
                return null;
            }

            return new EvidenceSubjectDto(
                SubjectId: workflow.WorkflowId.ToString("D"),
                SubjectKind: AccountingRecordKind,
                Label: $"Accounting record {workflow.PeriodId}",
                Workspace: "Accounting",
                Route: BuildOperationsContinuityRoute(workflow.WorkflowId, workflow.LedgerBookId),
                PageTag: "OperationsContinuity",
                LedgerBookId: workflow.LedgerBookId);
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

    private IReadOnlyList<ReportPackDeliveryAttemptDto> ListReportPackDeliveryAttempts(int limit)
        => ReportingDeliveryReadModelSecurity.ListVisibleAttempts(_services, limit).Attempts;

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

    private static string? TryResolveFundProfileIdFromPaymentIntentId(string paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        var parts = paymentIntentId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
               string.Equals(parts[0], "payment", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static ReportPackDeliveryAttemptDto? ResolveReportPackDeliveryAttempt(
        IReadOnlyList<ReportPackDeliveryAttemptDto> attempts,
        string subjectId)
    {
        var normalized = subjectId.Trim();
        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            Guid.TryParse(parts[0], out var reportId) &&
            Guid.TryParse(parts[1], out var attemptId))
        {
            return attempts.FirstOrDefault(attempt =>
                attempt.ReportId == reportId &&
                attempt.AttemptId == attemptId);
        }

        if (Guid.TryParse(normalized, out var attemptOnlyId))
        {
            return attempts.FirstOrDefault(attempt => attempt.AttemptId == attemptOnlyId);
        }

        return attempts.FirstOrDefault(attempt =>
            attempt.Package is not null &&
            string.Equals(attempt.Package.PackageId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildReportPackDeliverySubjectId(ReportPackDeliveryAttemptDto attempt)
        => $"{attempt.ReportId:D}:{attempt.AttemptId:D}";

    private static string BuildReportPackDeliveryRoute(ReportPackDeliveryAttemptDto attempt)
        => $"/reporting/report-packs?reportId={Uri.EscapeDataString(attempt.ReportId.ToString("D"))}&deliveryAttemptId={Uri.EscapeDataString(attempt.AttemptId.ToString("D"))}";
}
