using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;

namespace Meridian.Application.SecurityMaster.Validation;

public sealed class SecurityValidationGateService : ISecurityValidationGateService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ISecurityValidationService _validationService;
    private readonly ISecurityValidationSnapshotStore _snapshotStore;

    public SecurityValidationGateService(
        ISecurityMasterQueryService securityMaster,
        ISecurityValidationService validationService,
        ISecurityValidationSnapshotStore snapshotStore)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
        string symbol,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ct.ThrowIfCancellationRequested();

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var detail = await _securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, normalizedSymbol, provider: null, ct)
            .ConfigureAwait(false);

        if (detail is null)
        {
            var report = BuildUnresolvedSymbolReport(normalizedSymbol);
            var snapshot = persistSnapshot
                ? await RecordSnapshotAsync(report, workflow, workflowReference, actor, normalizedSymbol, ct).ConfigureAwait(false)
                : null;

            return new SecurityValidationGateResultDto(
                Workflow: workflow,
                Symbol: normalizedSymbol,
                SecurityId: null,
                IsResolved: false,
                IsBlocked: true,
                Report: report,
                Snapshot: snapshot);
        }

        return await ValidateSecurityAsync(
                detail.SecurityId,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                normalizedSymbol,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
        Guid securityId,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        string? symbol = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var report = await _validationService.ValidateSecurityAsync(securityId, ct).ConfigureAwait(false);
        var snapshot = persistSnapshot
            ? await RecordSnapshotAsync(report, workflow, workflowReference, actor, symbol, ct).ConfigureAwait(false)
            : null;

        return new SecurityValidationGateResultDto(
            Workflow: workflow,
            Symbol: string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant(),
            SecurityId: report.SecurityId ?? securityId,
            IsResolved: report.SecurityId.HasValue,
            IsBlocked: report.HasBlockingIssues,
            Report: report,
            Snapshot: snapshot);
    }

    private Task<SecurityValidationSnapshotDto> RecordSnapshotAsync(
        SecurityValidationReportDto report,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference,
        string? actor,
        string? symbol,
        CancellationToken ct)
    {
        var request = new SecurityValidationSnapshotRequestDto(
            Workflow: workflow,
            WorkflowReference: workflowReference,
            Actor: actor,
            Reason: BuildSnapshotReason(workflow, symbol),
            EvidenceLinks: BuildEvidenceLinks(workflow, workflowReference, symbol));

        return _snapshotStore.RecordAsync(report, request, ct);
    }

    private static string BuildSnapshotReason(SecurityValidationWorkflowDto workflow, string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? $"{workflow} validation gate evaluated a Security Master record."
            : $"{workflow} validation gate evaluated Security Master identity for {symbol.Trim().ToUpperInvariant()}.";

    private static IReadOnlyList<SecurityEvidenceLinkDto> BuildEvidenceLinks(
        SecurityValidationWorkflowDto workflow,
        string? workflowReference,
        string? symbol)
    {
        if (string.IsNullOrWhiteSpace(workflowReference))
        {
            return [];
        }

        var summary = string.IsNullOrWhiteSpace(symbol)
            ? $"{workflow} workflow {workflowReference}"
            : $"{workflow} workflow {workflowReference} for {symbol.Trim().ToUpperInvariant()}";

        return
        [
            new SecurityEvidenceLinkDto(
                EvidenceKind: workflow.ToString(),
                EvidenceId: workflowReference,
                Route: null,
                Summary: summary)
        ];
    }

    private static SecurityValidationReportDto BuildUnresolvedSymbolReport(string normalizedSymbol)
    {
        var issue = new SecurityValidationIssueDto(
            Severity: SecurityValidationSeverityDto.Error,
            Code: "SM_SYMBOL_UNRESOLVED",
            Title: "Security Master identity is unresolved",
            Message: $"Ticker '{normalizedSymbol}' could not be resolved to a Security Master record.",
            AffectedFields: ["symbol", "identifiers.Ticker"],
            SuggestedAction: "Create or map the Security Master record before the downstream workflow consumes this symbol.",
            EvidenceLinks: []);

        return new SecurityValidationReportDto(
            SecurityId: null,
            Scope: "SecurityMasterResolution",
            EvaluatedAtUtc: DateTimeOffset.UtcNow,
            HasBlockingIssues: true,
            CriticalIssueCount: 0,
            ErrorIssueCount: 1,
            Issues: [issue]);
    }
}

public sealed class NullSecurityValidationGateService : ISecurityValidationGateService
{
    public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
        string symbol,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        CancellationToken ct = default)
        => Task.FromResult(BuildUnavailableResult(workflow, symbol.Trim().ToUpperInvariant(), null));

    public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
        Guid securityId,
        SecurityValidationWorkflowDto workflow,
        string? workflowReference = null,
        string? actor = null,
        bool persistSnapshot = false,
        string? symbol = null,
        CancellationToken ct = default)
        => Task.FromResult(BuildUnavailableResult(workflow, symbol, securityId));

    private static SecurityValidationGateResultDto BuildUnavailableResult(
        SecurityValidationWorkflowDto workflow,
        string? symbol,
        Guid? securityId)
    {
        var report = new SecurityValidationReportDto(
            SecurityId: securityId,
            Scope: securityId.HasValue ? "Security" : "SecurityMasterResolution",
            EvaluatedAtUtc: DateTimeOffset.UtcNow,
            HasBlockingIssues: true,
            CriticalIssueCount: 1,
            ErrorIssueCount: 0,
            Issues:
            [
                new SecurityValidationIssueDto(
                    SecurityValidationSeverityDto.Critical,
                    "SM_SECURITY_VALIDATION_GATE_UNCONFIGURED",
                    "Security Master validation gate is not configured",
                    "The workflow requires Security Master validation, but the validation gate service is unavailable.",
                    ["securityMaster"],
                    "Configure Security Master services before running governed workflows.",
                    [])
            ]);

        return new SecurityValidationGateResultDto(
            Workflow: workflow,
            Symbol: string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant(),
            SecurityId: securityId,
            IsResolved: securityId.HasValue,
            IsBlocked: true,
            Report: report,
            Snapshot: null);
    }
}
