using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

public interface IReportingRunReadinessDependencyEvaluator
{
    Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default);
}

public sealed record ReportingRunReadinessOptions(
    bool AllowLegacyUnscopedDrafts = false,
    bool AllowDraftWhenDependencyEvaluatorIsUnavailable = false);

public sealed class ReportingRunReadinessDependencyEvaluator : IReportingRunReadinessDependencyEvaluator
{
    private readonly IServiceProvider _services;

    public ReportingRunReadinessDependencyEvaluator(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var checks = new List<ReportingRunReadinessCheckDto>(3);
        checks.Add(await EvaluateManualJournalsAsync(parameters, accessContext, ct).ConfigureAwait(false));
        checks.Add(await EvaluateAccountingReadinessAsync(parameters, accessContext, ct).ConfigureAwait(false));
        checks.Add(EvaluateDataset(request, template, parameters, accessContext));
        return checks;
    }

    private async Task<ReportingRunReadinessCheckDto> EvaluateManualJournalsAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return Unavailable(
                "manual-journals",
                "Journal posting completeness",
                "The manual-journal dependency is unavailable, so final-output readiness cannot be established.",
                "/workstation/accounting/journal-entries");
        }

        try
        {
            var workbench = await service.GetWorkbenchAsync(
                parameters.Scope.FundProfileId,
                parameters.LedgerBook.LedgerBookId,
                ct,
                companyId: accessContext?.CompanyId).ConfigureAwait(false);
            var unresolved = workbench.Drafts
                .Where(draft => string.Equals(draft.PeriodId, parameters.PeriodId, StringComparison.OrdinalIgnoreCase))
                .Where(static draft => draft.Status is not (
                    ManualJournalEntryStatusDto.Posted or
                    ManualJournalEntryStatusDto.Rejected or
                    ManualJournalEntryStatusDto.Reversed or
                    ManualJournalEntryStatusDto.CloseLocked))
                .ToArray();
            return unresolved.Length == 0
                ? Ready(
                    "manual-journals",
                    "Journal posting completeness",
                    $"No unresolved manual journals were found for period {parameters.PeriodId}.",
                    "/workstation/accounting/journal-entries",
                    [$"manual-journals:{parameters.PeriodId}:complete"])
                : new ReportingRunReadinessCheckDto(
                    "manual-journals",
                    "Journal posting completeness",
                    ReportingRunReadinessStatusDto.ReviewRequired,
                    $"{unresolved.Length} manual journal(s) remain unresolved for period {parameters.PeriodId}.",
                    unresolved.Length,
                    BlocksDraft: false,
                    BlocksFinal: true,
                    "/workstation/accounting/journal-entries",
                    unresolved.Select(static draft => $"journal:{draft.JournalEntryId:D}").ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unavailable(
                "manual-journals",
                "Journal posting completeness",
                $"Journal readiness could not be loaded: {ex.Message}",
                "/workstation/accounting/journal-entries");
        }
    }

    private async Task<ReportingRunReadinessCheckDto> EvaluateAccountingReadinessAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct)
    {
        var service = _services.GetService<AccountingProductionReadinessService>();
        if (service is null)
        {
            return Unavailable(
                "accounting-readiness",
                "Accounting and close dependencies",
                "The accounting-readiness dependency is unavailable, so final-output readiness cannot be established.",
                "/workstation/accounting/close");
        }

        try
        {
            var result = await service.AssessAsync(
                new AccountingProductionReadinessRequestDto(
                    parameters.Scope.FundProfileId,
                    parameters.LedgerBook.LedgerBookId,
                    MapAccountingBasis(parameters.AccountingBasis),
                    CompanyId: accessContext?.CompanyId),
                ct).ConfigureAwait(false);
            var status = result.Status switch
            {
                AccountingProductionReadinessStatusDto.Ready => ReportingRunReadinessStatusDto.Ready,
                AccountingProductionReadinessStatusDto.ReviewRequired => ReportingRunReadinessStatusDto.ReviewRequired,
                AccountingProductionReadinessStatusDto.Blocked => ReportingRunReadinessStatusDto.Blocked,
                _ => ReportingRunReadinessStatusDto.Unavailable
            };
            return new ReportingRunReadinessCheckDto(
                "accounting-readiness",
                "Accounting and close dependencies",
                status,
                $"Accounting production readiness is {result.Status} with score {result.Score} and {result.CriticalIssueCount} critical issue(s).",
                result.CriticalIssueCount + result.WarningIssueCount,
                BlocksDraft: false,
                BlocksFinal: true,
                "/workstation/accounting/close",
                result.Issues
                    .Select(static issue => $"accounting-readiness:{issue.Code}")
                    .Append($"accounting-readiness:{parameters.Scope.FundProfileId}:{parameters.PeriodId}:{result.Status}:{result.Score}")
                    .ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unavailable(
                "accounting-readiness",
                "Accounting and close dependencies",
                $"Accounting readiness could not be loaded: {ex.Message}",
                "/workstation/accounting/close");
        }
    }

    private ReportingRunReadinessCheckDto EvaluateDataset(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext)
    {
        if (template.ReportWriterGrids is not { Count: > 0 })
        {
            return Ready(
                "report-dataset",
                "Report dataset availability",
                "The selected template does not require a report-writer dataset.",
                "/workstation/reporting/data",
                [$"dataset:not-required:{template.TemplateId}"]);
        }

        if (request.DatasetRows is { Count: > 0 })
        {
            if (parameters.Finality == ReportingFinalityDto.Final)
            {
                return new ReportingRunReadinessCheckDto(
                    "report-dataset",
                    "Authoritative report dataset",
                    ReportingRunReadinessStatusDto.Blocked,
                    "Final reporting output cannot use caller-supplied rows; select a server-owned dataset source.",
                    1,
                    BlocksDraft: false,
                    BlocksFinal: true,
                    "/workstation/reporting/data",
                    [$"dataset:caller-supplied:{request.DatasetRows.Count}"]);
            }

            return Ready(
                "report-dataset",
                "Report dataset availability",
                $"The draft request retained {request.DatasetRows.Count} explicit, non-certifiable dataset row(s).",
                "/workstation/reporting/data",
                [$"dataset:request:{request.DatasetRows.Count}"]);
        }

        var service = _services.GetService<ReportWriterDatasetSourceService>();
        if (service is null)
        {
            return Unavailable(
                "report-dataset",
                "Report dataset availability",
                "The report-writer dataset dependency is unavailable.",
                "/workstation/reporting/data");
        }

        try
        {
            var rows = service.BuildDatasetRowsForSource(request.DatasetSourceId, accessContext);
            return rows.Count > 0
                ? Ready(
                    "report-dataset",
                    "Report dataset availability",
                    $"The selected dataset source resolved {rows.Count} row(s).",
                    "/workstation/reporting/data",
                    [$"dataset:{request.DatasetSourceId ?? "retained-reporting-rows"}:{rows.Count}"])
                : new ReportingRunReadinessCheckDto(
                    "report-dataset",
                    "Report dataset availability",
                    ReportingRunReadinessStatusDto.Blocked,
                    "The selected dataset source resolved no rows.",
                    1,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    "/workstation/reporting/data");
        }
        catch (Exception ex)
        {
            return Unavailable(
                "report-dataset",
                "Report dataset availability",
                $"The selected dataset source could not be resolved: {ex.Message}",
                "/workstation/reporting/data",
                blocksDraft: true);
        }
    }

    private static AccountingBasisKindDto MapAccountingBasis(ReportingAccountingBasisDto basis) => basis switch
    {
        ReportingAccountingBasisDto.Gaap => AccountingBasisKindDto.Gaap,
        ReportingAccountingBasisDto.Tax => AccountingBasisKindDto.Tax,
        ReportingAccountingBasisDto.Cash => AccountingBasisKindDto.Cash,
        ReportingAccountingBasisDto.Statutory => AccountingBasisKindDto.Statutory,
        _ => AccountingBasisKindDto.Primary
    };

    private static ReportingRunReadinessCheckDto Ready(
        string id,
        string label,
        string summary,
        string route,
        IReadOnlyList<string>? evidence = null) =>
        new(id, label, ReportingRunReadinessStatusDto.Ready, summary, 0, false, false, route, evidence);

    private static ReportingRunReadinessCheckDto Unavailable(
        string id,
        string label,
        string summary,
        string route,
        bool blocksDraft = true) =>
        new(id, label, ReportingRunReadinessStatusDto.Unavailable, summary, 1, blocksDraft, true, route);
}

public sealed class ReportingRunReadinessService
{
    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly IReportingRunReadinessDependencyEvaluator? _dependencyEvaluator;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ReportingRunReadinessOptions _options;

    public ReportingRunReadinessService(
        IReportingTemplateCatalog templateCatalog,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        IReportingRunReadinessDependencyEvaluator? dependencyEvaluator = null,
        Func<DateTimeOffset>? utcNow = null,
        ReportingRunReadinessOptions? options = null)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _governedTemplateCatalog = governedTemplateCatalog;
        _dependencyEvaluator = dependencyEvaluator;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _options = options ?? new ReportingRunReadinessOptions();
    }

    public async Task<ReportingRunReadinessDto> AssessAsync(
        ReportingRunRequestDto request,
        ReportAccessQueryContext? accessContext = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);

        var templateName = request.TemplateId.Trim();
        if (request.Template is not null &&
            !string.Equals(request.Template.Name, templateName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("template.name must match templateId.", nameof(request));
        }

        var template = request.Template is null
            ? _templateCatalog.Get(templateName)
            : _templateCatalog.Get(request.Template);
        var resolvedTemplate = request.Template ?? new VersionedReportTemplateIdDto(
            template.TemplateId,
            ParseMajorVersion(template.Version));
        EnsureAccess(resolvedTemplate, template, accessContext);

        var isLegacyRequest = request.Parameters is null;
        var parameters = NormalizeParameters(request, isLegacyRequest);
        var checks = BuildContractChecks(request, template, parameters, accessContext, isLegacyRequest).ToList();
        if (_dependencyEvaluator is null)
        {
            checks.Add(new ReportingRunReadinessCheckDto(
                "dependency-evaluator",
                "Server dependency evidence",
                ReportingRunReadinessStatusDto.Unavailable,
                "The server readiness dependency evaluator is unavailable.",
                1,
                BlocksDraft: !_options.AllowDraftWhenDependencyEvaluatorIsUnavailable,
                BlocksFinal: true));
        }
        else
        {
            checks.AddRange(await _dependencyEvaluator
                .EvaluateAsync(request, template, parameters, accessContext, ct)
                .ConfigureAwait(false));
        }

        var canGenerateDraft = checks.All(static check => !check.BlocksDraft || check.Status == ReportingRunReadinessStatusDto.Ready);
        var canGenerateFinal = checks.All(static check => !check.BlocksFinal || check.Status == ReportingRunReadinessStatusDto.Ready);
        var status = !canGenerateDraft || !canGenerateFinal
            ? ReportingRunReadinessStatusDto.Blocked
            : checks.Any(static check => check.Status == ReportingRunReadinessStatusDto.ReviewRequired)
                ? ReportingRunReadinessStatusDto.ReviewRequired
                : ReportingRunReadinessStatusDto.Ready;
        var blockingReasons = checks
            .Where(check =>
                (check.BlocksDraft && !canGenerateDraft || check.BlocksFinal && !canGenerateFinal) &&
                check.Status != ReportingRunReadinessStatusDto.Ready)
            .Select(static check => check.Summary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evaluatedAt = _utcNow();
        var evidenceHash = ComputeEvidenceHash(resolvedTemplate, parameters, checks);
        return new ReportingRunReadinessDto(
            $"report-readiness-{Guid.NewGuid():N}",
            evaluatedAt,
            resolvedTemplate,
            parameters,
            status,
            canGenerateDraft,
            canGenerateFinal,
            checks,
            blockingReasons,
            evidenceHash);
    }

    private void EnsureAccess(
        VersionedReportTemplateIdDto templateId,
        ReportingTemplateMetadata template,
        ReportAccessQueryContext? accessContext)
    {
        var evaluation = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext)
            ?? ReportAccessPolicyEvaluator.Evaluate(template.AccessPolicy, accessContext);
        if (!evaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(evaluation.Reason);
        }
    }

    private ReportingRunParametersDto NormalizeParameters(ReportingRunRequestDto request, bool isLegacyRequest)
    {
        var fallbackAsOf = request.AsOfDate ?? DateOnly.FromDateTime(_utcNow().UtcDateTime);
        if (isLegacyRequest)
        {
            return new ReportingRunParametersDto(
                new ReportingRunScopeDto("legacy-unscoped"),
                fallbackAsOf.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
                fallbackAsOf,
                new ReportingLedgerBookSelectionDto(LedgerBookCode: "legacy-default"),
                ReportingAccountingBasisDto.Gaap,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Draft,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true);
        }

        var parameters = request.Parameters!;
        var scope = parameters.Scope with
        {
            FundProfileId = parameters.Scope.FundProfileId?.Trim() ?? string.Empty,
            EntityId = NormalizeOptional(parameters.Scope.EntityId),
            PortfolioId = NormalizeOptional(parameters.Scope.PortfolioId),
            InvestorId = NormalizeOptional(parameters.Scope.InvestorId)
        };
        var ledgerBook = parameters.LedgerBook with
        {
            LedgerBookCode = NormalizeOptional(parameters.LedgerBook.LedgerBookCode)
        };
        var templateParameters = parameters.TemplateParameters
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
        return parameters with
        {
            Scope = scope,
            PeriodId = parameters.PeriodId?.Trim() ?? string.Empty,
            LedgerBook = ledgerBook,
            PresentationCurrency = parameters.PresentationCurrency?.Trim().ToUpperInvariant() ?? string.Empty,
            TemplateParameters = templateParameters
        };
    }

    private IEnumerable<ReportingRunReadinessCheckDto> BuildContractChecks(
        ReportingRunRequestDto request,
        ReportingTemplateMetadata template,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext? accessContext,
        bool isLegacyRequest)
    {
        yield return new ReportingRunReadinessCheckDto(
            "template-version",
            "Approved template version",
            ReportingRunReadinessStatusDto.Ready,
            $"Approved template {template.TemplateId} version {template.Version} resolved exactly.",
            0,
            BlocksDraft: false,
            BlocksFinal: false,
            EvidenceReferences: [$"template:{template.TemplateId}@{template.Version}"]);

        var requiresBoundTenantScope = accessContext?.RequireBoundScope == true;
        var hasBoundTenantScope = accessContext is not null
            && !string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            && !string.IsNullOrWhiteSpace(accessContext.TenantId)
            && !string.IsNullOrWhiteSpace(accessContext.CompanyId);
        yield return !requiresBoundTenantScope
            ? new ReportingRunReadinessCheckDto(
                "tenant-scope",
                "Authenticated tenant and company scope",
                ReportingRunReadinessStatusDto.Ready,
                "Compatibility execution is not using the tenant-scoped HTTP boundary; it cannot be governed for release.",
                0,
                false,
                false,
                EvidenceReferences: ["tenant-scope:compatibility-unbound"])
            : hasBoundTenantScope
            ? new ReportingRunReadinessCheckDto(
                "tenant-scope",
                "Authenticated tenant and company scope",
                ReportingRunReadinessStatusDto.Ready,
                "The server resolved a bound actor, tenant, and company scope for this run.",
                0,
                false,
                false,
                EvidenceReferences:
                [
                    $"tenant:{accessContext!.TenantId}",
                    $"company:{accessContext.CompanyId}",
                    $"actor:{accessContext.ActorPrincipalId}"
                ])
            : new ReportingRunReadinessCheckDto(
                "tenant-scope",
                "Authenticated tenant and company scope",
                ReportingRunReadinessStatusDto.Blocked,
                "A server-resolved actor, tenant, and company scope is required.",
                1,
                BlocksDraft: true,
                BlocksFinal: true);

        var scopeIssues = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters.Scope.FundProfileId) ||
            string.Equals(parameters.Scope.FundProfileId, "legacy-unscoped", StringComparison.OrdinalIgnoreCase))
        {
            scopeIssues.Add("fund profile");
        }

        if (parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Entity && string.IsNullOrWhiteSpace(parameters.Scope.EntityId))
        {
            scopeIssues.Add("entity id");
        }
        if (parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Portfolio && string.IsNullOrWhiteSpace(parameters.Scope.PortfolioId))
        {
            scopeIssues.Add("portfolio id");
        }
        if (parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Investor && string.IsNullOrWhiteSpace(parameters.Scope.InvestorId))
        {
            scopeIssues.Add("investor id");
        }
        if (string.IsNullOrWhiteSpace(parameters.PeriodId))
        {
            scopeIssues.Add("period");
        }
        if (parameters.LedgerBook.LedgerBookId is null && string.IsNullOrWhiteSpace(parameters.LedgerBook.LedgerBookCode))
        {
            scopeIssues.Add("ledger book");
        }
        if (string.IsNullOrWhiteSpace(parameters.PresentationCurrency))
        {
            scopeIssues.Add("presentation currency");
        }
        if (request.AsOfDate is not null && request.AsOfDate.Value != parameters.AsOfDate)
        {
            scopeIssues.Add("matching as-of date");
        }

        yield return scopeIssues.Count == 0
            ? new ReportingRunReadinessCheckDto(
                "run-scope",
                "Fund, entity, period, and ledger scope",
                ReportingRunReadinessStatusDto.Ready,
                "The requested report scope is complete and internally consistent.",
                0,
                false,
                false,
                EvidenceReferences:
                [
                    $"fund:{parameters.Scope.FundProfileId}",
                    $"period:{parameters.PeriodId}",
                    $"book:{parameters.LedgerBook.LedgerBookId?.ToString("D") ?? parameters.LedgerBook.LedgerBookCode}",
                    $"as-of:{parameters.AsOfDate:yyyy-MM-dd}"
                ])
            : new ReportingRunReadinessCheckDto(
                "run-scope",
                "Fund, entity, period, and ledger scope",
                isLegacyRequest && _options.AllowLegacyUnscopedDrafts
                    ? ReportingRunReadinessStatusDto.ReviewRequired
                    : ReportingRunReadinessStatusDto.Blocked,
                $"Required report scope is incomplete: {string.Join(", ", scopeIssues)}.",
                scopeIssues.Count,
                BlocksDraft: !isLegacyRequest || !_options.AllowLegacyUnscopedDrafts,
                BlocksFinal: true);

        var requiredParameters = template.Parameters?
            .Where(static parameter => parameter.Required)
            .Select(static parameter => parameter.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var missingParameters = requiredParameters
            .Where(name => !parameters.TemplateParameters.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
        yield return missingParameters.Length == 0
            ? new ReportingRunReadinessCheckDto(
                "template-parameters",
                "Required template parameters",
                ReportingRunReadinessStatusDto.Ready,
                requiredParameters.Length == 0
                    ? "The selected template defines no required parameters."
                    : $"All {requiredParameters.Length} required template parameter(s) were supplied.",
                0,
                false,
                false,
                EvidenceReferences: requiredParameters.Length == 0
                    ? ["template-parameters:none-required"]
                    : requiredParameters.Select(static name => $"template-parameter:{name}").ToArray())
            : new ReportingRunReadinessCheckDto(
                "template-parameters",
                "Required template parameters",
                ReportingRunReadinessStatusDto.Blocked,
                $"Required template parameter(s) are missing: {string.Join(", ", missingParameters)}.",
                missingParameters.Length,
                BlocksDraft: true,
                BlocksFinal: true);

        var evidenceAppendixReady = parameters.Finality != ReportingFinalityDto.Final
            || parameters.IncludeEvidenceAppendix;
        yield return evidenceAppendixReady
            ? new ReportingRunReadinessCheckDto(
                "evidence-appendix",
                "Release evidence appendix",
                ReportingRunReadinessStatusDto.Ready,
                parameters.IncludeEvidenceAppendix
                    ? "The report will retain its supporting evidence appendix."
                    : "Evidence appendix is optional for this draft run.",
                0,
                false,
                false,
                EvidenceReferences: [parameters.IncludeEvidenceAppendix ? "evidence-appendix:included" : "evidence-appendix:draft-optional"])
            : new ReportingRunReadinessCheckDto(
                "evidence-appendix",
                "Release evidence appendix",
                ReportingRunReadinessStatusDto.Blocked,
                "Final reporting output must include the supporting evidence appendix.",
                1,
                BlocksDraft: false,
                BlocksFinal: true);
    }

    private static int ParseMajorVersion(string version)
    {
        var token = version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return int.TryParse(token, out var parsed) && parsed > 0 ? parsed : 1;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string ComputeEvidenceHash(
        VersionedReportTemplateIdDto template,
        ReportingRunParametersDto parameters,
        IReadOnlyList<ReportingRunReadinessCheckDto> checks)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            template,
            parameters,
            checks = checks.Select(static check => new
            {
                check.CheckId,
                check.Status,
                check.IssueCount,
                check.BlocksDraft,
                check.BlocksFinal,
                check.EvidenceReferences
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
