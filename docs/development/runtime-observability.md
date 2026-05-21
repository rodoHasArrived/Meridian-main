# Runtime Observability And Diagnostics

**Scope:** Runtime diagnostics for the Meridian host, providers, pipelines, storage, and operator support surfaces.

Meridian diagnostics should make failures explainable without exposing secrets or slowing hot paths. Prefer structured logs, counters, health summaries, and diagnostic bundles over high-frequency per-event logging.

## Log Levels

Use these levels consistently:

| Level | Use for |
| --- | --- |
| Trace | Developer-only flow detail behind explicit diagnostic mode. |
| Debug | Troubleshooting detail that is useful during a focused investigation. |
| Information | Lifecycle milestones such as startup, shutdown, provider connect/disconnect, bundle generation, and operator-command completion. |
| Warning | Recoverable abnormal conditions such as retry, degraded provider state, queue high-water marks, validation rejection summaries, or slow lifecycle phases. |
| Error | Failed operations where Meridian recovered or returned a safe failure to the caller. |
| Critical/Fatal | App-threatening failures, crash paths, repeated startup failure, or unsafe shutdown failure. |

## Required Context

Structured runtime logs should include these fields when applicable:

| Field | Example |
| --- | --- |
| `OperationName` | `diagnostic-bundle.generate`, `provider.connect`, `pipeline.flush` |
| `ComponentName` or source context | `DiagnosticBundleService`, `EventPipeline`, provider adapter name |
| `CorrelationId` | Startup sequence ID, provider connection attempt ID, user command ID, or bundle ID |
| `ProviderName` | `Alpaca`, `Polygon`, `Synthetic` |
| Safe entity identifier | Ticker symbol or non-sensitive security ID only when safe |
| `SubscriptionId` | Provider subscription lifecycle diagnostics |
| `QueueName` | Pipeline or channel name |
| `ElapsedMs` | Timed operation duration |
| `FailureReason` | Short reason category, not a raw provider payload |
| `RecoveryAction` | Retry, degraded mode, operator next step, or safe abort |

Errors should answer: what failed, which component failed, what safe input/entity was involved, whether the failure was recoverable, and what Meridian did next.

## Redaction Rules

Never log or export:

- API keys, secret keys, passwords, bearer/basic auth tokens, refresh tokens, session tokens, client secrets, private keys, or connection strings.
- Account numbers or custodian account identifiers.
- Full raw provider payloads, market-data payloads, portfolio holdings, order details, or statement rows unless explicitly redacted and debug-gated.

Diagnostic bundles and live diagnostics endpoints must sanitize logs, configuration summaries, environment variables, query strings, tracked error messages, stack traces, contexts, and storage listings before exporting support data. If a diagnostic feature cannot safely redact a field, omit the field and include a non-sensitive count or status instead.

## Performance Rules

- Do not log every market-data event, quote, trade, or depth update.
- Use counters, meters, queue-depth gauges, and periodic summaries for hot paths.
- Sample queue depth and latency instead of allocating diagnostic objects per event.
- Throttle repeated warnings such as provider reconnect attempts or queue high-water alerts.
- Use structured logging placeholders instead of interpolated strings so disabled log levels do not pay formatting cost.
- Keep diagnostic bundle generation and support export operations off ingestion hot paths.

## Lifecycle Diagnostics

Startup and shutdown should emit information-level milestones with elapsed time and correlation:

- Startup requested
- Host built
- Services registered
- Workstation host listening
- First window or workstation route ready
- Startup complete
- Shutdown requested
- Cancellation signaled
- Providers disconnecting and disconnected
- Channels completed
- Storage writers flushed
- Host stopped
- Logs flushed
- Shutdown complete

Warn when startup or shutdown phases exceed their expected budget, when a background service fails to stop, when a provider cannot disconnect cleanly, or when a channel consumer does not exit before timeout.

`GracefulShutdownService` emits the `runtime.shutdown.flush` operation when hosted buffers are
flushed. Use its `CorrelationId` to connect the shutdown-request log, per-component flush timings,
and the final outcome summary. The summary reports succeeded, failed, cancelled, and missing
flushes plus a recovery action when buffered data may need verification.

## Provider Health

Provider diagnostics should track connection state, last heartbeat, last message time, reconnect attempts, authentication failures, subscription failures, data gaps, rate-limit warnings, latency, disconnect reasons, and recovery state.

Provider health summaries should expose:

- Provider name
- Status: connected, disconnected, degraded, blocked, or unknown
- Last connected time
- Last message time
- Active subscription count
- Last safe error category
- Reconnect count
- Recommended operator action

## Pipeline And Queue Metrics

Event pipeline diagnostics should prefer counters and summaries:

- Events received, processed, dropped, rejected, and deduplicated
- Queue depth and peak queue depth
- Processing latency and max latency
- Consumer lag
- Backpressure events
- Serialization and storage failures
- UI dispatch lag for operator-facing updates

Log anomalies and periodic summaries such as events/sec, dropped count, queue depth, and max latency. Do not log individual high-frequency payloads.

The live host exposes a low-cost queue and throughput snapshot at `GET /api/diagnostics/metrics`.
Use the `eventPipeline` block for current queue depth, peak queue depth, utilization, drops, and
flush age. It also reports recovered, rejected, and deduplicated counts plus whether WAL,
validation, and deduplication are enabled for the live pipeline. Use the `marketDataMetrics` block
for aggregate published/dropped counts, events/sec, message-type counters, and latency summaries.
When the dual-path trade/quote fast path is registered, use the `eventPipelineHotPath` block for
ring-buffer depth plus hot-path published, consumed, fallback, and dropped counts so operators can
distinguish slow-path backpressure from hot-path saturation without enabling verbose logging.
Use the `runtime` block for support triage before enabling heavier tracing: process ID, sanitized
process name, start time, uptime, thread count, handle count when available, working set, private
memory, managed heap, processor count, runtime version, and OS description. These values are sampled
only when the diagnostics endpoint is called and must stay out of market-data hot paths.
These fields are counts, timings, and runtime metadata only; do not add raw provider payloads,
account identifiers, order details, or portfolio values to this endpoint.

## Diagnostic Bundles

Support bundles may include:

- Manifest with bundle ID, correlation ID, generation time, elapsed time, runtime version, OS version, and files collected.
- `runtime-summary.json` with operation name, correlation ID, redaction policy, log-directory presence, safe process/runtime counters, and high-level metrics.
- Sanitized configuration summary.
- Sanitized recent logs.
- Metrics snapshot.
- Storage shape and disk-space summary.
- Redacted Meridian/provider-related environment variables.

Bundles must exclude secrets and account numbers. Use status, counts, and masked summaries instead of raw sensitive data.
When `IEventMetrics` is registered, `metrics.json` and `runtime-summary.json` must include the
same aggregate event counters used by `/api/diagnostics/metrics` so support bundles can explain
pipeline throughput without requiring a live repro.

## Validation Expectations

Runtime observability changes should add or update focused tests for the affected seam:

- Redaction rules
- Correlation ID propagation
- Provider health state transitions
- Queue metric updates
- Runtime resource snapshots
- Diagnostic summary generation
- Error handling paths
- Logging does not throw
- Bundle exports exclude secrets

Use the narrowest validation command that covers the changed files, then broaden only when shared DTOs, endpoints, or UI contracts change.
