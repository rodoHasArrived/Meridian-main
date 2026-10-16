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
| `Backfill` | Controls historical import behavior, retry policy, and scheduling. | Explicit aliases such as `MDC_BACKFILL_ENABLED`, `MDC_BACKFILL_PROVIDER`, `MDC_BACKFILL_FROM`, and `MDC_BACKFILL_TO`. |
| `Storage` | Controls retention, partitioning, and storage pressure behavior. | `MDC_STORAGE_NAMING`, `MDC_STORAGE_PARTITION`, `MDC_STORAGE_RETENTION_DAYS`, `MDC_STORAGE_MAX_MB`. |
| `Compress` | Top-level JSONL gzip setting; this is not `Storage.CompressOutput`. | `MDC_COMPRESS`. |
| `TenantScopeEnforcement` | Defaults to `fail-closed`; retained-data readiness must pass before serving scoped work. Explicit `deployment-boundary` is temporary single-company migration compatibility. Requires restart. | `MERIDIAN_TENANT_SCOPE_ENFORCEMENT` |
| `IB`, `IBClientPortal` | Controls broker connectivity and execution-adjacent account surfaces. | Explicit aliases such as `MDC_IB_HOST`, `MDC_IB_PORT`, `MDC_IB_CLIENT_PORTAL_ENABLED`, and `MDC_IB_CLIENT_PORTAL_BASE_URL`. |
| `Alpaca`, provider blocks under `Backfill:Providers` | Provider-specific data/credential posture. | `MDC_ALPACA_*`, provider-specific keys |
| `SecurityMasterWorkbench` | Controls governed-write conflict-authority source precedence for Security Master passport edits. | n/a |
| `Serilog` | Logging signal/noise and sensitive-output posture. | `MDC_DEBUG`, `MDC_LOG_LEVEL` |
| `Tracing` | Explicit opt-in tracing, exporter destinations, sampling, and shutdown flush. Requires restart. | Runtime `Tracing` section; exporters are not enabled by `OTEL_*` variables. |

The supported aliases and their precedence are listed in [Environment Variables](environment-variables.md)
and implemented by [`ConfigEnvironmentOverride`](../../src/Meridian.Core/Config/ConfigEnvironmentOverride.cs).
Do not infer arbitrary environment names from section names: its generic `MDC_` path only applies
the branches implemented by the override service. Other settings may be read by host configuration
or dedicated runtime services instead.

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
option also owns a `Meridian.Pipeline` meter provider for its existing metrics decorator. Metrics use
the same explicit console/OTLP exporter flags and destination as traces; both providers flush during
shutdown and are disposed by the host. The compatibility option does not enable exporters by default,
and `Tracing.Enabled` alone does not add pipeline metrics instrumentation.

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
# Validate effective AppConfig values without starting the host
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config

# Show the configuration summary
dotnet run --project src/Meridian/Meridian.csproj -- --show-config
```

`--validate-config` runs the `AppConfig` validator after configuration normalization and
environment overrides. It does not start provider sessions, connect to PostgreSQL, validate every
host service, or prove startup viability. For a running desktop API, `GET /api/config/effective`
returns selected settings and recognized environment-source annotations; use the actual host URL
and the authentication described in [API Reference](api-reference.md#authentication). It is not an
inventory of every host configuration value. Use the [operator preflight](../operators/preflight-checklist.md)
for runtime and persistence checks.

See also: [Environment Variables](environment-variables.md), [Provider Credential Operations](../operators/provider-credentials.md).
