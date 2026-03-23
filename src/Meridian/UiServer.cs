using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Contracts;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Meridian;

/// <summary>
/// Embedded HTTP server for the web dashboard UI.
/// Uses ServiceCompositionRoot for centralized service registration.
/// All endpoints are organized in dedicated endpoint classes in Meridian.Ui.Shared/Endpoints/.
/// </summary>
[ImplementsAdr("ADR-001", "UiServer uses centralized composition root")]
[ImplementsAdr("ADR-004", "Large file decomposition - endpoints extracted to dedicated modules")]
public sealed class UiServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions s_jsonOptionsCompact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly WebApplication _app;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly ILogger<UiServer> _logger;

    /// <summary>
    /// Creates a new UiServer using the centralized ServiceCompositionRoot.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="port">HTTP port to listen on.</param>
    public UiServer(string configPath, int port = 8080)
    {
        var builder = WebApplication.CreateBuilder();

        // Minimize logging from ASP.NET Core
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Use centralized service composition root
        var compositionOptions = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        builder.Services.AddMarketDataServices(compositionOptions);
        builder.Services.AddSingleton<StatusEndpointHandlers>(sp =>
        {
            var pipeline = sp.GetRequiredService<EventPipeline>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();

            return new StatusEndpointHandlers(
                Metrics.GetSnapshot,
                pipeline.GetStatistics,
                () => depthCollector.GetRecentIntegrityEvents(),
                () => null);
        });

        // Register session-based authentication service
        builder.Services.AddSingleton<LoginSessionService>();
        builder.Services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        builder.Services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        builder.Services.AddSingleton<PortfolioReadService>();
        builder.Services.AddSingleton<LedgerReadService>();
        builder.Services.AddSingleton<StrategyRunReadService>();
        builder.Services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        builder.Services.AddSingleton<ReconciliationProjectionService>();
        builder.Services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();

        // Register OpenAPI/Swagger services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Meridian API",
                Version = "v1",
                Description = "REST API for the Meridian system. Provides endpoints for real-time data streaming, " +
                              "historical backfill, storage management, provider configuration, and data quality monitoring.",
                Contact = new OpenApiContact
                {
                    Name = "Meridian Team"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT"
                }
            });

            options.TagActionsBy(api =>
            {
                var path = api.RelativePath ?? string.Empty;
                if (path.StartsWith("api/symbols"))
                    return ["Symbols"];
                if (path.StartsWith("api/storage/quality"))
                    return ["Storage Quality"];
                if (path.StartsWith("api/storage"))
                    return ["Storage"];
                if (path.StartsWith("api/config"))
                    return ["Configuration"];
                if (path.StartsWith("api/backfill"))
                    return ["Backfill"];
                if (path.StartsWith("api/providers"))
                    return ["Providers"];
                if (path.StartsWith("api/quality"))
                    return ["Data Quality"];
                if (path.StartsWith("api/sla"))
                    return ["SLA"];
                if (path.StartsWith("api/maintenance"))
                    return ["Maintenance"];
                if (path.StartsWith("api/packaging"))
                    return ["Packaging"];
                if (path.StartsWith("api/failover"))
                    return ["Failover"];
                if (path.StartsWith("api/export"))
                    return ["Export"];
                if (path.StartsWith("api/diagnostics"))
                    return ["Diagnostics"];
                if (path.StartsWith("api/admin"))
                    return ["Admin"];
                if (path.StartsWith("api/live"))
                    return ["Live Data"];
                if (path.StartsWith("api/replay"))
                    return ["Replay"];
                if (path.StartsWith("api/lean"))
                    return ["Lean Integration"];
                if (path.StartsWith("api/messaging"))
                    return ["Messaging"];
                if (path.StartsWith("api/analytics"))
                    return ["Analytics"];
                if (path.StartsWith("api/historical"))
                    return ["Historical"];
                if (path.StartsWith("api/options"))
                    return ["Options"];
                return ["General"];
            });
        });

        _app = builder.Build();
        _logger = _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<UiServer>();
        SecurityMasterStartup.EnsureDatabaseReady(_app.Services, _logger);
        DirectLendingStartup.EnsureDatabaseReady(_app.Services, _logger);

        // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
        ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(_app.Services);

        // Enable session-based authentication middleware (optional in Development/Test, required elsewhere by default)
        _app.UseStaticFiles();
        _app.UseLoginSessionAuthentication();

        // Enable Swagger middleware
        _app.UseSwagger();
        _app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Meridian API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Meridian - API Documentation";
        });

        ConfigureRoutes();
    }

    private void ConfigureRoutes()
    {
        // ==================== BASIC HEALTH ENDPOINTS ====================
        // Kept inline for simplicity - these are used by container orchestration

        _app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            return Results.Json(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                uptime = uptime.ToString(),
                version = "1.6.1"
            });
        });

        _app.MapGet("/healthz", () => Results.Ok("healthy"));
        _app.MapGet("/ready", () => Results.Ok("ready"));
        _app.MapGet("/readyz", () => Results.Ok("ready"));
        _app.MapGet("/live", () => Results.Ok("alive"));
        _app.MapGet("/livez", () => Results.Ok("alive"));

        // ==================== EXTRACTED ENDPOINT MODULES ====================
        // All API endpoints are organized in dedicated endpoint classes
        // in Meridian.Ui.Shared/Endpoints/ for maintainability
        // This reduces UiServer from 3,030 lines to ~250 lines

        // Index page
        _app.MapIndexEndpoints(s_jsonOptions);

        // Configuration API
        _app.MapConfigEndpoints(s_jsonOptions);

        // Symbol Management API
        _app.MapSymbolEndpoints(s_jsonOptions);

        // Status and Health API
        var statusHandlers = _app.Services.GetRequiredService<StatusEndpointHandlers>();
        _app.MapStatusEndpoints(statusHandlers, s_jsonOptions);
        _app.MapHealthEndpoints(s_jsonOptions);

        // Backfill API
        _app.MapBackfillEndpoints(s_jsonOptions, s_jsonOptionsCompact);
        _app.MapBackfillScheduleEndpoints(s_jsonOptions);
        _app.MapCheckpointEndpoints(s_jsonOptions);

        // Storage API
        _app.MapStorageEndpoints(s_jsonOptions);
        _app.MapStorageQualityEndpoints(s_jsonOptions);

        // Provider API
        _app.MapProviderEndpoints(s_jsonOptions);
        _app.MapProviderExtendedEndpoints(s_jsonOptions);
        _app.MapCppTraderEndpoints();

        // Data Quality API
        var auditTrail = _app.Services.GetService<DroppedEventAuditTrail>();
        _app.MapQualityDropsEndpoints(auditTrail, s_jsonOptions);

        // Subscription API
        _app.MapSubscriptionEndpoints(s_jsonOptions);

        // Watchlist API

        // Diagnostics API
        _app.MapDiagnosticsEndpoints(s_jsonOptions);

        // Historical Data API
        _app.MapHistoricalEndpoints(s_jsonOptions);

        // Data Packaging API
        var config = _app.Services.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load();
        _app.MapPackagingEndpoints(config.DataRoot);

        // Archive Maintenance API
        _app.MapArchiveMaintenanceEndpoints();

        // Maintenance Schedule API
        _app.MapMaintenanceScheduleEndpoints(s_jsonOptions);

        // Failover API
        _app.MapFailoverEndpoints(s_jsonOptions);

        // Lean Integration API
        _app.MapLeanEndpoints(s_jsonOptions);

        // IB-specific API
        _app.MapIBEndpoints(s_jsonOptions);

        // Live Data API
        _app.MapLiveDataEndpoints(s_jsonOptions);

        // Options / Derivatives API
        _app.MapOptionsEndpoints(s_jsonOptions);

        // Messaging Hub API
        _app.MapMessagingEndpoints(s_jsonOptions);

        // Replay API
        _app.MapReplayEndpoints(s_jsonOptions);

        // Sampling API
        _app.MapSamplingEndpoints(s_jsonOptions);

        // Symbol Mapping API
        _app.MapSymbolMappingEndpoints(s_jsonOptions);

        // Time Series Alignment API
        _app.MapAlignmentEndpoints(s_jsonOptions);

        // Analytics API
        _app.MapAnalyticsEndpoints(s_jsonOptions);

        // Admin API
        _app.MapAdminEndpoints(s_jsonOptions);

        // Cron API
        _app.MapCronEndpoints(s_jsonOptions);

        // Export API
        _app.MapExportEndpoints(s_jsonOptions);

        // Canonicalization parity dashboard (Phase 2)
        _app.MapCanonicalizationEndpoints(s_jsonOptions);

        // UI API (includes resilience, quality, SLA, and all other endpoint groups)
        _app.MapUiEndpoints(s_jsonOptions);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _app.StartAsync(ct);
        _logger.LogInformation("UiServer started on {Urls}", string.Join(", ", _app.Urls));
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _app.StopAsync(ct);
        _logger.LogInformation("UiServer stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
