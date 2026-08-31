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
            var providerConfig = store.Load().Backfill?.Providers;
            if (providerConfig is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill provider configuration");

            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, providerConfig);
            return Results.Json(statuses, jsonOptions);
        })
        .WithName("GetBackfillProviderStatuses")
        .WithDescription("Returns the configured status of backfill providers; unavailable configuration fails closed.")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderStatusDto[]>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.ManageProviders);

        // Get fallback chain preview (enabled providers only, sorted by priority)
        group.MapGet(UiApiRoutes.BackfillFallbackChain, (HttpContext context, [FromServices] ConfigStore store) =>
        {
            var providerConfig = store.Load().Backfill?.Providers;
            if (providerConfig is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill provider configuration");

            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, providerConfig);
            var chain = statuses.Where(s => s.Options.Enabled).ToArray();
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
            var enabledChain = statuses.Where(s => s.Options.Enabled).ToArray();

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
    {
        return
        [
            new() { ProviderId = "alpaca", DisplayName = "Alpaca", Description = "Bars, trades, and quotes via REST API.", DataTypes = ["Bars", "Trades", "Quotes"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 5, DefaultRateLimitPerMinute = 200 },
            new() { ProviderId = "polygon", DisplayName = "Polygon", Description = "Full market data including aggregates.", DataTypes = ["Bars", "Trades", "Quotes", "Aggregates"], RequiresApiKey = true, FreeTier = false, DefaultPriority = 12, DefaultRateLimitPerMinute = 5 },
            new() { ProviderId = "tiingo", DisplayName = "Tiingo", Description = "Daily bars and end-of-day data.", DataTypes = ["Daily bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 15, DefaultRateLimitPerHour = 50, SupportedGranularities = ["Daily"] },
            new() { ProviderId = "finnhub", DisplayName = "Finnhub", Description = "Daily bars with international coverage.", DataTypes = ["Daily bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 18, DefaultRateLimitPerMinute = 60, SupportedGranularities = ["Daily"] },
            new() { ProviderId = "stooq", DisplayName = "Stooq", Description = "Free daily bar data. No API key required.", DataTypes = ["Daily bars"], RequiresApiKey = false, FreeTier = true, DefaultPriority = 20, SupportedGranularities = ["Daily"] },
            new() { ProviderId = "yahoo", DisplayName = "Yahoo Finance", Description = "Unofficial daily and intraday bar data.", DataTypes = ["Daily bars", "Intraday bars", "Aggregates"], RequiresApiKey = false, FreeTier = true, DefaultPriority = 10, DefaultRateLimitPerHour = 2000, SupportedGranularities = ["Daily", "1Min", "5Min", "15Min", "30Min", "Hourly", "4Hour"], FeatureFlags = new Dictionary<string, bool> { ["supportsIntraday"] = true, ["unofficial"] = true } },
            new() { ProviderId = "alphavantage", DisplayName = "Alpha Vantage", Description = "Daily and intraday bars with strict rate limits.", DataTypes = ["Daily bars", "Intraday bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 25, DefaultRateLimitPerMinute = 5, SupportedGranularities = ["Daily", "1Min", "5Min", "15Min", "30Min", "Hourly"] },
            new() { ProviderId = "nasdaqdatalink", DisplayName = "Nasdaq Data Link", Description = "Various market data sets.", DataTypes = ["Various"], RequiresApiKey = true, FreeTier = false, DefaultPriority = 30, SupportedGranularities = ["Daily"] },
        ];
    }

    private static Meridian.Contracts.Configuration.BackfillProviderStatusDto[] BuildProviderStatuses(
        Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] metadata,
        BackfillProvidersConfig config)
    {
        return metadata.Select(m =>
        {
            var opts = GetProviderOptionsFromConfig(config, m.ProviderId);
            return new Meridian.Contracts.Configuration.BackfillProviderStatusDto
            {
                Metadata = m,
                Options = opts,
                EffectiveConfigSource = HasExplicitProviderConfig(config, m.ProviderId)
                    ? "user"
                    : "default",
            };
        })
        .OrderBy(s => s.Options.Enabled ? 0 : 1)
        .ThenBy(s => s.Options.Priority ?? s.Metadata.DefaultPriority)
        .ToArray();
    }

    internal static Meridian.Contracts.Configuration.BackfillProviderOptionsDto GetProviderOptionsFromConfig(
        BackfillProvidersConfig config,
        string providerId)
    {
        return providerId switch
        {
            "alpaca" => ToOptions(
                config.Alpaca?.Enabled ?? true,
                config.Alpaca?.Priority ?? 5,
                rateLimitPerMinute: config.Alpaca?.RateLimitPerMinute ?? 200),
            "polygon" => ToOptions(
                config.Polygon?.Enabled ?? true,
                config.Polygon?.Priority ?? 12,
                rateLimitPerMinute: config.Polygon?.RateLimitPerMinute ?? 5),
            "tiingo" => ToOptions(
                config.Tiingo?.Enabled ?? true,
                config.Tiingo?.Priority ?? 15,
                rateLimitPerHour: config.Tiingo?.RateLimitPerHour ?? 50),
            "finnhub" => ToOptions(
                config.Finnhub?.Enabled ?? true,
                config.Finnhub?.Priority ?? 18,
                rateLimitPerMinute: config.Finnhub?.RateLimitPerMinute ?? 60),
            "stooq" => ToOptions(
                config.Stooq?.Enabled ?? true,
                config.Stooq?.Priority ?? 20),
            "yahoo" => ToOptions(
                config.Yahoo?.Enabled ?? true,
                config.Yahoo?.Priority ?? 10,
                rateLimitPerHour: config.Yahoo?.RateLimitPerHour ?? 2000),
            "alphavantage" => ToOptions(
                config.AlphaVantage?.Enabled ?? true,
                config.AlphaVantage?.Priority ?? 25,
                rateLimitPerMinute: config.AlphaVantage?.RateLimitPerMinute ?? 5),
            "nasdaqdatalink" => ToOptions(
                config.Nasdaq?.Enabled ?? true,
                config.Nasdaq?.Priority ?? 30),
            _ => throw new InvalidOperationException($"Unsupported backfill provider metadata id '{providerId}'.")
        };
    }

    private static bool HasExplicitProviderConfig(BackfillProvidersConfig config, string providerId)
        => providerId switch
        {
            "alpaca" => config.Alpaca is not null,
            "polygon" => config.Polygon is not null,
            "tiingo" => config.Tiingo is not null,
            "finnhub" => config.Finnhub is not null,
            "stooq" => config.Stooq is not null,
            "yahoo" => config.Yahoo is not null,
            "alphavantage" => config.AlphaVantage is not null,
            "nasdaqdatalink" => config.Nasdaq is not null,
            _ => false
        };

    private static Meridian.Contracts.Configuration.BackfillProviderOptionsDto ToOptions(
        bool enabled,
        int priority,
        int? rateLimitPerMinute = null,
        int? rateLimitPerHour = null)
        => new()
        {
            Enabled = enabled,
            Priority = priority,
            RateLimitPerMinute = rateLimitPerMinute,
            RateLimitPerHour = rateLimitPerHour
        };

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
