# Distributed Tracing Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-02

Meridian tracing is an explicit startup option. With the `Tracing` section absent or
`Tracing.Enabled=false`, the shared host does not create a tracing provider or exporters; normal
startup, pipeline processing, and historical backfill continue. Console and OTLP exporters are
also disabled by default. Setting an `OTEL_*` environment variable alone does not enable them.

Use the [configuration field reference](../reference/appsettings-schema.md#tracing) for defaults
and validation rules. Apply the section to the runtime JSON configuration used by the host and
restart it after changes; tracing configuration is not hot-reloaded. The `Tracing` settings use
the host's `ConfigStore` configuration; `Tracing__*` environment variables are not a supported
override for this section.

## Inspect a local operation

Use a running, configured host with the provider/storage prerequisites from
[preflight](preflight-checklist.md). For a Windows source checkout, edit its local runtime config
and run commands in PowerShell 7 from the repository root; leave the host in terminal 1 and initiate
the bounded operation from the workstation or an authenticated client in terminal 2. Installed
hosts use supervisor restart and the configuration path in their lifecycle manifest. Retain a copy
of the prior tracing section so the diagnostic change can be reversed. Trace configuration is
diagnostic setup, not a credential/persistence substitute.

1. Add this section to the host's runtime JSON configuration:

   ```json
   {
     "Tracing": {
       "Enabled": true,
       "EnableConsoleExporter": true,
       "EnableOtlpExporter": false,
       "SamplingRatio": 1.0,
       "Environment": "development"
     }
   }
   ```

2. Restart the host with its existing provider and storage configuration. The console exporter
   writes completed spans to the host's standard output.
3. Run a bounded operation using the existing
   [provider backfill procedure](provider-backfill-operations.md), or allow the configured
   streaming provider to publish events into the pipeline. Retain the symbol, provider, and run
   time so the spans can be matched to the operation.
4. Inspect the spans' trace IDs, span IDs, parent span IDs, status, and tags. Pipeline
   `ProcessMarketEvent.*` and `StoreMarketEvent.*` spans retain the context captured when the event
   entered the queue. Backfill worker `Backfill.*`, `BackfillFetch.*`, and
   `BackfillStorage.WriteBars` spans retain the context of the queued work. For a single operation,
   related spans share a trace ID and each child points to its parent's span ID, even when the
   request span has ended before queued work finishes. Independent operations have independent
   trace IDs; a batch containing several operations can link to more than one trace.
5. For a failed operation, inspect spans with error status and exception events alongside the
   existing operation result and logs. Tracing is diagnostic evidence; retain the normal backfill
   result, checkpoint, and quality evidence before accepting data.

Backfill queue context is retained for in-process retries. It is not stored with the job for
cross-restart recovery: a recovered job starts with the context of its new admission, or a new
root trace when none is present. Retain the job's normal identity to correlate separate attempts
across a host restart.

The shared host subscribes to the existing Meridian activity sources and HTTP client/server
instrumentation. A host with tracing enabled and both exporters disabled still records sampled
local activities for an attached listener or custom exporter, but has no built-in output
destination. Select an exporter to inspect a trace outside the process.

Code integrations using `CompositionOptions.EnableOpenTelemetry` also collect pipeline counters and
latency through the `Meridian.Pipeline` meter. Its console/OTLP metrics exporters follow the same
explicit exporter flags, resource identity, destination, headers, and timeout as tracing. The host
flushes both providers during shutdown. The JSON `Tracing.Enabled` option alone enables tracing;
existing Prometheus metrics remain available through their normal path.

## Export to an OTLP collector

Configure an explicit OTLP/gRPC destination. For a local collector listening on port 4317:

```json
{
  "Tracing": {
    "Enabled": true,
    "EnableConsoleExporter": false,
    "EnableOtlpExporter": true,
    "OtlpEndpoint": "http://localhost:4317",
    "SamplingRatio": 1.0,
    "ServiceName": "Meridian",
    "ServiceVersion": "1.0.0",
    "Environment": "development",
    "FlushTimeoutMilliseconds": 5000
  }
}
```

Use the collector's reachable `https://` endpoint for TLS deployments. The endpoint must be an
absolute HTTP or HTTPS URI without embedded credentials, a query string, or a fragment. Enabling
the OTLP exporter without a destination, or configuring an invalid destination, refuses enabled
tracing startup. This validation checks the destination's shape; it does not prove collector
reachability, authentication, or backend delivery. OTLP uses gRPC, so choose the collector's gRPC
listener rather than an HTTP/protobuf `/v1/traces` ingestion URL.

If required by the collector, supply `OtlpHeaders` through deployment-managed configuration in
the exporter's comma-separated `key=value` format. Treat authentication headers as secrets and
exclude them from committed configuration and support bundles. The collector destination comes
from `Tracing.OtlpEndpoint`; ambient `OTEL_EXPORTER_OTLP_ENDPOINT` does not opt the host into export
or replace the configured destination.

Restart, run one bounded operation, then search the trace backend by `service.name`, the configured
environment, and the operation time. Use the span's `market.symbol` and `backfill.provider` tags
to identify the work. Confirm that queued processing and storage spans are connected by trace and
parent IDs. Keep `SamplingRatio=1.0` while verifying; lower values sample a fraction of new root
traces, while parent-based sampling preserves the upstream sampling decision.

## Stop and troubleshoot

Use normal host shutdown or the
[lifecycle supervisor](../reference/lifecycle-control-plane.md) so workers finish and the pipeline
drains. Hosted-service stop attempts to flush completed spans using `FlushTimeoutMilliseconds`.
The provider stays alive until service-container disposal, which also exports spans completed
during final pipeline cleanup and disposes the exporter. `FlushTimeoutMilliseconds` also limits
OTLP exporter requests. Allow sufficient supervisor/process stop time for work drain, telemetry
flush, and disposal; a forced process kill or unreachable collector cannot guarantee delivery.

If no spans appear, check that the host loaded the intended configuration, `Enabled` and the
chosen exporter flag are true, the operation actually ran, and the parent trace was sampled.
For OTLP, also verify collector protocol, destination, credentials, and backend routing. Console
export can establish whether the host is producing spans before investigating collector delivery.

To disable tracing, set `Tracing.Enabled=false` and restart. Exporter settings can remain in the
file for a later explicit enablement.

## Related guidance

- [Appsettings Schema Reference](../reference/appsettings-schema.md#tracing)
- [Provider Backfill Operations](provider-backfill-operations.md)
- [Operator Runbook](operator-runbook.md)
- [Lifecycle Control Plane](../reference/lifecycle-control-plane.md)
