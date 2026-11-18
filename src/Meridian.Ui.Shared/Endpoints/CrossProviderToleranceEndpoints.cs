using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// User-editable cross-provider data-quality tolerances: read, save (with bounds, a recorded reason
/// when loosening, and before/after audit evidence) and an on-demand "would have flagged" preview.
/// </summary>
public static class CrossProviderToleranceEndpoints
{
    /// <summary>Maximum lookback for the preview; each preview fetches one range per provider.</summary>
    public const int MaxPreviewDays = 30;

    private const int ProviderCallsPerPreview = 2;

    public static void MapCrossProviderToleranceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Quality");
        group.RequireWorkstationTenantScope();

        group.MapGet(UiApiRoutes.QualityCrossProviderPolicy, (
            [FromServices] ConfigStore store,
            [FromServices] IEnumerable<IHistoricalDataProvider>? providers) =>
        {
            var config = store.Load();
            return Results.Json(new CrossProviderTolerancePolicyResponse(
                CrossProviderTolerancePolicy.FromConfig(config),
                CrossProviderTolerancePolicy.Default,
                new CrossProviderToleranceBounds(
                    CrossProviderTolerancePolicy.MaxPriceTolerancePercent,
                    CrossProviderTolerancePolicy.MaxVolumeTolerancePercent,
                    CrossProviderTolerancePolicy.MaxBackfillBarsToCompare,
                    CrossProviderTolerancePolicy.MaxAssetClassOverrides,
                    MaxPreviewDays),
                (config.DataQuality?.CrossProviderAudit ?? []).Reverse().Take(100).ToArray(),
                PreviewProviderNames(providers),
                ProviderCallsPerPreview), jsonOptions);
        }).WithName("GetCrossProviderTolerancePolicy")
            .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig);

        group.MapPut(UiApiRoutes.QualityCrossProviderPolicy, async (
            CrossProviderTolerancePolicyRequest request,
            HttpContext context,
            [FromServices] ConfigStore store,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request?.Policy is null)
                return ApiProblemDetails.Validation(context, "policy", "A tolerance policy is required.");
            var actor = HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor;
            if (string.IsNullOrWhiteSpace(actor))
                return Results.Unauthorized();

            try
            {
                request.Policy.Validate();
                if (request.Reason?.Length > 2000)
                    throw new ArgumentException("The change reason cannot exceed 2000 characters.");
                var audit = await store.UpdateAsync(config => ApplyPolicy(config, request, actor), ct).ConfigureAwait(false);
                loggerFactory.CreateLogger(nameof(CrossProviderToleranceEndpoints)).LogInformation(
                    "Cross-provider tolerances changed {AuditId} {Actor} {Loosened} {Reason} {PricePercent} {QuotePercent} {VolumePercent} {VolumeMode} {BackfillBars} {BackfillClosePercent} {OverrideCount}",
                    audit.Id, audit.Actor, audit.Loosened, audit.Reason, audit.After.PriceTolerancePercent,
                    audit.After.QuoteTolerancePercent, audit.After.VolumeTolerancePercent, audit.After.VolumeMode,
                    audit.After.BackfillBarsToCompare, audit.After.BackfillCloseTolerancePercent,
                    audit.After.AssetClassOverrides?.Count ?? 0);
                return Results.Json(new CrossProviderTolerancePolicySaveResponse(
                    audit.After, audit, store.GetChangeStatus()), jsonOptions);
            }
            catch (ArgumentException exception)
            {
                return ApiProblemDetails.Validation(context, "policy", exception.Message);
            }
        }).WithName("UpdateCrossProviderTolerancePolicy")
            .RequirePermission(UserPermission.ModifyConfig)
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ModifyConfig))
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.QualityCrossProviderPreview, async (
            CrossProviderTolerancePreviewRequest request,
            HttpContext context,
            [FromServices] IEnumerable<IHistoricalDataProvider>? providers,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request is null)
                return ApiProblemDetails.Validation(context, "request", "A preview request is required.");
            var symbol = request.Symbol?.Trim().ToUpperInvariant() ?? "";
            if (symbol.Length is 0 or > 32 || symbol.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '/' and not '^' and not '='))
                return ApiProblemDetails.Validation(context, "symbol", "A valid symbol is required.");
            if (request.Days is < 1 or > MaxPreviewDays)
                return ApiProblemDetails.Validation(context, "days", $"The preview covers 1 to {MaxPreviewDays} days.");
            var policy = request.Policy ?? CrossProviderTolerancePolicy.Default;
            try
            {
                policy.Validate();
            }
            catch (ArgumentException exception)
            {
                return ApiProblemDetails.Validation(context, "policy", exception.Message);
            }

            var available = (providers ?? []).ToArray();
            var names = PreviewProviderNames(available);
            string? Match(string? name) => names.FirstOrDefault(n => string.Equals(n, name?.Trim(), StringComparison.OrdinalIgnoreCase));
            var baseline = Match(request.BaselineProvider);
            var comparison = Match(request.ComparisonProvider);
            if (baseline is null || comparison is null)
                return ApiProblemDetails.Validation(context, "provider", "Choose two available historical providers.");
            if (string.Equals(baseline, comparison, StringComparison.OrdinalIgnoreCase))
                return ApiProblemDetails.Validation(context, "provider", "Choose two different providers to compare.");

            var to = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
            var from = to.AddDays(-(request.Days - 1));
            var tolerances = policy.Resolve(request.AssetClass);
            try
            {
                var service = new CrossSourceBackfillReconciliationService(available.Where(provider =>
                    string.Equals(provider.Name, baseline, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(provider.Name, comparison, StringComparison.OrdinalIgnoreCase)));
                var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
                    symbol, from, to, baseline, [comparison],
                    CloseToleranceRatio: (decimal)tolerances.BackfillCloseTolerancePercent / 100m,
                    VolumeToleranceRatio: tolerances.VolumeMode == CrossProviderVolumeMode.Off
                        ? decimal.MaxValue
                        : (decimal)tolerances.VolumeTolerancePercent / 100m), ct).ConfigureAwait(false);
                return Results.Json(BuildPreview(symbol, from, to, baseline, comparison, tolerances, result), jsonOptions);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                loggerFactory.CreateLogger(nameof(CrossProviderToleranceEndpoints)).LogWarning(
                    exception, "Cross-provider tolerance preview for {Symbol} could not run", symbol);
                return ApiProblemDetails.Validation(context, "preview", "The preview could not run for this symbol and provider pair.");
            }
        }).WithName("PreviewCrossProviderTolerancePolicy")
            .RequirePermission(UserPermission.ModifyConfig)
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ModifyConfig))
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    /// <summary>Validates and saves a policy, appending before/after audit evidence in the same write.</summary>
    public static (AppConfig Config, CrossProviderTolerancePolicyAudit Result) ApplyPolicy(
        AppConfig config,
        CrossProviderTolerancePolicyRequest request,
        string actor)
    {
        ArgumentNullException.ThrowIfNull(request.Policy);
        request.Policy.Validate();
        var dataQuality = config.DataQuality ?? new DataQualityConfig();
        var previous = CrossProviderTolerancePolicy.FromConfig(config);
        var loosened = request.Policy.Loosens(previous);
        if (loosened && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Loosening cross-provider tolerances requires a recorded reason.");

        var audit = new CrossProviderTolerancePolicyAudit(Guid.NewGuid(), DateTimeOffset.UtcNow, actor,
            request.Reason?.Trim(), loosened, previous, request.Policy);
        return (config with
        {
            DataQuality = dataQuality with
            {
                CrossProvider = request.Policy,
                CrossProviderAudit = (dataQuality.CrossProviderAudit ?? []).Append(audit).ToArray()
            }
        }, audit);
    }

    internal static CrossProviderTolerancePreviewResponse BuildPreview(
        string symbol,
        DateOnly from,
        DateOnly to,
        string baseline,
        string comparison,
        CrossProviderTolerances tolerances,
        CrossSourceBackfillReconciliationResult result)
    {
        // Kinds produced by CrossSourceBackfillReconciliationService.
        static bool IsDrift(string kind) => kind is "ClosePriceDrift" or "VolumeDrift" or "PriceAndVolumeDrift";
        static bool IsMissing(string kind) => kind.EndsWith("MissingEvidence", StringComparison.Ordinal) ||
                                              kind.EndsWith("MissingSession", StringComparison.Ordinal);

        var flagged = new List<CrossProviderTolerancePreviewDay>();
        var informational = 0;
        foreach (var day in result.Discrepancies.Where(d => IsDrift(d.Kind)).GroupBy(d => d.SessionDate).OrderBy(g => g.Key))
        {
            var drift = day.First();
            var priceFlagged = drift.Kind is "ClosePriceDrift" or "PriceAndVolumeDrift";
            var volumeFlagged = drift.Kind is "VolumeDrift" or "PriceAndVolumeDrift";
            if (!priceFlagged && tolerances.VolumeMode == CrossProviderVolumeMode.Informational)
            {
                informational++;
                continue;
            }

            flagged.Add(new CrossProviderTolerancePreviewDay(
                day.Key,
                drift.BaselineClose,
                drift.ComparisonClose,
                drift.CloseDifferenceRatio is { } close ? close * 100m : null,
                volumeFlagged && tolerances.VolumeMode == CrossProviderVolumeMode.Alert && drift.VolumeDifferenceRatio is { } volume
                    ? volume * 100m
                    : null,
                priceFlagged ? "Price" : "Volume"));
        }

        return new CrossProviderTolerancePreviewResponse(
            symbol,
            from,
            to,
            baseline,
            comparison,
            tolerances,
            flagged.Count,
            flagged,
            informational,
            result.Discrepancies.Where(d => IsMissing(d.Kind)).Select(d => d.SessionDate).Distinct().Count(),
            result.ProviderErrors.Select(error => $"{error.Provider}: {error.ErrorType}").ToArray(),
            ProviderCallsPerPreview);
    }

    private static string[] PreviewProviderNames(IEnumerable<IHistoricalDataProvider>? providers)
        => (providers ?? [])
            .Where(provider => !provider.IsSimulated && !string.IsNullOrWhiteSpace(provider.Name))
            .Select(provider => provider.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record CrossProviderTolerancePolicyRequest(CrossProviderTolerancePolicy Policy, string? Reason = null);

public sealed record CrossProviderToleranceBounds(
    double MaxPriceTolerancePercent,
    double MaxVolumeTolerancePercent,
    int MaxBackfillBarsToCompare,
    int MaxAssetClassOverrides,
    int MaxPreviewDays);

public sealed record CrossProviderTolerancePolicyResponse(
    CrossProviderTolerancePolicy Policy,
    CrossProviderTolerancePolicy Defaults,
    CrossProviderToleranceBounds Bounds,
    IReadOnlyList<CrossProviderTolerancePolicyAudit> Audit,
    IReadOnlyList<string> PreviewProviders,
    int ProviderCallsPerPreview);

public sealed record CrossProviderTolerancePolicySaveResponse(
    CrossProviderTolerancePolicy Policy,
    CrossProviderTolerancePolicyAudit Audit,
    Meridian.Contracts.Configuration.ConfigurationChangeStatus ChangeImpact);

public sealed record CrossProviderTolerancePreviewRequest(
    string Symbol,
    string BaselineProvider,
    string ComparisonProvider,
    int Days = CrossProviderToleranceEndpoints.MaxPreviewDays,
    CrossProviderTolerancePolicy? Policy = null,
    string? AssetClass = null);

public sealed record CrossProviderTolerancePreviewDay(
    DateOnly SessionDate,
    decimal? BaselineClose,
    decimal? ComparisonClose,
    decimal? CloseDifferencePercent,
    decimal? VolumeDifferencePercent,
    string FlaggedBy);

public sealed record CrossProviderTolerancePreviewResponse(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string BaselineProvider,
    string ComparisonProvider,
    CrossProviderTolerances Tolerances,
    int FlaggedDays,
    IReadOnlyList<CrossProviderTolerancePreviewDay> Flagged,
    int InformationalDays,
    int MissingSessions,
    IReadOnlyList<string> ProviderErrors,
    int ProviderCallsUsed);
