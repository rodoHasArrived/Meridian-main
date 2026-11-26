using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Scoped operator casework. These commands grant no legal, close or posting authority.</summary>
public static class IntercompanyAgreementWorkQueueEndpoints
{
    public static void MapIntercompanyAgreementWorkQueueEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        var transport = new JsonSerializerOptions(options);
        transport.Converters.Insert(0, new IntercompanyDecimalJsonConverter());
        var json = new IntercompanyAgreementWorkQueueJsonContext(transport);

        app.MapGet("/api/ledger/intercompany/work-queue", (string? fundProfileId, Guid? ledgerBookId,
            DateOnly asOf, int? expiryHorizonDays, Guid? entityId, HttpContext http) =>
            ExecuteAsync(http, fundProfileId, ledgerBookId, asOf, expiryHorizonDays, entityId, "read", json))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost("/api/ledger/intercompany/work-queue/observe", (string? fundProfileId, Guid? ledgerBookId,
            DateOnly asOf, int? expiryHorizonDays, Guid? entityId, HttpContext http) =>
            ExecuteAsync(http, fundProfileId, ledgerBookId, asOf, expiryHorizonDays, entityId, "observe", json))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost("/api/ledger/intercompany/work-queue/update", (string? fundProfileId, Guid? ledgerBookId,
            DateOnly asOf, int? expiryHorizonDays, Guid? entityId, HttpContext http) =>
            ExecuteAsync(http, fundProfileId, ledgerBookId, asOf, expiryHorizonDays, entityId, "update", json))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, string? fundProfileId, Guid? ledgerBookId,
        DateOnly asOf, int? expiryHorizonDays, Guid? entityId, string operation,
        IntercompanyAgreementWorkQueueJsonContext json)
    {
        if (ledgerBookId is not { } bookId || bookId == Guid.Empty || asOf == default
            || expiryHorizonDays is < 0 or > 3660 || entityId == Guid.Empty)
            return Results.BadRequest(new { error = "Select a ledger book, an explicit as-of date and a horizon between 0 and 3660 days." });

        var caller = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        var service = http.RequestServices.GetService<IIntercompanyAgreementWorkQueueService>();
        var journals = http.RequestServices.GetService<ILedgerJournalStore>();
        var ownership = http.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (service is null || journals is null || ownership is null)
            return Unavailable();

        try
        {
            var book = await journals.GetLedgerBookAsync(bookId, http.RequestAborted);
            var owner = book is null ? null : await ownership.ResolveAsync(book.FundProfileId, http.RequestAborted);
            if (book is null || owner is null || !owner.IsHeldBy(caller.TenantId)
                || !string.Equals(owner.CompanyId, caller.CompanyId, StringComparison.Ordinal)
                || fundProfileId is not null && !string.Equals(fundProfileId, book.FundProfileId, StringComparison.Ordinal)
                || entityId.HasValue && entityId != book.FundStructureNodeId)
                return EndpointHelpers.Forbidden();

            using var reportingAccess = IntercompanyLedgerReadPermissionScope.TryEnter(http);
            if (reportingAccess is null)
                return EndpointHelpers.Forbidden();
            if (operation != "read")
            {
                // A manage grant in one entity must not combine with a read grant in another
                // to authorize case mutations there. The enclosing scope restores this item.
                http.Items[HttpContextIntercompanyAgreementAccessAuthority.RequiredPermissionKey] =
                    new[] { UserPermission.ManageLedgerReports };
            }
            var context = new IntercompanyAgreementCallContext(caller.TenantId!, caller.Actor!);
            var scope = new IntercompanyObligationMonitorRequestDto(book.FundProfileId, bookId, asOf,
                expiryHorizonDays ?? 30, entityId);

            if (operation == "read")
            {
                var result = await service.GetAsync(context, caller.CompanyId, scope, http.RequestAborted);
                result = result with
                {
                    CanManage = EndpointAuthorization.HasAnyPermission(http,
                        UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
                };
                return Results.Json(result, json.IntercompanyAgreementWorkQueueDto);
            }

            if (operation == "observe")
            {
                var request = await JsonSerializer.DeserializeAsync(http.Request.Body,
                    json.IntercompanyAgreementWorkQueueObserveRequestDto, http.RequestAborted);
                if (request is null)
                    return Results.BadRequest(new { error = "A case observation command is required." });
                var result = await service.ObserveAsync(context, caller.CompanyId, scope, request, http.RequestAborted);
                return Results.Json(result, json.IntercompanyAgreementWorkItemDto);
            }

            var update = await JsonSerializer.DeserializeAsync(http.Request.Body,
                json.IntercompanyAgreementWorkQueueUpdateRequestDto, http.RequestAborted);
            if (update is null)
                return Results.BadRequest(new { error = "A case update command is required." });
            var updated = await service.UpdateAsync(context, caller.CompanyId, scope, update, http.RequestAborted);
            return Results.Json(updated, json.IntercompanyAgreementWorkItemDto);
        }
        catch (IntercompanyAgreementWorkQueueConflictException error)
        {
            return Results.Conflict(new { error = error.Message });
        }
        catch (IntercompanyAgreementWorkQueueUnavailableException) { return Unavailable(); }
        catch (IntercompanyAgreementNotFoundException) { return Results.NotFound(); }
        catch (IntercompanyAgreementForbiddenException) { return EndpointHelpers.Forbidden(); }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (IntercompanyAgreementValidationException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (JsonException) { return Results.BadRequest(new { error = "The case command could not be read. Check its fields and action." }); }
        catch (IOException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (Npgsql.NpgsqlException) { return Unavailable(); }
    }

    private static IResult Unavailable() => Results.Problem(
        "Intercompany case history or source authority is unavailable. Refresh before taking an action.", statusCode: 503);
}
