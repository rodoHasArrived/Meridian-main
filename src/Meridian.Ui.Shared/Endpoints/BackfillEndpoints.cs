using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering backfill-related API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
public static class BackfillEndpoints
{
    // Symbols should be 1-20 uppercase alphanumeric chars, dots, or hyphens
    private static readonly Regex SymbolPattern = new(@"^[A-Za-z0-9.\-]{1,20}$", RegexOptions.Compiled);
    private const int MaxIntradaySpanDays = 31;
    private const int MaxIntradaySymbolCount = 10;

    /// <summary>
    /// Maps all backfill API endpoints.
    /// </summary>
    public static void MapBackfillEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions, JsonSerializerOptions jsonOptionsIndented)
    {
        var group = app.MapGroup("").WithTags("Backfill");
        group.RequireWorkstationTenantScope();

        // Get available providers
        group.MapGet(UiApiRoutes.BackfillProviders, ([FromServices] BackfillCoordinator? backfill, HttpContext context) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            var providers = backfill.DescribeProviders();
            return Results.Json(providers, jsonOptions);
        })
        .WithName("GetBackfillProviders")
        .WithDescription("Returns list of available historical data providers for backfill operations.")
        .Produces<BackfillProviderInfo[]>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Get last backfill status
        group.MapGet(UiApiRoutes.BackfillStatus, ([FromServices] BackfillCoordinator? backfill, HttpContext context) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            var status = backfill.TryReadLast();
            return status is null
                ? ApiProblemDetails.NotFound(context, "No completed backfill operation was found.")
                : Results.Json(status, jsonOptionsIndented);
        })
        .WithName("GetBackfillStatus")
        .WithDescription("Returns the result of the most recent backfill operation, or 404 if none has been run.")
        .Produces<BackfillResult>(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Preview backfill (dry run - shows what would be fetched)
        group.MapPost(UiApiRoutes.BackfillRunPreview, async (
            HttpContext context,
            [FromServices] BackfillCoordinator? backfill,
            [FromServices] ILoggerFactory loggerFactory,
            BackfillRequestDto req,
            CancellationToken ct) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            BackfillRequest request;
            try
            {
                request = CreateBackfillRequest(req);
                var validation = ValidateBackfillRequest(context, req, request, backfill);
                if (validation is not null)
                    return validation;
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblemDetails.Validation(context, "request", ex.Message);
            }

            try
            {
                var preview = await backfill.PreviewAsync(request, ct).ConfigureAwait(false);
                return Results.Json(preview, jsonOptionsIndented);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ApiProblemDetails.Timeout(context);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(BackfillEndpoints))
                    .LogError(ex, "Backfill preview failed.");
                return ApiProblemDetails.Internal(
                    context,
                    "The backfill preview could not be completed.");
            }
        })
        .WithName("PreviewBackfill")
        .WithDescription("Dry-run preview of a backfill operation showing what data would be fetched.")
        .Produces<Meridian.Application.Backfill.BackfillPreviewResult>(200)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Run backfill
        group.MapPost(UiApiRoutes.BackfillRun, async (
            HttpContext context,
            [FromServices] BackfillCoordinator? backfill,
            [FromServices] ILoggerFactory loggerFactory,
            BackfillRequestDto req,
            CancellationToken ct) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            BackfillRequest request;
            try
            {
                request = CreateBackfillRequest(req);
                var validation = ValidateBackfillRequest(context, req, request, backfill);
                if (validation is not null)
                    return validation;
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblemDetails.Validation(context, "request", ex.Message);
            }

            try
            {
                var result = await backfill.RunAsync(request, ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptionsIndented);
            }
            catch (InvalidOperationException)
            {
                return ApiProblemDetails.Conflict(
                    context,
                    "Another backfill operation is already active.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ApiProblemDetails.Timeout(context);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(BackfillEndpoints))
                    .LogError(ex, "Backfill execution failed.");
                return ApiProblemDetails.Internal(
                    context,
                    "The backfill operation could not be completed.");
            }
        })
        .WithName("RunBackfill")
        .WithDescription("Executes a backfill operation for the specified symbols and date range.")
        .Produces<BackfillResult>(200)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Backfill progress endpoint
        group.MapGet(UiApiRoutes.BackfillProgress, ([FromServices] BackfillCoordinator? backfill, HttpContext context) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            var progress = backfill.GetProgress() ?? new BackfillRunProgressResponse(
                LastRun: null,
                IsActive: false,
                ProviderProgress: new BackfillProviderProgressSnapshotDto(
                    Symbols: new Dictionary<string, BackfillProviderSymbolProgressDto>(StringComparer.OrdinalIgnoreCase),
                    RecentProviderAttempts: Array.Empty<BackfillProviderAttemptProgressDto>(),
                    OverallPercentComplete: 0,
                    TotalSymbols: 0,
                    CompletedSymbols: 0,
                    FailedSymbols: 0,
                    DroppedProviderNotifications: 0,
                    Timestamp: DateTimeOffset.UtcNow),
                Timestamp: DateTimeOffset.UtcNow);
            return Results.Json(progress, jsonOptions);
        })
        .WithName("GetBackfillProgress")
        .WithDescription("Returns the latest run plus bounded, live provider-attempt progress for the current symbol and range.")
        .Produces<BackfillRunProgressResponse>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Get provider metadata descriptors
        group.MapGet(UiApiRoutes.BackfillProviderMetadata, () =>
        {
            var metadata = GetKnownProviderMetadata();
            return Results.Json(metadata, jsonOptions);
        })
        .WithName("GetBackfillProviderMetadata")
        .WithDescription("Returns metadata descriptors for all known backfill providers.")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderMetadataDto[]>(200)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ManageProviders);

        // Get provider statuses from the effective host configuration.
        group.MapGet(UiApiRoutes.BackfillProviderStatuses, (HttpContext context, [FromServices] ConfigStore store) =>
        {
            var providerConfig = store.Load().Backfill?.Providers ?? new BackfillProvidersConfig();
            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, providerConfig);
            return Results.Json(statuses, jsonOptions);
        })
        .WithName("GetBackfillProviderStatuses")
        .WithDescription("Returns configured and catalog-default provider options without implying runtime activation.")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderStatusDto[]>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ManageProviders);

        // Secret-free provider edits preserve all unrelated host configuration under the store lock.
        group.MapPut(UiApiRoutes.BackfillProviders + "/{id}", async (
            string id, BackfillProviderEdit request, HttpContext context,
            [FromServices] ConfigStore store, CancellationToken ct) =>
        {
            if (!BackfillProviderCatalog.Providers.Any(p => p.Id == id))
                return ApiProblemDetails.NotFound(context, "Unknown or non-editable provider.");
            try
            {
                var saved = await store.UpdateAsync(config =>
                {
                    var backfill = config.Backfill ?? new BackfillConfig();
                    var providers = BackfillProviderCatalog.Apply(backfill.Providers ?? new(), id, request);
                    var updated = config with { Backfill = backfill with { Providers = providers } };
                    return (updated, GetProviderOptionsFromConfig(providers, id));
                }, ct).ConfigureAwait(false);
                return Results.Json(new
                {
                    providerId = id,
                    options = saved,
                    changeImpact = store.GetChangeStatus()
                }, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return ApiProblemDetails.Validation(context, "provider", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblemDetails.Validation(context, "configuration", ex.Message);
            }
        })
        .WithName("UpdateBackfillProvider")
        .WithDescription("Updates secret-free backfill options within the declared vendor-tier ceiling; requires a host restart.")
        .ProducesValidationProblem()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageProviders)
        .RequirePermission(UserPermission.ModifyConfig);

        // Get fallback chain preview (enabled providers only, sorted by priority)
        group.MapGet(UiApiRoutes.BackfillFallbackChain, (HttpContext context, [FromServices] ConfigStore store) =>
        {
            var providerConfig = store.Load().Backfill?.Providers;
            if (providerConfig is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill provider configuration");

            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, providerConfig);
            var chain = statuses.Where(s => s.Options.Enabled && s.Metadata.ProviderId != "openfigi").ToArray();
            return Results.Json(chain, jsonOptions);
        })
        .WithName("GetBackfillFallbackChain")
        .WithDescription("Returns the effective fallback chain sorted by priority (enabled providers only).")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderStatusDto[]>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ManageProviders);

        // Dry-run backfill plan
        group.MapPost(UiApiRoutes.BackfillDryRunPlan, async (HttpContext context, [FromServices] ConfigStore store) =>
        {
            var body = await context.Request
                .ReadFromJsonAsync<DryRunPlanRequest>(jsonOptions, context.RequestAborted)
                .ConfigureAwait(false);
            if (body?.Symbols is null || body.Symbols.Length == 0)
                return ApiProblemDetails.Validation(
                    context,
                    "symbols",
                    "At least one symbol is required.");

            var providerConfig = store.Load().Backfill?.Providers;
            if (providerConfig is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill provider configuration");

            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, providerConfig);
            var enabledChain = statuses.Where(s => s.Options.Enabled && s.Metadata.ProviderId != "openfigi").ToArray();

            var plan = BuildDryRunPlan(body.Symbols, enabledChain);
            return Results.Json(plan, jsonOptions);
        })
        .WithName("PostBackfillDryRunPlan")
        .WithDescription("Generates a dry-run backfill plan showing which providers would be selected per symbol.")
        .Produces<Meridian.Contracts.Configuration.BackfillDryRunPlanDto>(200)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Get provider configuration audit log when the host registers an audit reader.
        group.MapGet(UiApiRoutes.BackfillProviderConfigAudit, (HttpContext context, IServiceProvider services) =>
        {
            var auditReader = services.GetService<IBackfillProviderConfigAuditReader>();
            if (auditReader is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill provider configuration audit");

            var entries = auditReader.GetAuditLog();
            return Results.Json(entries.Select(static entry => new Meridian.Contracts.Configuration.ProviderConfigAuditEntryDto
            {
                Timestamp = entry.Timestamp,
                ProviderId = entry.ProviderId,
                Action = entry.Action,
                Source = entry.Source
            }), jsonOptions);
        })
        .WithName("GetBackfillProviderConfigAudit")
        .WithDescription("Returns the sanitized audit trail of provider configuration changes.")
        .Produces<Meridian.Contracts.Configuration.ProviderConfigAuditEntryDto[]>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequirePermission(UserPermission.ManageProviders);
    }

    private static Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] GetKnownProviderMetadata()
        => BackfillProviderCatalog.Describe();

    private static Meridian.Contracts.Configuration.BackfillProviderStatusDto[] BuildProviderStatuses(
        Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] metadata,
        BackfillProvidersConfig config)
        => metadata.Select(m => new Meridian.Contracts.Configuration.BackfillProviderStatusDto
        {
            Metadata = m,
            Options = GetProviderOptionsFromConfig(config, m.ProviderId),
            EffectiveConfigSource = HasExplicitProviderConfig(config, m.ProviderId) ? "user" : "default"
        }).OrderBy(s => s.Options.Enabled ? 0 : 1).ThenBy(s => s.Options.Priority).ToArray();

    private static bool HasExplicitProviderConfig(BackfillProvidersConfig config, string id) => id switch
    {
        "alpaca" => config.Alpaca is not null,
        "polygon" => config.Polygon is not null,
        "tiingo" => config.Tiingo is not null,
        "finnhub" => config.Finnhub is not null,
        "stooq" => config.Stooq is not null,
        "yahoo" => config.Yahoo is not null,
        "alphavantage" => config.AlphaVantage is not null,
        "nasdaqdatalink" => config.Nasdaq is not null,
        "twelvedata" => config.TwelveData is not null,
        "openfigi" => config.OpenFigi is not null,
        _ => false
    };

    internal static Meridian.Contracts.Configuration.BackfillProviderOptionsDto GetProviderOptionsFromConfig(
        BackfillProvidersConfig config, string providerId)
    {
        var edit = BackfillProviderCatalog.Read(config, providerId);
        return new Meridian.Contracts.Configuration.BackfillProviderOptionsDto
        {
            Enabled = edit.Enabled,
            Priority = edit.Priority,
            TierId = edit.TierId,
            RateLimitPerMinute = edit.RateLimitPerMinute,
            RateLimitPerHour = edit.RateLimitPerHour
        };
    }

    private static Meridian.Contracts.Configuration.BackfillDryRunPlanDto BuildDryRunPlan(
        string[] symbols,
        Meridian.Contracts.Configuration.BackfillProviderStatusDto[] enabledChain)
    {
        if (enabledChain.Length == 0)
        {
            return new Meridian.Contracts.Configuration.BackfillDryRunPlanDto
            {
                ValidationErrors = ["No enabled providers available. Enable at least one provider."],
            };
        }

        var sequence = enabledChain.Select(c => c.Metadata.ProviderId).ToArray();
        var plans = symbols.Select(s => new Meridian.Contracts.Configuration.BackfillSymbolPlanDto
        {
            Symbol = s,
            ProviderSequence = sequence,
            SelectedProvider = sequence[0],
            Reason = $"Highest priority enabled provider (priority {enabledChain[0].Options.Priority ?? enabledChain[0].Metadata.DefaultPriority})",
        }).ToArray();

        return new Meridian.Contracts.Configuration.BackfillDryRunPlanDto
        {
            Symbols = plans,
        };
    }

    private static IResult? ValidateBackfillRequest(
        HttpContext context,
        BackfillRequestDto req,
        BackfillRequest request,
        BackfillCoordinator backfill)
    {
        if (req.Symbols is null || req.Symbols.Length == 0)
            return ApiProblemDetails.Validation(context, "symbols", "At least one symbol is required.");

        if (req.Symbols.Length > 100)
            return ApiProblemDetails.Validation(context, "symbols", "Maximum 100 symbols per backfill request.");

        var invalidSymbols = req.Symbols.Where(s => !SymbolPattern.IsMatch(s)).ToArray();
        if (invalidSymbols.Length > 0)
            return ApiProblemDetails.Validation(
                context,
                "symbols",
                $"Invalid symbol format: {string.Join(", ", invalidSymbols.Take(5))}. Symbols must be 1-20 alphanumeric characters.");

        if (req.From.HasValue && req.To.HasValue && req.From.Value > req.To.Value)
            return ApiProblemDetails.Validation(context, "from", "From date must be before or equal to To date.");

        if (req.From.HasValue && req.From.Value < new DateOnly(1970, 1, 1))
            return ApiProblemDetails.Validation(context, "from", "From date must be after 1970-01-01.");

        if (req.To.HasValue && req.To.Value > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return ApiProblemDetails.Validation(context, "to", "To date cannot be in the future.");

        if (request.Granularity.IsIntraday())
        {
            if (req.Symbols.Length > MaxIntradaySymbolCount)
            {
                return ApiProblemDetails.Validation(
                    context,
                    "symbols",
                    $"Intraday backfill supports at most {MaxIntradaySymbolCount} symbols per request.");
            }

            if (req.From.HasValue && req.To.HasValue)
            {
                var spanDays = req.To.Value.DayNumber - req.From.Value.DayNumber + 1;
                if (spanDays > MaxIntradaySpanDays)
                {
                    return ApiProblemDetails.Validation(
                        context,
                        "from",
                        $"Intraday backfill date range cannot exceed {MaxIntradaySpanDays} days.");
                }
            }
        }

        try
        {
            backfill.ValidateRequest(request);
        }
        catch (InvalidOperationException ex)
        {
            return ApiProblemDetails.Validation(context, "request", ex.Message);
        }

        return null;
    }

    private static BackfillRequest CreateBackfillRequest(BackfillRequestDto req)
    {
        var granularity = string.IsNullOrWhiteSpace(req.Granularity)
            ? DataGranularity.Daily
            : DataGranularityExtensions.TryParseValue(req.Granularity, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Unsupported granularity '{req.Granularity}'. Use Daily, Hourly, 1Min, 5Min, 15Min, 30Min, or 4Hour.");

        return new BackfillRequest(
            string.IsNullOrWhiteSpace(req.Provider) ? "stooq" : req.Provider!,
            req.Symbols ?? Array.Empty<string>(),
            req.From,
            req.To,
            granularity);
    }
}
