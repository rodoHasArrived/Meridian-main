using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
// Import extension methods for DTO to domain conversion
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering configuration API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>
    /// Maps all configuration-related API endpoints.
    /// </summary>
    public static void MapConfigEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Configuration");

        // Get full configuration
        group.MapGet(UiApiRoutes.Config, (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(new
            {
                dataRoot = cfg.DataRoot,
                compress = cfg.Compress,
                dataSource = cfg.DataSource.ToString(),
                alpaca = cfg.Alpaca,
                storage = cfg.Storage,
                symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>(),
                backfill = cfg.Backfill,
                derivatives = cfg.Derivatives
            }, jsonOptions);
        }).WithName("GetConfig")
        .WithDescription("Returns the full application configuration including symbols, storage settings, and provider options.")
        .Produces(200);

        // Get effective configuration with source annotations
        group.MapGet(UiApiRoutes.ConfigEffective, (ConfigStore store) =>
        {
            var cfg = store.Load();
            var envOverride = new ConfigEnvironmentOverride();
            var envVars = envOverride.GetRecognizedVariables();

            // Build effective config entries with source annotations
            var entries = new List<object>();

            // Core settings
            entries.Add(BuildEntry("dataSource", cfg.DataSource.ToString(), envVars, "MDC_DATASOURCE", "IB"));
            entries.Add(BuildEntry("dataRoot", cfg.DataRoot, envVars, "MDC_DATA_ROOT", "data"));
            entries.Add(BuildEntry("compress", cfg.Compress?.ToString() ?? "null", envVars, "MDC_COMPRESS", "null"));

            // Alpaca settings
            if (cfg.Alpaca != null)
            {
                entries.Add(BuildEntry("alpaca.keyId", SensitiveValueMasker.Mask(cfg.Alpaca.KeyId), envVars, "MDC_ALPACA_KEY_ID", null));
                entries.Add(BuildEntry("alpaca.secretKey", SensitiveValueMasker.MaskCompletely(cfg.Alpaca.SecretKey), envVars, "MDC_ALPACA_SECRET_KEY", null));
                entries.Add(BuildEntry("alpaca.feed", cfg.Alpaca.Feed ?? "iex", envVars, "MDC_ALPACA_FEED", "iex"));
                entries.Add(BuildEntry("alpaca.useSandbox", cfg.Alpaca.UseSandbox.ToString(), envVars, "MDC_ALPACA_SANDBOX", "False"));
            }

            // Storage settings
            var storage = cfg.Storage;
            entries.Add(BuildEntry("storage.namingConvention", storage?.NamingConvention ?? "BySymbol", envVars, "MDC_STORAGE_NAMING", "BySymbol"));
            entries.Add(BuildEntry("storage.datePartition", storage?.DatePartition ?? "Daily", envVars, "MDC_STORAGE_PARTITION", "Daily"));
            entries.Add(BuildEntry("storage.retentionDays", storage?.RetentionDays?.ToString() ?? "null", envVars, "MDC_STORAGE_RETENTION_DAYS", "null"));
            entries.Add(BuildEntry("storage.enableParquetSink", storage?.EnableParquetSink.ToString() ?? "False", envVars, null, "False"));

            // Backfill settings
            var backfill = cfg.Backfill;
            entries.Add(BuildEntry("backfill.enabled", backfill?.Enabled.ToString() ?? "False", envVars, "MDC_BACKFILL_ENABLED", "False"));
            entries.Add(BuildEntry("backfill.provider", backfill?.Provider ?? "composite", envVars, "MDC_BACKFILL_PROVIDER", "composite"));

            // Symbols summary
            entries.Add(new { key = "symbols.count", value = (cfg.Symbols?.Length ?? 0).ToString(), source = "config" });

            // Environment overrides summary
            var activeOverrides = envVars.Where(v => v.IsSet).Select(v => new
            {
                envVar = v.EnvironmentVariable,
                configPath = v.ConfigPath,
                isSensitive = v.IsSensitive
            });

            return Results.Json(new
            {
                timestamp = DateTimeOffset.UtcNow,
                entries,
                environmentOverrides = activeOverrides
            }, jsonOptions);
        }).WithName("GetConfigEffective")
        .WithDescription("Returns the resolved effective configuration with source annotations showing where each value comes from.")
        .Produces(200);

        // Update data source
        group.MapPost(UiApiRoutes.ConfigDataSource, async (ConfigStore store, DataSourceRequest req) =>
        {
            var cfg = store.Load();

            if (!Enum.TryParse<DataSourceKind>(req.DataSource, ignoreCase: true, out var ds))
                return Results.BadRequest("Invalid DataSource. Use 'IB' or 'Alpaca'.");

            var next = cfg with { DataSource = ds };
            await store.SaveAsync(next);

            return Results.Ok();
        }).WithName("UpdateDataSource")
        .WithDescription("Updates the active streaming data source (e.g., IB, Alpaca, Polygon).")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Update Alpaca settings
        group.MapPost(UiApiRoutes.ConfigAlpaca, async (ConfigStore store, AlpacaOptionsDto alpaca) =>
        {
            var cfg = store.Load();
            var next = cfg with { Alpaca = alpaca.ToDomain() };
            await store.SaveAsync(next);
            return Results.Ok();
        }).WithName("UpdateAlpaca")
        .WithDescription("Updates Alpaca provider connection settings.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Update storage settings
        group.MapPost(UiApiRoutes.ConfigStorage, async (ConfigStore store, StorageSettingsRequest req) =>
        {
            var cfg = store.Load();
            var storage = new StorageConfig(
                NamingConvention: req.NamingConvention ?? "BySymbol",
                DatePartition: req.DatePartition ?? "Daily",
                IncludeProvider: req.IncludeProvider,
                FilePrefix: string.IsNullOrWhiteSpace(req.FilePrefix) ? null : req.FilePrefix,
                Profile: string.IsNullOrWhiteSpace(req.Profile) ? null : req.Profile
            );
            var sanitizedRoot = PathValidation.SanitizeDataRoot(req.DataRoot);
            if (sanitizedRoot is null)
                return Results.BadRequest("Invalid DataRoot: must be a relative path without traversal sequences.");

            var next = cfg with
            {
                DataRoot = sanitizedRoot,
                Compress = req.Compress,
                Storage = storage
            };
            await store.SaveAsync(next);
            return Results.Ok();
        }).WithName("UpdateStorage")
        .WithDescription("Updates storage settings including root path, naming convention, and compression.")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Add or update symbol
        group.MapPost(UiApiRoutes.ConfigSymbols, async (ConfigStore store, SymbolConfig symbol) =>
        {
            if (string.IsNullOrWhiteSpace(symbol.Symbol))
                return Results.BadRequest("Symbol is required.");

            var cfg = store.Load();

            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            var idx = list.FindIndex(s => string.Equals(s.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = symbol;
            else
                list.Add(symbol);

            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);

            return Results.Ok();
        }).WithName("UpsertSymbol")
        .WithDescription("Adds or updates a symbol in the monitoring configuration.")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Delete symbol
        group.MapDelete(UiApiRoutes.ConfigSymbols + "/{symbol}", async (ConfigStore store, string symbol) =>
        {
            var cfg = store.Load();
            var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
            list.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            var next = cfg with { Symbols = list.ToArray() };
            await store.SaveAsync(next);
            return Results.Ok();
        }).WithName("DeleteSymbol")
        .WithDescription("Removes a symbol from the monitoring configuration.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get derivatives configuration
        group.MapGet(UiApiRoutes.ConfigDerivatives, (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(cfg.Derivatives ?? new Application.Config.DerivativesConfig(), jsonOptions);
        }).WithName("GetDerivatives")
        .WithDescription("Returns the current derivatives trading configuration.")
        .Produces(200);

        // Update derivatives configuration
        group.MapPost(UiApiRoutes.ConfigDerivatives, async (ConfigStore store, DerivativesConfigDto derivatives) =>
        {
            var cfg = store.Load();
            var next = cfg with { Derivatives = derivatives.ToDomain() };
            await store.SaveAsync(next);
            return Results.Ok();
        }).WithName("UpdateDerivatives")
        .WithDescription("Updates the derivatives trading configuration.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static object BuildEntry(
        string key,
        string value,
        IReadOnlyList<EnvironmentOverrideInfo> envVars,
        string? envVarName,
        string? defaultValue)
    {
        string source;

        if (envVarName != null)
        {
            var envInfo = envVars.FirstOrDefault(v =>
                string.Equals(v.EnvironmentVariable, envVarName, StringComparison.OrdinalIgnoreCase));
            if (envInfo?.IsSet == true)
                source = $"env:{envVarName}";
            else if (value == defaultValue)
                source = "default";
            else
                source = "config";
        }
        else
        {
            source = value == defaultValue ? "default" : "config";
        }

        return new { key, value, source };
    }
}
