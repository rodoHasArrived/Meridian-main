using System.Diagnostics;
using Meridian.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Meridian.Platform.Tracing;

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

    /// <summary>
    /// Registers one DI-owned tracing provider. Calling this method is an explicit opt-in;
    /// exporters remain disabled by default. The compatibility opt-in can also collect pipeline metrics.
    /// </summary>
    public static IServiceCollection AddOpenTelemetryTracing(
        this IServiceCollection services,
        OpenTelemetryConfiguration config,
        bool enablePipelineMetrics = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        var endpoint = ValidateConfiguration(config);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TracingLifetimeService)))
            return services;

        services.AddSingleton(config);
        var telemetry = services.AddOpenTelemetry().ConfigureResource(resource => resource
            .AddService(config.ServiceName, serviceVersion: config.ServiceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = config.Environment
            }));
        telemetry.WithTracing(tracing =>
        {
            tracing
                // Platform pipeline and Infrastructure backfill use this existing source name.
                .AddSource(ActivitySource.Name)
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(config.SamplingRatio)))
                .AddHttpClientInstrumentation(options => options.RecordException = true)
                .AddAspNetCoreInstrumentation(options => options.RecordException = true);

            if (config.EnableConsoleExporter)
                tracing.AddConsoleExporter();

            if (config.EnableOtlpExporter)
                tracing.AddOtlpExporter(options =>
                {
                    // Explicit values override ambient OTEL exporter defaults. Merely setting
                    // OTEL_* environment variables must never opt a host into network export.
                    options.Endpoint = endpoint!;
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    options.Headers = config.OtlpHeaders ?? string.Empty;
                    options.TimeoutMilliseconds = config.FlushTimeoutMilliseconds;
                });
        });

        if (enablePipelineMetrics)
            telemetry.WithMetrics(metrics =>
            {
                metrics.AddMeter("Meridian.Pipeline");
                if (config.EnableConsoleExporter)
                    metrics.AddConsoleExporter();
                if (config.EnableOtlpExporter)
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = endpoint!;
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                        options.Headers = config.OtlpHeaders ?? string.Empty;
                        options.TimeoutMilliseconds = config.FlushTimeoutMilliseconds;
                    });
            });

        services.AddSingleton<TracingLifetimeService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(
            sp => sp.GetRequiredService<TracingLifetimeService>());
        return services;
    }

    private static Uri? ValidateConfiguration(OpenTelemetryConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.ServiceName))
            throw new ArgumentException("Tracing.ServiceName must not be blank.", nameof(config));
        if (!double.IsFinite(config.SamplingRatio) || config.SamplingRatio is < 0 or > 1)
            throw new ArgumentException("Tracing.SamplingRatio must be between 0 and 1.", nameof(config));
        if (config.FlushTimeoutMilliseconds <= 0)
            throw new ArgumentException("Tracing.FlushTimeoutMilliseconds must be positive.", nameof(config));

        Uri? endpoint = null;
        if (config.OtlpEndpoint is not null || config.EnableOtlpExporter)
        {
            if (!Uri.TryCreate(config.OtlpEndpoint, UriKind.Absolute, out endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(endpoint.Host)
                || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
                throw new ArgumentException(
                    "Tracing.OtlpEndpoint must be an absolute HTTP(S) collector URL without credentials, query, or fragment.",
                    nameof(config));
        }

        return endpoint;
    }

    /// <summary>
    /// Resolve the provider while hosted services are constructed, before any worker or startup
    /// guard creates instrumented services. Stop flushes completed spans; the provider remains
    /// alive until DI disposal, which also exports spans produced by final pipeline draining.
    /// </summary>
    private sealed class TracingLifetimeService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly TracerProvider _provider;
        private readonly OpenTelemetryConfiguration _config;
        private readonly MeterProvider? _meterProvider;

        public TracingLifetimeService(TracerProvider provider, OpenTelemetryConfiguration config,
            MeterProvider? meterProvider = null)
        {
            _provider = provider;
            _config = config;
            _meterProvider = meterProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_provider.ForceFlush(_config.FlushTimeoutMilliseconds))
                LoggingSetup.ForContext("OpenTelemetrySetup").Warning("Tracing shutdown flush timed out or failed");
            if (_meterProvider is not null && !_meterProvider.ForceFlush(_config.FlushTimeoutMilliseconds))
                LoggingSetup.ForContext("OpenTelemetrySetup").Warning("Pipeline metrics shutdown flush timed out or failed");
            return Task.CompletedTask;
        }
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

    /// <summary>Start a queued batch under its producer, linking other producers in the batch.</summary>
    public static Activity? StartBatchConsumeActivity(
        int batchSize, ActivityContext parentContext, IEnumerable<ActivityLink>? links = null)
    {
        var activity = Source.StartActivity(
            "Pipeline.ConsumeBatch", ActivityKind.Internal, parentContext, links: links);
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

    /// <summary>Bounded shutdown flush and OTLP request timeout.</summary>
    public int FlushTimeoutMilliseconds { get; init; } = 5000;

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
