using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// Maps all backfill API endpoints.
    /// </summary>
    public static void MapBackfillEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions, JsonSerializerOptions jsonOptionsIndented)
    {
        var group = app.MapGroup("").WithTags("Backfill");

        // Get available providers
        group.MapGet(UiApiRoutes.BackfillProviders, (BackfillCoordinator backfill) =>
        {
            var providers = backfill.DescribeProviders();
            return Results.Json(providers, jsonOptions);
        })
        .WithName("GetBackfillProviders")
        .WithDescription("Returns list of available historical data providers for backfill operations.")
        .Produces<BackfillProviderInfo[]>(200);

        // Get last backfill status
        group.MapGet(UiApiRoutes.BackfillStatus, (BackfillCoordinator backfill) =>
        {
            var status = backfill.TryReadLast();
            return status is null
                ? Results.NotFound()
                : Results.Json(status, jsonOptionsIndented);
        })
        .WithName("GetBackfillStatus")
        .WithDescription("Returns the result of the most recent backfill operation, or 404 if none has been run.")
        .Produces<BackfillResult>(200)
        .Produces(404);

        // Preview backfill (dry run - shows what would be fetched)
        group.MapPost(UiApiRoutes.BackfillRun + "/preview", async (BackfillCoordinator backfill, BackfillRequestDto req) =>
        {
            var validation = ValidateBackfillRequest(req);
            if (validation is not null)
                return validation;

            try
            {
                var request = new BackfillRequest(
                    string.IsNullOrWhiteSpace(req.Provider) ? "stooq" : req.Provider!,
                    req.Symbols!,
                    req.From,
                    req.To);

                var preview = await backfill.PreviewAsync(request);
                return Results.Json(preview, jsonOptionsIndented);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Backfill preview failed: {ex.Message}" });
            }
        })
        .WithName("PreviewBackfill")
        .WithDescription("Dry-run preview of a backfill operation showing what data would be fetched.")
        .Produces<BackfillResult>(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Run backfill
        group.MapPost(UiApiRoutes.BackfillRun, async (BackfillCoordinator backfill, BackfillRequestDto req) =>
        {
            var validation = ValidateBackfillRequest(req);
            if (validation is not null)
                return validation;

            try
            {
                var request = new BackfillRequest(
                    string.IsNullOrWhiteSpace(req.Provider) ? "stooq" : req.Provider!,
                    req.Symbols!,
                    req.From,
                    req.To);

                var result = await backfill.RunAsync(request);
                return Results.Json(result, jsonOptionsIndented);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Backfill execution failed: {ex.Message}" });
            }
        })
        .WithName("RunBackfill")
        .WithDescription("Executes a backfill operation for the specified symbols and date range.")
        .Produces<BackfillResult>(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Backfill progress endpoint
        group.MapGet("/api/backfill/progress", (BackfillCoordinator backfill) =>
        {
            var progress = backfill.GetProgress();
            return progress is not null
                ? Results.Json(progress, jsonOptions)
                : Results.Json(new { message = "No active backfill operation", symbols = Array.Empty<object>() }, jsonOptions);
        })
        .WithName("GetBackfillProgress")
        .WithDescription("Returns progress of the currently active backfill operation, if any.")
        .Produces(200);

        // Get provider metadata descriptors
        group.MapGet(UiApiRoutes.BackfillProviderMetadata, () =>
        {
            var metadata = GetKnownProviderMetadata();
            return Results.Json(metadata, jsonOptions);
        })
        .WithName("GetBackfillProviderMetadata")
        .WithDescription("Returns metadata descriptors for all known backfill providers.")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderMetadataDto[]>(200);

        // Get provider statuses (config + health combined, uses defaults when no config available)
        group.MapGet(UiApiRoutes.BackfillProviderStatuses, () =>
        {
            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, null);
            return Results.Json(statuses, jsonOptions);
        })
        .WithName("GetBackfillProviderStatuses")
        .WithDescription("Returns combined status of all backfill providers including config and health.")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderStatusDto[]>(200);

        // Get fallback chain preview (enabled providers only, sorted by priority)
        group.MapGet(UiApiRoutes.BackfillFallbackChain, () =>
        {
            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, null);
            var chain = statuses.Where(s => s.Options.Enabled).ToArray();
            return Results.Json(chain, jsonOptions);
        })
        .WithName("GetBackfillFallbackChain")
        .WithDescription("Returns the effective fallback chain sorted by priority (enabled providers only).")
        .Produces<Meridian.Contracts.Configuration.BackfillProviderStatusDto[]>(200);

        // Dry-run backfill plan
        group.MapPost(UiApiRoutes.BackfillDryRunPlan, async (HttpRequest request) =>
        {
            var body = await request.ReadFromJsonAsync<DryRunPlanRequest>(jsonOptions);
            if (body?.Symbols is null || body.Symbols.Length == 0)
                return Results.BadRequest(new { error = "At least one symbol is required." });

            var metadata = GetKnownProviderMetadata();
            var statuses = BuildProviderStatuses(metadata, null);
            var enabledChain = statuses.Where(s => s.Options.Enabled).ToArray();

            var plan = BuildDryRunPlan(body.Symbols, enabledChain);
            return Results.Json(plan, jsonOptions);
        })
        .WithName("PostBackfillDryRunPlan")
        .WithDescription("Generates a dry-run backfill plan showing which providers would be selected per symbol.")
        .Produces<Meridian.Contracts.Configuration.BackfillDryRunPlanDto>(200)
        .Produces(400);

        // Get audit log (stub — desktop persists locally, server returns empty)
        group.MapGet(UiApiRoutes.BackfillProviderConfigAudit, () =>
        {
            return Results.Json(Array.Empty<Meridian.Contracts.Configuration.ProviderConfigAuditEntryDto>());
        })
        .WithName("GetBackfillProviderConfigAudit")
        .WithDescription("Returns the audit trail of provider configuration changes.")
        .Produces<Meridian.Contracts.Configuration.ProviderConfigAuditEntryDto[]>(200);
    }

    private static Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] GetKnownProviderMetadata()
    {
        return
        [
            new() { ProviderId = "alpaca", DisplayName = "Alpaca", Description = "Bars, trades, and quotes via REST API.", DataTypes = ["Bars", "Trades", "Quotes"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 5, DefaultRateLimitPerMinute = 200 },
            new() { ProviderId = "polygon", DisplayName = "Polygon", Description = "Full market data including aggregates.", DataTypes = ["Bars", "Trades", "Quotes", "Aggregates"], RequiresApiKey = true, FreeTier = false, DefaultPriority = 12, DefaultRateLimitPerMinute = 5 },
            new() { ProviderId = "tiingo", DisplayName = "Tiingo", Description = "Daily bars and end-of-day data.", DataTypes = ["Daily bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 15, DefaultRateLimitPerHour = 500 },
            new() { ProviderId = "finnhub", DisplayName = "Finnhub", Description = "Daily bars with international coverage.", DataTypes = ["Daily bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 20, DefaultRateLimitPerMinute = 60 },
            new() { ProviderId = "stooq", DisplayName = "Stooq", Description = "Free daily bar data. No API key required.", DataTypes = ["Daily bars"], RequiresApiKey = false, FreeTier = true, DefaultPriority = 25 },
            new() { ProviderId = "yahoo", DisplayName = "Yahoo Finance", Description = "Unofficial daily bar data.", DataTypes = ["Daily bars"], RequiresApiKey = false, FreeTier = true, DefaultPriority = 30 },
            new() { ProviderId = "alphavantage", DisplayName = "Alpha Vantage", Description = "Daily bars with strict rate limits.", DataTypes = ["Daily bars"], RequiresApiKey = true, FreeTier = true, DefaultPriority = 35, DefaultRateLimitPerMinute = 5, DefaultRateLimitPerHour = 500 },
            new() { ProviderId = "nasdaqdatalink", DisplayName = "Nasdaq Data Link", Description = "Various market data sets.", DataTypes = ["Various"], RequiresApiKey = true, FreeTier = false, DefaultPriority = 40 },
        ];
    }

    private static Meridian.Contracts.Configuration.BackfillProviderStatusDto[] BuildProviderStatuses(
        Meridian.Contracts.Configuration.BackfillProviderMetadataDto[] metadata,
        Meridian.Contracts.Configuration.BackfillProvidersConfigDto? config)
    {
        return metadata.Select(m =>
        {
            var opts = GetProviderOptionsFromConfig(config, m.ProviderId);
            return new Meridian.Contracts.Configuration.BackfillProviderStatusDto
            {
                Metadata = m,
                Options = opts ?? new Meridian.Contracts.Configuration.BackfillProviderOptionsDto
                {
                    Enabled = true,
                    Priority = m.DefaultPriority,
                    RateLimitPerMinute = m.DefaultRateLimitPerMinute,
                    RateLimitPerHour = m.DefaultRateLimitPerHour,
                },
                EffectiveConfigSource = opts != null ? "user" : "default",
            };
        })
        .OrderBy(s => s.Options.Enabled ? 0 : 1)
        .ThenBy(s => s.Options.Priority ?? s.Metadata.DefaultPriority)
        .ToArray();
    }

    private static Meridian.Contracts.Configuration.BackfillProviderOptionsDto? GetProviderOptionsFromConfig(
        Meridian.Contracts.Configuration.BackfillProvidersConfigDto? config,
        string providerId)
    {
        if (config == null)
            return null;
        return providerId switch
        {
            "alpaca" => config.Alpaca,
            "polygon" => config.Polygon,
            "tiingo" => config.Tiingo,
            "finnhub" => config.Finnhub,
            "stooq" => config.Stooq,
            "yahoo" => config.Yahoo,
            "alphavantage" => config.AlphaVantage,
            "nasdaqdatalink" => config.NasdaqDataLink,
            _ => null,
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

    private static IResult? ValidateBackfillRequest(BackfillRequestDto req)
    {
        if (req.Symbols is null || req.Symbols.Length == 0)
            return Results.BadRequest(new { error = "At least one symbol is required." });

        if (req.Symbols.Length > 100)
            return Results.BadRequest(new { error = "Maximum 100 symbols per backfill request." });

        var invalidSymbols = req.Symbols.Where(s => !SymbolPattern.IsMatch(s)).ToArray();
        if (invalidSymbols.Length > 0)
            return Results.BadRequest(new { error = $"Invalid symbol format: {string.Join(", ", invalidSymbols.Take(5))}. Symbols must be 1-20 alphanumeric characters." });

        if (req.From.HasValue && req.To.HasValue && req.From.Value > req.To.Value)
            return Results.BadRequest(new { error = "From date must be before or equal to To date." });

        if (req.From.HasValue && req.From.Value < new DateOnly(1970, 1, 1))
            return Results.BadRequest(new { error = "From date must be after 1970-01-01." });

        if (req.To.HasValue && req.To.Value > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return Results.BadRequest(new { error = "To date cannot be in the future." });

        return null;
    }
}
