using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public interface IOperationsContinuityReconciliationBridge
{
    Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default);
}

public sealed class OperationsContinuityReconciliationBridge : IOperationsContinuityReconciliationBridge
{
    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly IReconciliationRunService? _reconciliationRunService;

    public OperationsContinuityReconciliationBridge(
        IOperationsContinuityWorkflowService workflowService,
        IReconciliationRunService? reconciliationRunService = null)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _reconciliationRunService = reconciliationRunService;
    }

    public async Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ShouldUseDirectRequest(request))
        {
            return await _workflowService
                .RunReconciliationAsync(workflowId, request, ct)
                .ConfigureAwait(false);
        }

        if (_reconciliationRunService is null)
        {
            return MissingReconciliationSource(
                "Reconciliation run service is not registered; submit explicit break cases and posture counts or register IReconciliationRunService.");
        }

        var detail = await ResolveReconciliationDetailAsync(request, ct).ConfigureAwait(false);
        if (detail is null)
        {
            return MissingReconciliationSource(
                "No reconciliation run detail was found for the requested source run or reconciliation run id.");
        }

        var bridgedRequest = BuildWorkflowRequest(request, detail);
        return await _workflowService
            .RunReconciliationAsync(workflowId, bridgedRequest, ct)
            .ConfigureAwait(false);
    }

    private async Task<ReconciliationRunDetail?> ResolveReconciliationDetailAsync(
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.ReconciliationRunId))
        {
            return await _reconciliationRunService!
                .GetByIdAsync(request.ReconciliationRunId.Trim(), ct)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceRunId))
        {
            return await _reconciliationRunService!
                .RunAsync(
                    new ReconciliationRunRequest(
                        request.SourceRunId.Trim(),
                        request.AmountTolerance.GetValueOrDefault(0.01m),
                        request.MaxAsOfDriftMinutes.GetValueOrDefault(5),
                        request.BankEntityId),
                    ct)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static OperationsReconciliationRunRequestDto BuildWorkflowRequest(
        OperationsReconciliationRunRequestDto request,
        ReconciliationRunDetail detail)
    {
        var evidence = MergeEvidence(request.EvidenceLinks, detail);
        return request with
        {
            BreakCases = MapBreakCases(detail, evidence),
            EvidenceLinks = evidence,
            SecurityCoverageIssueCount = detail.Summary.SecurityIssueCount,
            SecurityAccountingIssueCount = detail.Summary.SecurityMasterAccountingIssueCount,
            ExpectedAccountingEventCount = detail.Summary.ExpectedAccountingEventCount,
            ExpectedJournalPreviewCount = detail.Summary.ExpectedJournalPreviewCount,
            ReconciliationRunId = detail.Summary.ReconciliationRunId,
            SourceRunId = detail.Summary.RunId
        };
    }

    private static IReadOnlyList<OperationsBreakCaseDto> MapBreakCases(
        ReconciliationRunDetail detail,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence)
    {
        var reconciliationEvidence = evidence
            .Where(static link => string.Equals(link.Source, "reconciliation-run", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (reconciliationEvidence.Length == 0)
        {
            reconciliationEvidence = evidence.ToArray();
        }

        return detail.Breaks
            .Select(breakRow => new OperationsBreakCaseDto(
                BuildBreakId(detail.Summary.ReconciliationRunId, breakRow.CheckId),
                breakRow.CheckId,
                breakRow.Category.ToString(),
                breakRow.Severity.ToString(),
                breakRow.Status.ToString(),
                Owner: null,
                DueDate: null,
                ExpectedSource: "reconciliation-run",
                ActualSource: string.IsNullOrWhiteSpace(breakRow.MissingSource) ? null : breakRow.MissingSource.Trim(),
                breakRow.ExpectedAmount,
                breakRow.ActualAmount,
                breakRow.Variance,
                SecurityId: null,
                Symbol: null,
                SuggestedAction: breakRow.Reason,
                reconciliationEvidence))
            .ToArray();
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> MergeEvidence(
        IReadOnlyList<OperationsEvidenceLinkDto>? requestEvidence,
        ReconciliationRunDetail detail)
    {
        var merged = new List<OperationsEvidenceLinkDto>(requestEvidence ?? []);
        var reconciliationEvidenceId = $"reconciliation-run:{detail.Summary.ReconciliationRunId}";
        if (merged.All(link => !string.Equals(link.EvidenceId, reconciliationEvidenceId, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(new OperationsEvidenceLinkDto(
                reconciliationEvidenceId,
                "Reconciliation run detail",
                $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(detail.Summary.ReconciliationRunId)}",
                "reconciliation-run",
                detail.Summary.CreatedAt));
        }

        if (detail.Summary.BankTransactionCount > 0)
        {
            var bankEvidenceId = $"bank-normalized-activity:{detail.Summary.ReconciliationRunId}";
            if (merged.All(link => !string.Equals(link.EvidenceId, bankEvidenceId, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(new OperationsEvidenceLinkDto(
                    bankEvidenceId,
                    $"{detail.Summary.BankTransactionCount} normalized bank transaction(s)",
                    $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(detail.Summary.ReconciliationRunId)}",
                    "bank-normalized-activity",
                    detail.Summary.CreatedAt));
            }
        }

        return merged
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldUseDirectRequest(OperationsReconciliationRunRequestDto request) =>
        string.IsNullOrWhiteSpace(request.SourceRunId) &&
        string.IsNullOrWhiteSpace(request.ReconciliationRunId);

    private static string BuildBreakId(string reconciliationRunId, string checkId) =>
        string.IsNullOrWhiteSpace(checkId)
            ? reconciliationRunId
            : $"{reconciliationRunId}:{checkId.Trim()}";

    private static OperationsTransitionResultDto MissingReconciliationSource(string message) =>
        new(
            Success: false,
            ErrorCode: "RECONCILIATION_RUN_NOT_FOUND",
            ErrorMessage: message,
            Workflow: null,
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    "RECONCILIATION_RUN_NOT_FOUND",
                    message,
                    OperationsGateKeyDto.Reconciliation,
                    "Error",
                    [])
            ],
            NextActions: []);
}
