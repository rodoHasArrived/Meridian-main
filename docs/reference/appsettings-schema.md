---
title: Appsettings Schema Reference
status: active
owner: core-team
reviewed: 2026-06-02
audience: developers-and-operators
---

# Appsettings Schema Reference

This page documents the canonical schema sources for runtime configuration:

- [`config/appsettings.sample.json`](../../config/appsettings.sample.json) (human-readable example)
- [`config/appsettings.schema.json`](../../config/appsettings.schema.json) (machine-validated schema)

Use this page when you need to map configuration sections to high-impact operational behavior.

## Schema Sources and Validation

| Source | Purpose |
|---|---|
| `config/appsettings.sample.json` | Operator/developer template with safe defaults and comments. |
| `config/appsettings.schema.json` | JSON schema used to validate allowed top-level sections and field shapes. |

## High-Impact Sections

| Section | Why it matters | Typical env override path |
|---|---|---|
| `DataSource` / `DataSources` | Chooses live/offline provider routing and failover posture. | `MDC_DATASOURCE` |
| `Backfill` | Controls historical import behavior, retry policy, and scheduling. | `MDC_BACKFILL_*` |
| `Storage` | Controls retention, partitioning, and storage pressure behavior. | `MDC_STORAGE_*` |
| `TenantScopeEnforcement` | Defaults to `fail-closed`; retained-data readiness must pass before serving scoped work. Explicit `deployment-boundary` is temporary single-company migration compatibility. Requires restart. | `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` |
| `IB`, `IBClientPortal` | Controls broker connectivity and execution-adjacent account surfaces. | `MDC_IB_*` |
| `Alpaca`, provider blocks under `Backfill:Providers` | Provider-specific data/credential posture. | `MDC_ALPACA_*`, provider-specific keys |
| `SecurityMasterWorkbench` | Controls governed-write conflict-authority source precedence for Security Master passport edits. | n/a |
| `Serilog` | Logging signal/noise and sensitive-output posture. | `MDC_DEBUG`, `MDC_LOG_LEVEL` |
| `Tracing` | Explicit opt-in tracing, exporter destinations, sampling, and shutdown flush. Requires restart. | Runtime `Tracing` section; exporters are not enabled by `OTEL_*` variables. |

## Tracing

The common host composition path owns one tracing provider when `Tracing.Enabled=true`. Omitting
the section or setting `Enabled=false` leaves normal startup and processing unchanged. For setup,
trace inspection, and shutdown verification, see
[Distributed Tracing Operations](../operators/distributed-tracing.md).

| Field under `Tracing` | Default | Contract |
| --- | --- | --- |
| `Enabled` | `false` | Explicit host opt-in. Exporter flags alone do not enable tracing. |
| `EnableConsoleExporter` | `false` | Write completed spans to standard output. |
| `EnableOtlpExporter` | `false` | Export traces using OTLP/gRPC; requires `OtlpEndpoint`. |
| `OtlpEndpoint` | `null` | Explicit absolute HTTP or HTTPS collector URI, without user information, query, or fragment. Validated when tracing is enabled; supplied destinations must be valid even if OTLP export is off. |
| `OtlpHeaders` | `null` | Optional comma-separated `key=value` exporter headers. Authentication values are secrets. |
| `SamplingRatio` | `1.0` | Finite value from `0.0` through `1.0`, inclusive. Samples new root traces; parent-based sampling respects the upstream sampling decision. |
| `ServiceName` | `Meridian` | Nonempty service name attached to exported spans. |
| `ServiceVersion` | `1.0.0` | Service version attached to exported spans. |
| `Environment` | `development` | Deployment environment attached to exported spans. |
| `FlushTimeoutMilliseconds` | `5000` | Positive timeout for the host's telemetry flush and OTLP exporter requests. |

These values are read at host composition time; changes require restart. Enabling tracing with
both exporters disabled permits local activities without a built-in output destination. Enabling
OTLP does not infer a destination from `OTEL_EXPORTER_OTLP_ENDPOINT`. Existing code integrations
can also explicitly opt in through `CompositionOptions.EnableOpenTelemetry`; that compatibility
option still uses the same host-owned provider and does not enable exporters by default.

## Security and Mutation Guardrails

Before upgrading existing installations, follow the [tenant cutover and backfill runbook](../operators/fund-structure-tenant-backfill.md).
Unattributed records cause an explicit startup refusal with remediation, rather than an apparently
empty ledger. A configured posture in the host configuration takes precedence over the fallback
ConfigStore file; the Meridian environment override takes precedence over both.

- Keep secrets out of `appsettings.json`; use environment-variable or secret-store injection.
- Treat auth/rate-limit runtime variables (`MDC_API_KEY`, `MDC_AUTH_MODE`, `MDC_DISABLE_RATE_LIMIT`) as production control-plane settings.
- Validate effective runtime config before enabling execution/direct-lending/security-master mutation workflows.

## Operator Verification Steps

```bash
# 1) Validate config shape and startup viability
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config

# 2) Verify effective config sources (default/config/env)
curl http://localhost:8080/api/config/effective
```

See also: [Environment Variables](environment-variables.md), [Provider Credential Operations](../operators/provider-credentials.md).
