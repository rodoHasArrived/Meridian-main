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
| `IB`, `IBClientPortal` | Controls broker connectivity and execution-adjacent account surfaces. | `MDC_IB_*` |
| `Connectivity:Probes` | Controls operator-tunable connectivity diagnostics, including TCP probe timeout behavior. | JSON/appsettings only |
| `StockSharp` | Controls connector runtime and broker/data credential surfaces. | `MDC_STOCKSHARP_*` |
| `Alpaca`, provider blocks under `Backfill:Providers` | Provider-specific data/credential posture. | `MDC_ALPACA_*`, provider-specific keys |
| `Serilog` | Logging signal/noise and sensitive-output posture. | `MDC_DEBUG`, `MDC_LOG_LEVEL` |

## Security and Mutation Guardrails

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
