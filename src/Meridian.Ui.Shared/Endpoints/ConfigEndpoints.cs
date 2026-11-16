using System.Text.Json;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
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
        group.AddEndpointFilter(new ProviderConfigurationCredentialFilter());
        var requireModifyConfig = EndpointAuthorization.Require(UserPermission.ModifyConfig);

        // SEC-001: configuration reads expose masked secrets and provider wiring, so every route in
        // this group requires at least a configuration-read permission. Mutation routes additionally
        // require ModifyConfig below. Either ViewConfig or ModifyConfig satisfies the read gate.
        group.RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig);
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            { return await next(context); }
            catch (UiConfigurationValidationException failure)
            {
                return Results.ValidationProblem(failure.Errors.ToDictionary(entry => entry.Key, entry => entry.Value));
            }
        });

        group.MapGet("/api/config/changes", (ConfigStore store) => Results.Json(store.GetChangeStatus(), jsonOptions))
            .WithName("GetConfigurationChangeStatus")
            .WithDescription("Saved setting impacts and changes awaiting provider reconnect or host restart; no setting values are returned.")
            .Produces<ConfigurationChangeStatus>(200);

        // Get full configuration
        group.MapGet(UiApiRoutes.Config, (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(new
            {
                dataRoot = cfg.DataRoot,
                compress = cfg.Compress,
                dataSource = cfg.DataSource.ToString(),
                alpaca = cfg.Alpaca is null
                    ? null
                    : new
                    {
                        keyId = SensitiveValueMasker.Mask(cfg.Alpaca.KeyId),
                        secretKey = SensitiveValueMasker.MaskCompletely(cfg.Alpaca.SecretKey),
                        feed = cfg.Alpaca.Feed,
                        useSandbox = cfg.Alpaca.UseSandbox
                    },
                storage = cfg.Storage,
                symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>(),
                backfill = MaskBackfill(cfg.Backfill),
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
            // The original hand-picked keys keep their established names, defaults and masking.
            var entries = new List<object>();
            var curatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "symbols.count" };
            void AddEntry(string key, string value, string? envVarName, string? defaultValue)
            {
                curatedKeys.Add(key);
                entries.Add(BuildEntry(key, value, envVars, envVarName, defaultValue));
            }

            // Core settings
            AddEntry("dataSource", cfg.DataSource.ToString(), "MDC_DATASOURCE", "IB");
            AddEntry("dataRoot", cfg.DataRoot, "MDC_DATA_ROOT", "data");
            AddEntry("compress", cfg.Compress?.ToString() ?? "null", "MDC_COMPRESS", "null");

            // Alpaca settings
            if (cfg.Alpaca != null)
            {
                AddEntry("alpaca.keyId", SensitiveValueMasker.Mask(cfg.Alpaca.KeyId), "MDC_ALPACA_KEY_ID", null);
                AddEntry("alpaca.secretKey", SensitiveValueMasker.MaskCompletely(cfg.Alpaca.SecretKey), "MDC_ALPACA_SECRET_KEY", null);
                AddEntry("alpaca.feed", cfg.Alpaca.Feed ?? "iex", "MDC_ALPACA_FEED", "iex");
                AddEntry("alpaca.useSandbox", cfg.Alpaca.UseSandbox.ToString(), "MDC_ALPACA_SANDBOX", "False");
            }

            // Storage settings
            var storage = cfg.Storage;
            AddEntry("storage.namingConvention", storage?.NamingConvention ?? "BySymbol", "MDC_STORAGE_NAMING", "BySymbol");
            AddEntry("storage.datePartition", storage?.DatePartition ?? "Daily", "MDC_STORAGE_PARTITION", "Daily");
            AddEntry("storage.retentionDays", storage?.RetentionDays?.ToString() ?? "null", "MDC_STORAGE_RETENTION_DAYS", "null");
            AddEntry("storage.enableParquetSink", storage?.EnableParquetSink.ToString() ?? "False", null, "False");

            // Backfill settings
            var backfill = cfg.Backfill;
            AddEntry("backfill.enabled", backfill?.Enabled.ToString() ?? "False", "MDC_BACKFILL_ENABLED", "False");
            AddEntry("backfill.provider", backfill?.Provider ?? "composite", "MDC_BACKFILL_PROVIDER", "composite");

            // Symbols summary
            entries.Add(new { key = "symbols.count", value = (cfg.Symbols?.Length ?? 0).ToString(), source = "config", impact = ConfigurationChangeImpact.Live });

            // Every remaining AppConfig field, so no section is missing from the effective view.
            foreach (var entry in Meridian.Application.UI.EffectiveConfigurationProjection.Build(cfg, envVars))
            {
                if (!curatedKeys.Contains(entry.Key))
                    entries.Add(new { key = entry.Key, value = entry.Value, source = entry.Source, impact = entry.Impact });
            }

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
        group.MapPost(UiApiRoutes.ConfigDataSource, async (ConfigStore store, DataSourceRequest req, CancellationToken ct) =>
        {
            if (!Enum.TryParse<DataSourceKind>(req.DataSource, ignoreCase: true, out var ds) || !Enum.IsDefined(ds))
                return Results.BadRequest("Invalid DataSource. Use 'IB' or 'Alpaca'.");

            await store.UpdateAsync(cfg => cfg with { DataSource = ds }, ct);

            return Results.Ok(store.GetChangeStatus());
        }).WithName("UpdateDataSource")
        .WithDescription("Updates the active streaming data source (e.g., IB, Alpaca, Polygon).")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        // Update Alpaca settings
        group.MapPost(UiApiRoutes.ConfigAlpaca, async (ConfigStore store, AlpacaOptionsDto alpaca, CancellationToken ct) =>
        {
            var alpacaDomain = alpaca.ToDomain();
            await store.UpdateAsync(cfg => cfg with { Alpaca = alpacaDomain }, ct);
            return Results.Ok(store.GetChangeStatus());
        }).WithName("UpdateAlpaca")
        .WithDescription("Updates Alpaca provider connection settings.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        // Update storage settings
        group.MapPost(UiApiRoutes.ConfigStorage, async (ConfigStore store, StorageSettingsRequest req, CancellationToken ct) =>
        {
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

            await store.UpdateAsync(cfg => cfg with
            {
                DataRoot = sanitizedRoot,
                Compress = req.Compress,
                Storage = storage
            }, ct);
            return Results.Ok(store.GetChangeStatus());
        }).WithName("UpdateStorage")
        .WithDescription("Updates storage settings including root path, naming convention, and compression.")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        // Add or update symbol
        group.MapPost(UiApiRoutes.ConfigSymbols, async (ConfigStore store, SymbolConfig symbol, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol.Symbol))
                return Results.BadRequest("Symbol is required.");

            await store.UpdateAsync(cfg =>
            {
                var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
                var idx = list.FindIndex(s => string.Equals(s.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    list[idx] = symbol;
                else
                    list.Add(symbol);

                return cfg with { Symbols = list.ToArray() };
            }, ct);

            return Results.Ok(store.GetChangeStatus());
        }).WithName("UpsertSymbol")
        .WithDescription("Adds or updates a symbol in the monitoring configuration.")
        .Produces(200).Produces(400).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        // Delete symbol
        group.MapDelete(UiApiRoutes.ConfigSymbols + "/{symbol}", async (ConfigStore store, string symbol, CancellationToken ct) =>
        {
            await store.UpdateAsync(cfg =>
            {
                var list = (cfg.Symbols ?? Array.Empty<SymbolConfig>()).ToList();
                list.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                return cfg with { Symbols = list.ToArray() };
            }, ct);
            return Results.Ok(store.GetChangeStatus());
        }).WithName("DeleteConfigSymbol")
        .WithDescription("Removes a symbol from the monitoring configuration.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);

        // Get derivatives configuration
        group.MapGet(UiApiRoutes.ConfigDerivatives, (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(cfg.Derivatives ?? new DerivativesConfig(), jsonOptions);
        }).WithName("GetDerivatives")
        .WithDescription("Returns the current derivatives trading configuration.")
        .Produces(200);

        // Update derivatives configuration
        group.MapPost(UiApiRoutes.ConfigDerivatives, async (ConfigStore store, DerivativesConfigDto derivatives, CancellationToken ct) =>
        {
            var derivativesDomain = derivatives.ToDomain();
            await store.UpdateAsync(cfg => cfg with { Derivatives = derivativesDomain }, ct);
            return Results.Ok(store.GetChangeStatus());
        }).WithName("UpdateDerivatives")
        .WithDescription("Updates the derivatives trading configuration.")
        .Produces(200).RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(requireModifyConfig)
        .RequirePermission(UserPermission.ModifyConfig);
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

        return new { key, value, source, impact = Meridian.Application.UI.ConfigStore.GetSettingImpact(key) };
    }
    // Clone the response projection: masking must never alter the persisted credentials.
    private static BackfillConfig? MaskBackfill(BackfillConfig? config)
    {
        if (config?.Providers is not { } providers)
            return config;

        return config with
        {
            Providers = providers with
            {
                Alpaca = providers.Alpaca is { } alpaca ? alpaca with
                {
                    KeyId = SensitiveValueMasker.Mask(alpaca.KeyId),
                    SecretKey = SensitiveValueMasker.MaskCompletely(alpaca.SecretKey)
                } : null,
                Nasdaq = providers.Nasdaq is { } nasdaq ? nasdaq with { ApiKey = SensitiveValueMasker.MaskCompletely(nasdaq.ApiKey) } : null,
                OpenFigi = providers.OpenFigi is { } figi ? figi with { ApiKey = SensitiveValueMasker.MaskCompletely(figi.ApiKey) } : null,
                Tiingo = providers.Tiingo is { } tiingo ? tiingo with { ApiToken = SensitiveValueMasker.MaskCompletely(tiingo.ApiToken) } : null,
                Polygon = providers.Polygon is { } polygon ? polygon with { ApiKey = SensitiveValueMasker.MaskCompletely(polygon.ApiKey) } : null,
                AlphaVantage = providers.AlphaVantage is { } alpha ? alpha with { ApiKey = SensitiveValueMasker.MaskCompletely(alpha.ApiKey) } : null,
                Finnhub = providers.Finnhub is { } finnhub ? finnhub with { ApiKey = SensitiveValueMasker.MaskCompletely(finnhub.ApiKey) } : null,
                Fred = providers.Fred is { } fred ? fred with { ApiKey = SensitiveValueMasker.MaskCompletely(fred.ApiKey) } : null
            }
        };
    }

}
