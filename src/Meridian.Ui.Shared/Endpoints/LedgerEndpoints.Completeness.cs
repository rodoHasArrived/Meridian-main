using System.Text.Json.Serialization;
using Meridian.Contracts.Tenancy;
using Meridian.Execution.Services;
using Meridian.Execution.Events;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.AssetOperations;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapLedgerCompletenessEndpoints(WebApplication app)
    {
        app.MapGet("/api/ledger/books/{ledgerBookId:guid}/periods/{periodId:guid}/completeness", async (
            Guid ledgerBookId, Guid periodId, Guid aggregateId, string financialAccountId,
            string? sourceKinds, string? sessionId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!HasAccountingPackageTenantScope(tenant) || !EndpointAuthorization.TryResolveActor(context, out _))
                return EndpointHelpers.Forbidden();
            var journals = ResolveJournalStore(context);
            if (journals is null || ResolveService(context) is null
                || context.RequestServices.GetService<IFundProfileTenancyRegistry>() is null)
                return ServiceUnavailable();
            if (await ResolveAccessiblePreparationBookAsync(context, ledgerBookId).ConfigureAwait(false) is null)
                return Results.NotFound();
            var selected = new List<LedgerAccountingSourceKind>();
            foreach (var token in (sourceKinds ?? "Fill,AssetEvent,LoanEvent").Split(',', StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse<LedgerAccountingSourceKind>(token, true, out var kind) || !Enum.IsDefined(kind))
                    return Results.BadRequest(new { error = "Unknown accounting source population." });
                selected.Add(kind);
            }
            if (selected.Contains(LedgerAccountingSourceKind.LoanEvent) &&
                !EndpointAuthorization.HasAnyPermission(context, UserPermission.AdminMaintenance,
                    UserPermission.ViewDirectLending, UserPermission.ManageDirectLending))
                return EndpointHelpers.Forbidden();
            try
            {
                var service = new LedgerAccountingCompletenessReadService(journals,
                    context.RequestServices.GetService<IDirectLendingStateStore>(),
                    context.RequestServices.GetServices<ILedgerAccountingSourceReader>(),
                    context.RequestServices.GetService<IPaperSessionStore>(),
                    context.RequestServices.GetService<IAssetAccountingEventProjectionStore>(),
                    context.RequestServices.GetService<PaperFinancingPostingService>());
                var report = await service.ReadAsync(new(ledgerBookId, periodId, aggregateId,
                    financialAccountId, selected, sessionId), context.RequestAborted).ConfigureAwait(false);
                return Results.Json(report, LedgerAccountingCompletenessJsonContext.Default.LedgerAccountingCompletenessReadResult);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException
                or LedgerValidationException or System.Text.Json.JsonException)
            {
                return Results.Problem("The retained source population could not be safely reconciled.", statusCode: 503);
            }
        }).WithName("GetLedgerAccountingCompleteness")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
                UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .Produces<LedgerAccountingCompletenessReadResult>(200).Produces(400).Produces(403).Produces(404).Produces(503);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LedgerAccountingCompletenessReadResult))]
internal partial class LedgerAccountingCompletenessJsonContext : JsonSerializerContext;
