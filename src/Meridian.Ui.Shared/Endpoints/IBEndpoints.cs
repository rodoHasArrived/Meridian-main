using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering Interactive Brokers-specific API endpoints.
/// Exposes IB connection status, error code reference, and API limit documentation.
/// </summary>
public static class IBEndpoints
{
    /// <summary>
    /// Maps all Interactive Brokers-specific API endpoints.
    /// </summary>
    public static void MapIBEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Interactive Brokers");

        // IB provider status - shows connection mode, capabilities, and simulation state
        group.MapGet(UiApiRoutes.IBStatus, (ConfigStore configStore) =>
        {
            var config = configStore.Load();
            var socketConfig = config.IB;
            var clientPortalConfig = config.IBClientPortal;
            var buildMode = GetBuildMode();
            var runtimeTarget = socketConfig?.UsePaperTrading ?? true ? "paper" : "live";
            var socketReady = socketConfig is not null
                && !string.IsNullOrWhiteSpace(socketConfig.Host)
                && socketConfig.Port > 0
                && socketConfig.Port <= 65535;
            var clientPortalReady = clientPortalConfig is { Enabled: true }
                && Uri.TryCreate(clientPortalConfig.BaseUrl, UriKind.Absolute, out var baseUri)
                && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps);
            var isIBApiAvailable = buildMode is "smoke" or "vendor";

            return Results.Json(new
            {
                provider = "Interactive Brokers",
                ibApiAvailable = isIBApiAvailable,
                buildMode,
                runtimeTarget,
                buildInstructions = buildMode switch
                {
                    "guidance" => "Build with -p:EnableIbApiVendor=true (preferred) and place the official vendor SDK under external/IBApi. Legacy -p:DefineConstants=IBAPI remains supported. Use -p:EnableIbApiSmoke=true only for compile-only verification.",
                    "smoke" => "This build includes the compile-only smoke path. Rebuild with -p:EnableIbApiVendor=true and the official vendor SDK for real TWS/Gateway connectivity.",
                    _ => null
                },
                socket = new
                {
                    configured = socketConfig is not null,
                    ready = socketReady,
                    host = socketConfig?.Host,
                    port = socketConfig?.Port,
                    clientId = socketConfig?.ClientId,
                    paper = socketConfig?.UsePaperTrading ?? true
                },
                clientPortal = new
                {
                    enabled = clientPortalConfig?.Enabled ?? false,
                    ready = clientPortalReady,
                    baseUrl = clientPortalConfig?.BaseUrl,
                    allowSelfSignedCertificates = clientPortalConfig?.AllowSelfSignedCertificates ?? false
                },
                connectionPorts = new
                {
                    twsPaper = IBApiLimits.TwsPaperPort,
                    twsLive = IBApiLimits.TwsLivePort,
                    gatewayPaper = IBApiLimits.GatewayPaperPort,
                    gatewayLive = IBApiLimits.GatewayLivePort
                },
                capabilities = new
                {
                    trades = true,
                    quotes = true,
                    depth = true,
                    historicalBars = true,
                    intradayBars = isIBApiAvailable,
                    tickByTick = isIBApiAvailable,
                    maxDepthLevels = 10,
                    maxMarketDataLines = IBApiLimits.DefaultMarketDataLines
                },
                freeDataNotes = new[]
                {
                    "Free streaming from Cboe One + IEX (non-consolidated, not NBBO)",
                    "Up to 100 free snapshot quotes per month",
                    "Historical data requires active streaming subscription",
                    $"Account minimum: ${IBApiLimits.MinAccountBalanceForMarketData}"
                }
            }, jsonOptions);
        })
        .WithName("GetIBStatus")
        .Produces(200);

        // IB error code reference - returns all known error codes with descriptions
        group.MapGet(UiApiRoutes.IBErrorCodes, () =>
        {
            var errors = IBErrorCodeMap.GetAll();
            var errorList = errors.Select(kvp => new
            {
                code = kvp.Key,
                description = kvp.Value.Description,
                severity = kvp.Value.Severity.ToString().ToLowerInvariant(),
                remediation = kvp.Value.Remediation
            }).OrderBy(e => e.code).ToArray();

            return Results.Json(new
            {
                errorCodes = errorList,
                totalCount = errorList.Length,
                documentation = "https://interactivebrokers.github.io/tws-api/message_codes.html"
            }, jsonOptions);
        })
        .WithName("GetIBErrorCodes")
        .Produces(200);

        // IB API limits reference - returns rate limits and constraints
        group.MapGet(UiApiRoutes.IBLimits, () =>
        {
            return Results.Json(new
            {
                connection = new
                {
                    maxClientsPerTWS = IBApiLimits.MaxClientsPerTWS,
                    maxMessagesPerSecond = IBApiLimits.MaxMessagesPerSecond
                },
                realTimeData = new
                {
                    defaultMarketDataLines = IBApiLimits.DefaultMarketDataLines,
                    minDepthSubscriptions = IBApiLimits.MinDepthSubscriptions,
                    maxDepthSubscriptions = IBApiLimits.MaxDepthSubscriptions,
                    maxTickByTickSubscriptions = IBApiLimits.MaxTickByTickSubscriptions
                },
                historicalData = new
                {
                    maxConcurrentRequests = IBApiLimits.MaxConcurrentHistoricalRequests,
                    maxRequestsPer10Min = IBApiLimits.MaxHistoricalRequestsPer10Min,
                    minSecondsBetweenIdenticalRequests = IBApiLimits.MinSecondsBetweenIdenticalRequests,
                    maxSameContractRequestsPer2Sec = IBApiLimits.MaxSameContractRequestsPer2Sec,
                    maxHistoricalTicksPerRequest = IBApiLimits.MaxHistoricalTicksPerRequest,
                    smallBarMaxAgeMonths = IBApiLimits.SmallBarMaxAgeMonths,
                    bidAskRequestWeight = IBApiLimits.BidAskRequestWeight
                },
                snapshots = new
                {
                    freeSnapshotsPerMonth = IBApiLimits.FreeSnapshotsPerMonth,
                    snapshotCostUSD = IBApiLimits.SnapshotCostUSD
                }
            }, jsonOptions);
        })
        .WithName("GetIBLimits")
        .Produces(200);
    }

    private static string GetBuildMode()
    {
#if IBAPI
        var referencedAssemblies = typeof(IBApiLimits).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (referencedAssemblies.Contains("Meridian.IbApi.SmokeStub"))
            return "smoke";

        return "vendor";
#else
        return "guidance";
#endif
    }
}
