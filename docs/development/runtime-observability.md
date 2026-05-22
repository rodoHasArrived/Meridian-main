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
URL user-info credentials and credential-like query keys such as `client_secret`, `refresh_token`, `password`, and `credential` must be redacted before logs or bundle files leave the process.

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

The shared startup orchestrator emits `runtime.startup.sequence` with one correlation ID spanning
command dispatch, validation, runtime selection, and mode execution. Each phase reports
`StartupPhase`, `ElapsedMs`, and `Outcome`; phases exceeding the startup budget are warning-level
events with a recovery action. Config paths in these startup diagnostics are sanitized before
logging so account identifiers or credential-like query values are not exposed.

Warn when startup or shutdown phases exceed their expected budget, when a background service fails to stop, when a provider cannot disconnect cleanly, or when a channel consumer does not exit before timeout.

`GracefulShutdownService` emits the `runtime.shutdown.flush` operation when hosted buffers are
flushed. Use its `CorrelationId` to connect the shutdown-request log, per-component flush timings,
and the final outcome summary. The summary reports succeeded, failed, cancelled, and missing
flushes plus a recovery action when buffered data may need verification.

`GracefulShutdownHandler` emits the `runtime.shutdown.sequence` operation for process-signal and
manual shutdown flows that coordinate callbacks, producer cancellation, flushes, and disposal. It
uses one correlation ID across start, progress, flush, dispose, timeout, duplicate-request, and
completion diagnostics. Operator-supplied shutdown messages and exception messages are sanitized
before they are written to logs or returned in shutdown warnings, so credential-like values and
account identifiers do not leak through lifecycle diagnostics.

`ShutdownDiagnosticsService` keeps the latest in-process `runtime.shutdown.sequence` status for
support use. The snapshot includes the last shutdown correlation ID, reason, terminal status,
duration, incomplete flush count, warning count, sanitized warning summary, component counts, and
duplicate-request count. It is exposed through the live `GET /api/diagnostics/metrics` `shutdown`
block and through diagnostic bundles in `runtime-summary.json`. The warning summary must remain a
short sanitized list; do not add raw exception text, account identifiers, provider payloads, or
market/order details.

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

Streaming adapters based on `WebSocketProviderBase` expose these transport facts through the
optional `IProviderConnectionDiagnosticsSource` seam. Health and diagnostics consumers should read
that snapshot or subscribe to its change event instead of depending on adapter-specific socket
fields. The snapshot is intentionally limited to lifecycle state, WebSocket state, heartbeat/message
timestamps, reconnect counters, failure category, and safe error text; it must not include
credentials, account identifiers, request headers, or provider payloads.

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
Use the `shutdown` block to inspect the latest graceful shutdown sequence: last correlation ID,
status, incomplete flush count, sanitized warning summary, duplicate request count, and component
counts.
These fields are counts, timings, and runtime metadata only; do not add raw provider payloads,
account identifiers, order details, or portfolio values to this endpoint.

## Diagnostic Bundles

Support bundles may include:

- Manifest with bundle ID, correlation ID, generation time, elapsed time, runtime version, OS version, and files collected.
- `runtime-summary.json` with operation name, correlation ID, redaction policy, log-directory presence, safe process/runtime counters, and high-level metrics.
- Latest sanitized shutdown-sequence status when `ShutdownDiagnosticsService` is registered.
- Sanitized configuration summary.
- Sanitized recent logs.
- Metrics snapshot.
- Sanitized recent tracked errors from the in-process `ErrorTracker`.
- Storage shape and disk-space summary.
- Redacted Meridian/provider-related environment variables.

Bundles must exclude secrets and account numbers. Use status, counts, and masked summaries instead of raw sensitive data.
When `IEventMetrics` is registered, `metrics.json` and `runtime-summary.json` must include the
same aggregate event counters used by `/api/diagnostics/metrics` so support bundles can explain
pipeline throughput without requiring a live repro.
The summary includes published, dropped, rejected, trade, quote, depth-update, historical-bar,
events/sec, type-specific rate, latency-sample, latency, GC, and heap counters. These are aggregate
diagnostic values only and must not include raw event payloads, account identifiers, provider
responses, portfolio values, or order details.
When `ErrorTracker` is registered, `recent-errors.json` and the `runtime-summary.json` `errors`
block must include only sanitized error IDs, timestamps, levels, exception types, messages, stack
traces, contexts, and inner-exception messages. Secrets, credential query parameters, authorization
headers, and account identifiers must be redacted before the bundle is written.

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
