using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Compatibility wrappers for queue projection helpers used by the workstation endpoint partials.
/// The canonical projection lives in the shared application layer so statement intake can publish
/// casework without requiring an operator to open a GET route first.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static bool TryResolveReconciliationBreakQueueScope(
        HttpContext context,
        out ReconciliationBreakQueueScope scope)
    {
        var tenantId = context.Items[LoginSessionMiddleware.CurrentTenantIdKey] as string;
        var companyId = context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] as string;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            scope = null!;
            return false;
        }

        scope = new ReconciliationBreakQueueScope(tenantId, companyId);
        return true;
    }

    private static ReconciliationBreakQueueItem MapStatementBreakToQueueItem(
        StatementBreakDto statementBreak)
        => ReconciliationBreakQueueProjection.ProjectStatement(statementBreak);

    private static IReadOnlyList<ReconciliationBreakMeasureDto> BuildDefaultBreakMeasures(
        decimal? expected,
        decimal? actual,
        decimal variance,
        decimal? tolerance,
        string unit)
        => ReconciliationBreakQueueProjection.BuildDefaultMeasures(
            expected,
            actual,
            variance,
            tolerance,
            unit);

    private static bool IsOpenStatementBreak(string? status)
        => ReconciliationBreakQueueProjection.IsOpenStatementBreak(status);

    private static string ComputeStatementBreakLegacyFingerprint(StatementBreakDto statementBreak)
        => ReconciliationBreakQueueProjection.ComputeStatementBreakLegacyFingerprint(statementBreak);

    private static string ComputeReconciliationSourceFingerprint(params string?[] parts)
        => ReconciliationBreakQueueProjection.ComputeSourceFingerprint(parts);

    private static ReconciliationBreakQueueProjection.ReconciliationExceptionRouting
        ResolveReconciliationExceptionRouting(
            ReconciliationBreakCategory category,
            ReconciliationBreakSeverity severity,
            decimal variance)
        => ReconciliationBreakQueueProjection.ResolveRouting(category, severity, variance);
}
