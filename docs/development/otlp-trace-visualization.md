# OTLP Trace Visualization

Meridian already emits OpenTelemetry-compatible traces and metrics in the market-data pipeline. This guide shows how to route those signals to an OTLP collector and inspect them in a local trace UI.

## What Exists Today

- `Meridian.Application.Tracing.OpenTelemetrySetup` exposes the shared `ActivitySource` named `Meridian`.
- `MarketEventIngressTracing` starts collector-ingress spans for trade and quote publication.
- `EventPipeline` preserves trace context across queueing and uses storage spans for append operations.
- `TracedEventMetrics` emits OTLP-compatible counters and histograms from the `Meridian.Pipeline` meter.
- `CompositionOptions.EnableOpenTelemetry` enables the traced metrics decorator for pipeline metrics.

## Current Wiring Model

Trace export is available, but it is not yet auto-bound from `appsettings.json` in every host. Today there are two layers:

- Metrics instrumentation toggle:
  - Set `CompositionOptions.EnableOpenTelemetry = true` when building services.
- Exporter setup:
  - Call `services.AddOpenTelemetryTracing(config)` in DI-hosted apps, or
  - Call `OpenTelemetrySetup.Initialize(config)` in non-DI startup code.

That means operators can use OTLP now, but host startup must opt into it explicitly.

## Minimal Configuration

Use `OpenTelemetryConfiguration` from `Meridian.Application.Tracing`:

```csharp
using Meridian.Application.Tracing;

var otel = new OpenTelemetryConfiguration
{
    ServiceName = "Meridian.Workstation",
    ServiceVersion = "1.0.0",
    Environment = "development",
    EnableConsoleExporter = false,
    EnableOtlpExporter = true,
    OtlpEndpoint = "http://localhost:4317",
    SamplingRatio = 1.0
};
```

For DI-based hosts:

```csharp
services.AddMarketDataServices(new CompositionOptions
{
    EnableOpenTelemetry = true
});

services.AddOpenTelemetryTracing(otel);
```

For manual startup paths:

```csharp
OpenTelemetrySetup.Initialize(otel);
```

## Local Collector Setup

The easiest local path is an OpenTelemetry Collector plus Jaeger. The collector receives OTLP from Meridian on `4317` and forwards traces to Jaeger.

`otel-collector-config.yaml`

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

exporters:
  debug: {}
  otlp/jaeger:
    endpoint: jaeger:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [debug, otlp/jaeger]
    metrics:
      receivers: [otlp]
      exporters: [debug]
```

`docker-compose.yaml`

```yaml
services:
  jaeger:
    image: jaegertracing/all-in-one:1.61
    ports:
      - "16686:16686"
      - "4317:4317"

  otel-collector:
    image: otel/opentelemetry-collector:0.106.1
    command: ["--config=/etc/otelcol/config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otelcol/config.yaml:ro
    ports:
      - "4318:4318"
    depends_on:
      - jaeger
```

Then open [Jaeger UI](http://localhost:16686), search for service name `Meridian.Workstation`, and inspect the spans.

## Signals You Should See

With tracing enabled, a normal market-data path should show spans similar to:

- `trade-collector.publish` or `quote-collector.publish`
- `ProcessMarketEvent.<type>`
- `StoreMarketEvent.<sink>`

Useful tags include:

- `collector.name`
- `event.type`
- `symbol`
- `provider.name`
- `storage.type`
- `operation.type`

Structured logs now also include correlation, trace, and span identifiers, which makes it possible to pivot from logs to traces during incident review.

## Metrics Names

`TracedEventMetrics` publishes through meter `Meridian.Pipeline`:

- `mdc.pipeline.events.published`
- `mdc.pipeline.events.dropped`
- `mdc.pipeline.events.trades`
- `mdc.pipeline.events.depth`
- `mdc.pipeline.events.quotes`
- `mdc.pipeline.events.integrity`
- `mdc.pipeline.events.historical_bars`
- `mdc.pipeline.latency`

## Production Notes

- Use `SamplingRatio = 0.1` or lower on higher-volume hosts unless you are actively debugging.
- If your collector requires auth, set `OtlpHeaders` to the header string expected by the exporter.
- Keep service names stable per host role, for example `Meridian.Workstation`, `Meridian.Collector`, or `Meridian.Web`.
- Call `OpenTelemetrySetup.Shutdown()` on controlled shutdown for non-DI startup paths so buffered exports are flushed cleanly.

## Validation Checklist

- Meridian host starts with `EnableOpenTelemetry = true`.
- OTLP exporter is pointed at a reachable collector endpoint.
- Jaeger shows the selected service name.
- A replay, live provider session, or pipeline test causes new spans to appear.
- Trace IDs in logs line up with the spans visible in Jaeger.
