using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    private static string BuildCapitalAccountWorkbenchRoute(
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var query = new List<string>
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}"
        };

        if (ledgerBookId.HasValue)
        {
            query.Add($"ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString("D"))}");
        }

        return UiApiRoutes.WithQuery(UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench, string.Join("&", query));
    }

    private static void AddQuery(List<string> query, string key, string? value)
    {
        var normalized = Normalize(value);
        if (normalized is not null)
        {
            query.Add($"{key}={Uri.EscapeDataString(normalized)}");
        }
    }

    private static string BuildWorkflowRoute(Guid workflowId)
        => UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString("D"));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ManagementCompanyEvidenceSignal(string Label, bool IsPresent);

    private sealed record CloseControlRequirement(
        string Key,
        string Label,
        string RequiredAction);

    private sealed record CloseControlEvaluation(
        OperationsContinuityWorkflowDto Workflow,
        CloseControlRequirement Requirement,
        IReadOnlyList<OperationsCloseChecklistTaskDto> Tasks,
        EvidenceStatusDto Status,
        bool IsReady);
}
