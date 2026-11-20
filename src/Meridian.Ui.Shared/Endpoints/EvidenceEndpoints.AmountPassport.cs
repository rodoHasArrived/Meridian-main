using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Ui.Shared.Services.AmountPassportFacts;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class EvidenceEndpoints
{
    private static void MapAmountPassportEndpoint(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(EvidenceSubroute(UiApiRoutes.WorkstationAmountPassport), async (
            string subjectKind, string subjectId, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.Actor))
                return Results.Unauthorized();
            if (subjectKind is not (LedgerKind or PositionKind) || !TryReadPassportIdentity(context.Request.Query, subjectKind, subjectId, out var identity))
                return Results.BadRequest(Error("amount-passport-scope-required", "The passport requires its exact subject, scope, date and revision."));
            if (identity.Scope.TenantId != tenant.TenantId || identity.Scope.CompanyId != tenant.CompanyId)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                if (subjectKind == PositionKind)
                {
                    if (!BrokeragePortfolioSyncService.TryParsePositionPassport(subjectId, out var accountId, out _, out _, out _)
                        || identity.Scope.AccountId != accountId.ToString("D"))
                        return Results.BadRequest(Error("amount-passport-scope-required", "The position observation requires its exact account identity."));
                    if (!await FundAccountEndpoints.CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    var positions = context.RequestServices.GetService<BrokeragePortfolioSyncService>();
                    if (positions is null)
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    return PassportResult(await positions.GetPositionPassportAsync(identity, context.RequestAborted).ConfigureAwait(false));
                }

                if (!CanReadSubject(LedgerKind, context, subjectId))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (identity.Scope.FundProfileId is not { } fundId || identity.Scope.LedgerBookId is not { } bookId
                    || identity.Scope.PeriodId is not { } periodId)
                    return Results.BadRequest(Error("amount-passport-scope-required", "Ledger amounts require their exact fund, book and period."));
                var scope = new LedgerAmountScopeDto(tenant.TenantId!, tenant.CompanyId!, fundId, bookId, periodId);
                if (ReportLedgerAmountProvenanceService.IsReportSubject(subjectId))
                    return await FundStructureEndpoints.ReadReportAmountPassportAsync(identity, scope, BuildAmountReportAccess(context), context, jsonOptions)
                        .ConfigureAwait(false);
                var posted = context.RequestServices.GetRequiredService<PostedLedgerAmountProvenanceService>();
                var packet = await posted.GetPacketAsync(subjectId, scope, context.RequestAborted, identity).ConfigureAwait(false);
                return PassportResult(packet?.LedgerAmount?.AmountPassport);
            }
            catch (AmountPassportScopeMismatchException)
            {
                return Results.Conflict(Error("amount-passport-scope-mismatch", "The retained record does not match the declared scope, date or revision."));
            }
            catch (IOException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("GetWorkstationAmountPassport")
        .Produces<AmountPassportDto>(200)
        .Produces<EvidenceEndpointErrorDto>(400)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces<EvidenceEndpointErrorDto>(404)
        .Produces<EvidenceEndpointErrorDto>(409)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
            UserPermission.ManageDirectLending, UserPermission.AdminMaintenance, UserPermission.ViewTrades)
        .RequireWorkstationTenantCompanyScope();

        IResult PassportResult(AmountPassportDto? passport) => passport is null
            ? Results.NotFound(Error("amount-passport-unavailable", "The retained source is missing or unavailable in the declared scope."))
            : Results.Json(passport, jsonOptions);
    }

    private static bool TryReadPassportIdentity(IQueryCollection query, string kind, string subjectId,
        out AmountPassportIdentityDto identity)
    {
        identity = null!;
        string[] required = ["tenantId", "companyId", "basis", "asOf", "revision"];
        string[] optional = ["fundProfileId", "ledgerBookId", "periodId", "entityId", "accountId", "organizationId"];
        if (subjectId.Length is 0 or > 512 || required.Any(key => !query.ContainsKey(key))
            || query.Any(item => item.Value.Count != 1 || string.IsNullOrWhiteSpace(item.Value[0])
                || item.Value[0]!.Length > 256 || (!required.Contains(item.Key) && !optional.Contains(item.Key))))
            return false;
        var book = ReadGuid("ledgerBookId");
        var period = ReadGuid("periodId");
        if ((query.ContainsKey("ledgerBookId") && book is null) || (query.ContainsKey("periodId") && period is null))
            return false;
        identity = new(kind, subjectId, new(Value("tenantId")!, Value("companyId")!, Value("basis")!,
            Value("fundProfileId"), book, period, Value("entityId"), Value("accountId"), Value("organizationId")),
            Value("asOf")!, Value("revision")!);
        return true;

        string? Value(string key) => query.TryGetValue(key, out var value) ? value[0] : null;
        Guid? ReadGuid(string key) => Guid.TryParseExact(Value(key), "D", out var result) && result != Guid.Empty ? result : null;
    }
}
