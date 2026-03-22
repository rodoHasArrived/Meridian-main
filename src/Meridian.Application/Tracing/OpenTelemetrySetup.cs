using System.Diagnostics;
using Meridian.Application.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Meridian.Application.Tracing;

/// <summary>
/// OpenTelemetry setup for distributed tracing across the market data pipeline.
/// Traces events from provider → collector → storage with full context propagation.
///
/// Based on: https://github.com/open-telemetry/opentelemetry-dotnet (Apache 2.0)
/// Reference: docs/open-source-references.md #15
/// </summary>
public static class OpenTelemetrySetup
{
    /// <summary>
    /// The activity source for Meridian tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Meridian", "1.0.0");

    private static readonly ILogger Log = LoggingSetup.ForContext("OpenTelemetrySetup");
    private static TracerProvider? _tracerProvider;
    private static MeterProvider? _meterProvider;

    /// <summary>
    /// Initialize OpenTelemetry tracing with the specified configuration.
    /// </summary>
    public static void Initialize(OpenTelemetryConfiguration config)
    {
        if (_tracerProvider != null)
            return;

        Log.Information("Initializing OpenTelemetry with service name: {ServiceName}", config.ServiceName);

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: config.ServiceName,
                serviceVersion: config.ServiceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = config.Environment,
                ["host.name"] = Environment.MachineName
            });

        // Configure tracing
        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(ActivitySource.Name)
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
            });

        // Add exporters based on configuration
        if (config.EnableConsoleExporter)
        {
            tracerBuilder.AddConsoleExporter();
            Log.Debug("Console trace exporter enabled");
        }

        if (config.EnableOtlpExporter && !string.IsNullOrEmpty(config.OtlpEndpoint))
        {
            tracerBuilder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(config.OtlpEndpoint);
                if (!string.IsNullOrEmpty(config.OtlpHeaders))
                {
                    options.Headers = config.OtlpHeaders;
                }
            });
            Log.Information("OTLP trace exporter enabled: {Endpoint}", config.OtlpEndpoint);
        }

        // Configure sampling
        if (config.SamplingRatio < 1.0)
        {
            tracerBuilder.SetSampler(new TraceIdRatioBasedSampler(config.SamplingRatio));
            Log.Debug("Trace sampling configured at {Ratio:P0}", config.SamplingRatio);
        }

        _tracerProvider = tracerBuilder.Build();

        // Configure metrics (includes pipeline meter from TracedEventMetrics)
        var meterBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter("Meridian.Metrics")
            .AddMeter("Meridian.Pipeline");

        if (config.EnableConsoleExporter)
        {
            meterBuilder.AddConsoleExporter();
        }

        if (config.EnableOtlpExporter && !string.IsNullOrEmpty(config.OtlpEndpoint))
        {
            meterBuilder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(config.OtlpEndpoint);
            });
        }

        _meterProvider = meterBuilder.Build();

        Log.Information("OpenTelemetry initialized successfully");
    }

    /// <summary>
    /// Add OpenTelemetry services to the DI container.
    /// </summary>
    public static IServiceCollection AddOpenTelemetryTracing(
        this IServiceCollection services,
        OpenTelemetryConfiguration config)
    {
        services.AddSingleton(config);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: config.ServiceName,
                    serviceVersion: config.ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySource.Name)
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation();

                if (config.EnableConsoleExporter)
                    tracing.AddConsoleExporter();

                if (config.EnableOtlpExporter && !string.IsNullOrEmpty(config.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("Meridian.Metrics")
                    .AddMeter("Meridian.Pipeline")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (config.EnableConsoleExporter)
                    metrics.AddConsoleExporter();

                if (config.EnableOtlpExporter && !string.IsNullOrEmpty(config.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint);
                    });
                }
            });

        return services;
    }

    /// <summary>
    /// Shutdown OpenTelemetry providers gracefully.
    /// </summary>
    public static void Shutdown()
    {
        Log.Information("Shutting down OpenTelemetry...");
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
        _tracerProvider = null;
        _meterProvider = null;
        Log.Information("OpenTelemetry shutdown complete");
    }
}

/// <summary>
/// Tracing utilities for market data operations.
/// </summary>
public static class MarketDataTracing
{
    private static readonly ActivitySource Source = OpenTelemetrySetup.ActivitySource;

    /// <summary>
    /// Start a trace for receiving data from a market data provider.
    /// </summary>
    public static Activity? StartReceiveActivity(string providerName, string symbol)
    {
        var activity = Source.StartActivity(
            $"ReceiveMarketData.{providerName}",
            ActivityKind.Consumer);

        activity?.SetTag("provider.name", providerName);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "receive");

        return activity;
    }

    /// <summary>
    /// Start a trace for processing a market event.
    /// </summary>
    public static Activity? StartProcessActivity(string eventType, string symbol)
    {
        var activity = Source.StartActivity(
            $"ProcessMarketEvent.{eventType}",
            ActivityKind.Internal);

        activity?.SetTag("event.type", eventType);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "process");

        return activity;
    }

    /// <summary>
    /// Start a trace for processing a market event with an explicit parent context restored
    /// after an async queue boundary.
    /// </summary>
    public static Activity? StartProcessActivity(string eventType, string symbol, ActivityContext parentContext)
    {
        var activity = parentContext.TraceId == default
            ? StartProcessActivity(eventType, symbol)
            : Source.StartActivity(
                $"ProcessMarketEvent.{eventType}",
                ActivityKind.Internal,
                parentContext);

        activity?.SetTag("event.type", eventType);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "process");

        return activity;
    }

    /// <summary>
    /// Start a trace for storing a market event.
    /// </summary>
    public static Activity? StartStorageActivity(string storageType, string symbol)
    {
        var activity = Source.StartActivity(
            $"StoreMarketEvent.{storageType}",
            ActivityKind.Producer);

        activity?.SetTag("storage.type", storageType);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "store");

        return activity;
    }

    /// <summary>
    /// Start a trace for storing a market event with an explicit parent context.
    /// </summary>
    public static Activity? StartStorageActivity(string storageType, string symbol, ActivityContext parentContext)
    {
        var activity = parentContext.TraceId == default
            ? StartStorageActivity(storageType, symbol)
            : Source.StartActivity(
                $"StoreMarketEvent.{storageType}",
                ActivityKind.Producer,
                parentContext);

        activity?.SetTag("storage.type", storageType);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "store");

        return activity;
    }

    /// <summary>
    /// Start a trace for publishing to message bus.
    /// </summary>
    public static Activity? StartPublishActivity(string destination, string messageType)
    {
        var activity = Source.StartActivity(
            $"PublishMessage.{messageType}",
            ActivityKind.Producer);

        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.message_type", messageType);
        activity?.SetTag("operation.type", "publish");

        return activity;
    }

    /// <summary>
    /// Start a trace for WebSocket operations.
    /// </summary>
    public static Activity? StartWebSocketActivity(string operation, string endpoint)
    {
        var activity = Source.StartActivity(
            $"WebSocket.{operation}",
            ActivityKind.Client);

        activity?.SetTag("websocket.operation", operation);
        activity?.SetTag("websocket.endpoint", endpoint);

        return activity;
    }

    /// <summary>
    /// Start a trace for indicator calculation.
    /// </summary>
    public static Activity? StartIndicatorActivity(string indicatorName, string symbol)
    {
        var activity = Source.StartActivity(
            $"CalculateIndicator.{indicatorName}",
            ActivityKind.Internal);

        activity?.SetTag("indicator.name", indicatorName);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("operation.type", "calculate");

        return activity;
    }

    /// <summary>
    /// Start a trace for a pipeline batch consume operation.
    /// </summary>
    public static Activity? StartBatchConsumeActivity(int batchSize)
    {
        var activity = Source.StartActivity(
            "Pipeline.ConsumeBatch",
            ActivityKind.Internal);

        activity?.SetTag("pipeline.batch_size", batchSize);
        activity?.SetTag("operation.type", "consume_batch");

        return activity;
    }

    /// <summary>
    /// Start a trace for a backfill operation.
    /// </summary>
    public static Activity? StartBackfillActivity(string provider, string symbol, string? from, string? to)
    {
        var activity = Source.StartActivity(
            $"Backfill.{provider}",
            ActivityKind.Client);

        activity?.SetTag("backfill.provider", provider);
        activity?.SetTag("market.symbol", symbol);
        activity?.SetTag("backfill.from", from ?? "unspecified");
        activity?.SetTag("backfill.to", to ?? "unspecified");
        activity?.SetTag("operation.type", "backfill");

        return activity;
    }

    /// <summary>
    /// Start a trace for a WAL recovery operation.
    /// </summary>
    public static Activity? StartWalRecoveryActivity()
    {
        var activity = Source.StartActivity(
            "Pipeline.WalRecovery",
            ActivityKind.Internal);

        activity?.SetTag("operation.type", "wal_recovery");

        return activity;
    }

    /// <summary>
    /// Record an error on the current activity.
    /// </summary>
    public static void RecordError(Activity? activity, Exception ex)
    {
        if (activity == null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddException(ex);
    }

    /// <summary>
    /// Add latency measurement to activity.
    /// </summary>
    public static void RecordLatency(Activity? activity, TimeSpan latency)
    {
        activity?.SetTag("latency.ms", latency.TotalMilliseconds);
    }

    /// <summary>
    /// Add event count to activity.
    /// </summary>
    public static void RecordEventCount(Activity? activity, int count)
    {
        activity?.SetTag("event.count", count);
    }
}

/// <summary>
/// Configuration for OpenTelemetry integration.
/// </summary>
public sealed class OpenTelemetryConfiguration
{
    public string ServiceName { get; init; } = "Meridian";
    public string ServiceVersion { get; init; } = "1.0.0";
    public string Environment { get; init; } = "development";

    /// <summary>
    /// Enable console exporter for debugging.
    /// </summary>
    public bool EnableConsoleExporter { get; init; } = false;

    /// <summary>
    /// Enable OTLP exporter for production observability.
    /// </summary>
    public bool EnableOtlpExporter { get; init; } = false;

    /// <summary>
    /// OTLP collector endpoint (e.g., "http://localhost:4317").
    /// </summary>
    public string? OtlpEndpoint { get; init; }

    /// <summary>
    /// Optional headers for OTLP exporter (e.g., for authentication).
    /// </summary>
    public string? OtlpHeaders { get; init; }

    /// <summary>
    /// Sampling ratio (0.0 to 1.0). 1.0 = sample all traces.
    /// </summary>
    public double SamplingRatio { get; init; } = 1.0;

    /// <summary>
    /// Enable tracing for WebSocket operations.
    /// </summary>
    public bool TraceWebSocket { get; init; } = true;

    /// <summary>
    /// Enable tracing for storage operations.
    /// </summary>
    public bool TraceStorage { get; init; } = true;

    /// <summary>
    /// Enable tracing for message publishing.
    /// </summary>
    public bool TraceMessaging { get; init; } = true;

    public static OpenTelemetryConfiguration Default => new();

    public static OpenTelemetryConfiguration Development => new()
    {
        EnableConsoleExporter = true,
        Environment = "development",
        SamplingRatio = 1.0
    };

    public static OpenTelemetryConfiguration Production(string otlpEndpoint) => new()
    {
        EnableOtlpExporter = true,
        OtlpEndpoint = otlpEndpoint,
        Environment = "production",
        SamplingRatio = 0.1 // Sample 10% in production
    };
}
