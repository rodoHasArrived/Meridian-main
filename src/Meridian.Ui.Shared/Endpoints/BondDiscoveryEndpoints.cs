using System.Text.Json;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Subscriptions.Models;
using Meridian.Core.Exceptions;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Provider lookup supplies candidate identity evidence, never approved Security Master terms.</summary>
public static class BondDiscoveryEndpoints
{
    public static void MapBondDiscoveryEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).RequireWorkstationTenantScope();
        group.MapGet(UiApiRoutes.ReferenceDataBondDiscovery, async (HttpContext context, string? query) =>
        {
            var search = context.RequestServices.GetService<SymbolSearchService>();
            if (search is null)
                return Results.Problem("Provider-backed bond discovery is not configured on this host.", statusCode: 503);

            var normalized = query?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                return Results.BadRequest(new { error = "Enter a bond symbol or issuer before lookup." });
            if (normalized.Length > 200)
                return Results.BadRequest(new { error = "Bond lookup accepts at most 200 characters." });

            // Alpaca is the retained implementation with a fixed_income filter. Other symbol
            // providers must not silently broaden this bond-only surface to equities.
            var available = false;
            string? failureReason = null;
            SymbolSearchResponse? results = null;
            // The configuration lease protects dispatch ownership. Bound the whole operation so
            // quota pacing or an unresponsive provider cannot indefinitely block credential maintenance.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20),
                context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System);
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, deadline.Token);
            try
            {
                using var ownership = await AcquireOwnershipAsync(context, lookup.Token).ConfigureAwait(false);
                if (!ownership.TenantAllowed)
                    return Results.Problem("Bond discovery is unavailable in this tenant.", statusCode: StatusCodes.Status403Forbidden);

                results = await search.SearchAuthoritativeAsync(new SymbolSearchRequest(normalized, Limit: 25,
                    AssetType: "fixed_income", Provider: "alpaca", IncludeFigi: false), lookup.Token).ConfigureAwait(false);
                available = true;
            }
            catch (ProviderSearchException ex)
            {
                failureReason = JsonNamingPolicy.CamelCase.ConvertName(ex.Failure.ToString());
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
            {
                failureReason = "lookupTimedOut";
            }
            var candidates = (results?.Results ?? []).Where(candidate =>
                string.Equals(candidate.AssetType, "FixedIncome", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.AssetType, "fixed_income", StringComparison.OrdinalIgnoreCase)
                || (SecurityAssetClassCatalog.TryGet(candidate.AssetType, out var descriptor)
                    && descriptor.CarriesBondCouponStructure)).ToArray();

            EndpointAuthorization.TryGetPermissions(context, out var permissions);
            return Results.Json(new
            {
                provider = "alpaca",
                providerLabel = "Alpaca",
                available,
                unavailableReason = available ? null : "Alpaca bond lookup could not be completed. Retry lookup before reviewing candidates.",
                failureReason,
                canStage = (permissions & UserPermission.ModifySecurityMaster) == UserPermission.ModifySecurityMaster,
                query = normalized,
                candidates,
                observedAtUtc = DateTimeOffset.UtcNow
            }, jsonOptions, statusCode: failureReason is null ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("DiscoverProviderBonds")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record OwnershipLease(IDisposable ConfigurationLease, bool TenantAllowed) : IDisposable
    {
        public void Dispose() => ConfigurationLease.Dispose();
    }

    private static async Task<OwnershipLease> AcquireOwnershipAsync(HttpContext context, CancellationToken ct)
    {
        var config = context.RequestServices.GetService<ConfigStore>()
            ?? throw new ProviderSearchException(ProviderSearchFailure.CredentialsUnavailable,
                "Owned provider configuration is unavailable.");
        IDisposable? lease = null;
        try
        {
            // A binding cannot be removed or reassigned between this check and provider dispatch.
            lease = await config.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
            var scope = ProviderRuntimeCredentialService.ResolveScope(config.LoadRequired(), "alpaca")
                ?? throw new InvalidOperationException("An owned Alpaca runtime binding is required.");
            if (!string.Equals(scope.TenantId, context.Items[LoginSessionMiddleware.CurrentTenantIdKey] as string, StringComparison.Ordinal))
                return new(lease, false);
            var runtime = context.RequestServices.GetService<ProviderRuntimeCredentialService>()
                ?? throw new InvalidOperationException("Runtime credential application status is unavailable.");
            var status = runtime.GetApplicationStatus("alpaca", scope.ConnectionId, scope.TenantId);
            if (!status.IsBound || status.PendingRestart)
                throw new InvalidOperationException("The owned credential binding has not reached running provider instances.");
            return new(lease, true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            lease?.Dispose();
            throw new ProviderSearchException(ProviderSearchFailure.CredentialsUnavailable,
                "Owned provider credentials are unavailable.", ex);
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }
}
