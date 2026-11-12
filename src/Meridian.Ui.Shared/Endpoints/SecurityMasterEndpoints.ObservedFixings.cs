using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapObservedBondFixingEndpoints(WebApplication app, RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        jsonOptions = FixedIncomeHttpJson.Options(jsonOptions);
        group = group.MapGroup(string.Empty).RequireWorkstationTenantScope();
        const string route = UiApiRoutes.SecurityMasterObservedBondFixings;
        group.MapGet(route, async (Guid securityId, [FromServices] ISecurityMasterQueryService query,
            [FromServices] IBondObservedFixingStore store, CancellationToken ct) =>
        {
            if (await query.GetByIdAsync(securityId, ct).ConfigureAwait(false) is null)
                return Results.NotFound();
            try
            { return Results.Json(await store.ListAsync(securityId, ct).ConfigureAwait(false), jsonOptions); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            { return Results.Conflict(new { error = "Retained observed fixing evidence is unavailable or corrupt. Restore the retained receipts before selecting fixings." }); }
        }).WithName("ListObservedBondFixings")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPost(route, async (Guid securityId, RetainBondObservedFixingRequest request, HttpContext context,
            [FromServices] ISecurityMasterQueryService query, [FromServices] IBondObservedFixingStore store, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            var security = await query.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (security is null)
                return Results.NotFound();
            if (security.Version != request.ExpectedSecurityVersion)
                return Results.Conflict(new { error = "Security terms changed. Reload the authoritative security and review the fixing again." });
            try
            {
                if (string.IsNullOrWhiteSpace(request.Justification) || request.ResetDate == default || request.ResetDate > DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
                    throw new ArgumentException("Review an already-reset date and provide a source/convention justification.");
                var terms = StructuredCashFlowTermsResolver.Resolve(security);
                var leg = terms.Legs?.SingleOrDefault();
                if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure || terms.CouponKind != SecurityCouponKind.Floating
                    || leg is null || string.IsNullOrWhiteSpace(leg.IndexName))
                    throw new ArgumentException("Observed fixing retention requires a floating bond with one named coupon index.");
                var declared = ScheduleSpecFactory.Resolve(security, terms, app.Services.GetService<IBusinessDayCalendarSource>());
                var schedule = declared.Status == DeclaredScheduleStatus.Declared ? ScheduleGenerator.Generate(declared.Spec!) : null;
                if (schedule is not { Succeeded: true } || !schedule.Periods.Any(period => period.AccrualStart == request.ResetDate))
                    throw new ArgumentException("Select an exact contractual accrual/reset date from the bond's declared schedule. Review conventions if its schedule is unavailable.");
                if (leg.ResetSchedule?.Any(row => row.ResetDate == request.ResetDate && row.FixingRate is not null) == true ||
                    !leg.HasDeclaredResetSchedule && leg.CurrentIndexResetDate == request.ResetDate && leg.CurrentIndexRate is not null && !string.IsNullOrWhiteSpace(leg.CurrentIndexRateSource))
                    throw new InvalidOperationException("Retained terms already supply this reset. Resolve the existing source in the governed terms workflow; a new observation cannot override it implicitly.");
                var claim = request.Observation ?? throw new ArgumentException("Select a typed observed fixing.");
                var provider = app.Services.GetServices<ISecurityRateObservationProvider>().SingleOrDefault(source => string.Equals(source.Name, claim.Provider, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The observed fixing connection is unavailable. Configure the source and reload observations.");
                var observations = await ReadRateEvidenceAsync(provider, claim.SeriesId, claim.ObservationDate, claim.ObservationDate, ct).ConfigureAwait(false);
                var matches = observations.Where(candidate => candidate.Value == claim.Value && candidate.Unit == claim.Unit &&
                    candidate.Kind == SecurityRateObservationKind.ObservedFixing && candidate.ObservationDate == claim.ObservationDate &&
                    candidate.VintageDate == claim.VintageDate && candidate.VintageEndDate == claim.VintageEndDate && candidate.Transformation == claim.Transformation &&
                    string.Equals(candidate.SeriesId, leg.IndexName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Currency, security.Currency, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || matches[0].ObservationDate != request.ResetDate)
                    throw new InvalidOperationException("The observed fixing must uniquely match this bond's index, currency and exact reset date. A fixing-date lag requires a separately supported declared mapping; stale observations are never carried forward.");
                var observation = matches[0];
                var fraction = observation.Unit switch
                {
                    SecurityRateUnit.Fraction => observation.Value,
                    SecurityRateUnit.Percent => observation.Value / 100m,
                    SecurityRateUnit.BasisPoints => observation.Value / 10_000m,
                    _ => throw new ArgumentException("Observed interest fixings require explicit Fraction, Percent or BasisPoints units.")
                };
                var receipt = await store.RetainAsync(new(securityId, security.Version, leg.IndexName.Trim(), request.ResetDate,
                    fraction, observation, ResolveActor(context), request.Justification), ct).ConfigureAwait(false);
                return Results.Json(receipt, jsonOptions);
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            { return Results.Conflict(new { error = "Observed fixing retention could not be confirmed. Reload retained receipts before retrying; prior evidence is never overwritten." }); }
        }).WithName("RetainObservedBondFixing")
            .RequirePermission(UserPermission.ModifySecurityMaster)
            .AddEndpointFilter(RequireModifySecurityMasterPermission)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<BondObservedFixingReceipt>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict);
    }
}
