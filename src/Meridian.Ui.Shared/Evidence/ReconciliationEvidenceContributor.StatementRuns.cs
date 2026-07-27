using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using static Meridian.Ui.Shared.Evidence.EvidenceContributionHelpers;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class ReconciliationEvidenceContributor
{
    private async Task<EvidenceContribution> ContributeStatementRunAsync(EvidenceContributionContext context, string runId)
    {
        var service = _services.GetService<IReconciliationApiService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Statement reconciliation API service is not registered."]);
        }

        var tenantContext = _services.GetService<IWorkstationTenantContextAccessor>();
        if (tenantContext is null || !tenantContext.TryGetCurrent(out var tenant)
            || string.IsNullOrWhiteSpace(tenant.TenantId)
            || string.IsNullOrWhiteSpace(tenant.CompanyId))
        {
            return new EvidenceContribution(
                [],
                [],
                [],
                [],
                ["Tenant and company scope are required for statement-run evidence."]);
        }

        var accessScope = new ReconciliationBreakQueueScope(tenant.TenantId, tenant.CompanyId);
        var detail = await service
            .GetStatementRunAsync(runId, accessScope, context.CancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return new EvidenceContribution([], [], [], [], [$"No statement-run evidence is available for '{runId}'."]);
        }

        var nodeId = NodeId(context.Subject, "statement-run");
        var runKey = string.IsNullOrWhiteSpace(detail.RunId) ? runId : detail.RunId!;
        var matchSummary = detail.MatchSummary;
        var openExceptionCount = detail.Breaks?.Count(static item =>
            string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Status, "InReview", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var status = openExceptionCount > 0 ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready;
        var generatedAt = detail.CompletedAtUtc ?? detail.ImportedAtUtc ?? detail.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var route = $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(runKey)}";
        var sourceFileHash = string.IsNullOrWhiteSpace(detail.SourceFileHash) ? null : detail.SourceFileHash;
        var node = Node(
            context.Subject,
            nodeId,
            "statement-run",
            status,
            matchSummary is null
                ? $"{openExceptionCount} open exception(s)."
                : $"{matchSummary.MatchedItemCount}/{matchSummary.StatementItemCount} item(s) matched; {matchSummary.BreakCount} break(s); {openExceptionCount} open exception(s).",
            "ReconciliationApiService",
            generatedAt,
            artifacts: sourceFileHash is null
                ? []
                :
                [
                    Artifact(
                        $"{nodeId}:detail",
                        "statement-run-detail-route",
                        route: route,
                        generatedAt: generatedAt,
                        hash: sourceFileHash)
                ],
            workItemIds: detail.Breaks?
                .Select(static item => item.BreakId)
                .Where(static breakId => !string.IsNullOrWhiteSpace(breakId))
                .Select(static breakId => breakId!)
                .ToArray() ?? []);

        return new EvidenceContribution([node], [], [], [nodeId], []);
    }
}
