using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static Task<IResult> ListGovernedReportingAmountsAsync(string runId, HttpContext context,
        JsonSerializerOptions jsonOptions) => ExecuteGovernanceAsync(
            context, runId,
            async (coordinator, caller, id, ct) =>
            {
                // Apply exactly the same governed run authorization before exposing labels or money.
                var run = await coordinator.GetAsync(id, caller, ct).ConfigureAwait(false);
                var service = context.RequestServices.GetService<ReportLedgerAmountProvenanceService>()
                    ?? throw new NotSupportedException("Generated report amount proof is unavailable.");
                // Proof opens through the evidence route, which also requires ledger-report access.
                var proofAvailable = EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewLedgerReports,
                    UserPermission.ManageLedgerReports, UserPermission.ManageDirectLending, UserPermission.AdminMaintenance);
                return service.List(id, BuildReportAccessQueryContext(context))
                    .Select(amount => amount with
                    {
                        ProofAvailable = proofAvailable,
                        AmountPassport = proofAvailable && amount.AmountPassport is { } passport &&
                            passport.Identity.Revision == run.Snapshot.SourceCheckpointHash &&
                            passport.Identity.Scope.OrganizationId == run.Scope.OrganizationId ? passport : null
                    }).ToArray();
            },
            static (amounts, _) => amounts, StatusCodes.Status200OK, jsonOptions);

    internal static Task<IResult> ReadReportAmountPassportAsync(AmountPassportIdentityDto identity,
        LedgerAmountScopeDto scope, ReportAccessQueryContext access, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (!ReportLedgerAmountProvenanceService.TryParseSubject(identity.SubjectId, out var runId, out _))
            return Task.FromResult(Results.NotFound());

        return ExecuteGovernanceAsync(context, runId, async (coordinator, caller, id, ct) =>
        {
            var run = await coordinator.GetAsync(id, caller, ct).ConfigureAwait(false);
            if (run.Scope.TenantId != scope.TenantId || run.Scope.CompanyId != scope.CompanyId || run.Scope.FundId != scope.FundProfileId ||
                run.Scope.BookId != scope.LedgerBookId.ToString("D") || run.Scope.PeriodId != scope.PeriodId.ToString("D") ||
                run.Scope.OrganizationId != identity.Scope.OrganizationId || run.Snapshot.SourceCheckpointHash != identity.Revision)
                throw new AmountPassportScopeMismatchException();
            var service = context.RequestServices.GetRequiredService<ReportLedgerAmountProvenanceService>();
            var revisions = await coordinator.ListAsync(run.SeriesId, caller, ct).ConfigureAwait(false);
            var history = new List<AmountPassportRecordDto> { ReportRevision("original", run, "Retained report revision") };
            foreach (var relative in revisions)
            {
                if (relative.RunId == run.RestatementOfRunId)
                    history.Add(ReportRevision("predecessor", relative, "Predecessor report revision"));
                else if (relative.RestatementOfRunId == run.RunId && relative.GovernanceState == GovernedReportingState.Released)
                    history.Add(ReportRevision("successor", relative, "Released restatement"));
            }
            var packet = await service.GetPacketAsync(identity.SubjectId, scope, access, ct, identity, history).ConfigureAwait(false);
            return packet?.LedgerAmount?.AmountPassport
                ?? throw new KeyNotFoundException("The retained report amount was not found in the declared scope.");
        }, static (passport, _) => passport, StatusCodes.Status200OK, jsonOptions);
    }

    private static AmountPassportRecordDto ReportRevision(string kind, GovernedReportingRun run, string label)
        => new(kind, run.RunId, label,
            $"/workstation/reporting/runs/detail?runId={Uri.EscapeDataString(run.RunId)}",
            ContentHash: run.Snapshot.SourceCheckpointHash);
}
