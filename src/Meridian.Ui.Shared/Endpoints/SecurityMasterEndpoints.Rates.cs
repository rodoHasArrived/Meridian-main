using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapRateIngestionEndpoints(WebApplication app, RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        jsonOptions = FixedIncomeHttpJson.Options(jsonOptions);
        group.MapGet(UiApiRoutes.SecurityMasterRateObservations, async (
            string provider, string seriesId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(seriesId) || (from.HasValue && to.HasValue && from > to))
                return Results.BadRequest(new { error = "Select a provider, series and a valid observation date interval." });
            try
            {
                var source = app.Services.GetServices<ISecurityRateObservationProvider>()
                    .SingleOrDefault(candidate => string.Equals(candidate.Name, provider, StringComparison.OrdinalIgnoreCase));
                if (source is null)
                    return Results.Conflict(new { error = "This connection has no typed rates capability. Configure a rates provider before loading fixings." });
                return Results.Json(await ReadRateEvidenceAsync(source, seriesId, from, to, ct).ConfigureAwait(false), jsonOptions);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Conflict(new { error = "The rates provider could not be reached. Review the connection and retry; no observations were published." });
            }
        }).WithName("GetSecurityMasterRateObservations")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .Produces<IReadOnlyList<SecurityRateObservationDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict);

        group.MapPost(UiApiRoutes.SecurityMasterRateIngest, async (
            IngestSecurityRatesRequest? request, HttpContext context,
            [FromServices] ISecurityCurveStore store, CancellationToken ct) =>
        {
            var authorizationResult = RequireSecurityMasterMutationPermission(context);
            if (authorizationResult is not null)
                return authorizationResult;
            if (request is null)
                return Results.BadRequest(new { error = "Typed rate observations and a reviewed construction method are required." });
            var definition = await store.GetDefinitionAsync(request.CurveId, ct).ConfigureAwait(false);
            if (definition is null)
                return Results.NotFound(new { error = "Select an existing immutable curve definition before publishing." });
            return await RunCurveWriteAsync(context, async () =>
            {
                if (request.Points is not { Count: > 0 and <= 256 })
                    throw new ArgumentException("Select between one and 256 typed observations.");
                // The browser carries a selection claim. Re-read and bind the provider facts;
                // edited client timestamps, roles, currency or values never become source authority.
                var providers = app.Services.GetServices<ISecurityRateObservationProvider>().ToArray();
                var verifiedPoints = new List<SecurityRateIngestionPointDto>();
                var batches = new Dictionary<(string Provider, string SeriesId, DateOnly Date), IReadOnlyList<SecurityRateObservationDto>>();
                foreach (var selection in request.Points)
                {
                    var claim = selection.Observation ?? throw new ArgumentException("An observation selection is required.");
                    var source = providers.SingleOrDefault(candidate => string.Equals(candidate.Name, claim.Provider, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("The selected rates connection is unavailable. Reload observations after reviewing the connection.");
                    var key = (source.Name, claim.SeriesId, claim.ObservationDate);
                    if (!batches.TryGetValue(key, out var observed))
                    {
                        observed = await ReadRateEvidenceAsync(source, claim.SeriesId, claim.ObservationDate, claim.ObservationDate, ct).ConfigureAwait(false);
                        batches.Add(key, observed);
                    }
                    var matches = observed.Where(candidate => candidate.Value == claim.Value && candidate.Unit == claim.Unit &&
                        candidate.ObservationDate == claim.ObservationDate && candidate.VintageDate == claim.VintageDate &&
                        candidate.VintageEndDate == claim.VintageEndDate && candidate.Kind == claim.Kind &&
                        candidate.Transformation == claim.Transformation && candidate.Currency == claim.Currency).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException("The provider observation changed or could not be uniquely verified. Reload the rates evidence and review the construction again.");
                    verifiedPoints.Add(selection with { Observation = matches[0] });
                }
                return await store.PublishVersionAsync(SecurityRateIngestionRules.BuildPublication(
                    request with { Points = verifiedPoints }, definition, ResolveActor(context)), ct).ConfigureAwait(false);
            }, jsonOptions).ConfigureAwait(false);
        }).WithName("IngestSecurityMasterRates")
            .RequirePermission(UserPermission.ModifySecurityMaster)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
            .AddEndpointFilter(RequireModifySecurityMasterPermission)
            .Produces<SecurityCurveVersionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<IReadOnlyList<SecurityRateObservationDto>> ReadRateEvidenceAsync(
        ISecurityRateObservationProvider source, string seriesId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        try
        {
            return await source.GetRateObservationsAsync(seriesId, from, to, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or Meridian.Core.Exceptions.DataProviderException or JsonException)
        {
            throw new InvalidOperationException("The rates provider could not verify the selected evidence. Review connection availability or rate limits, reload observations, and retry. No curve was published.", exception);
        }
    }
}
