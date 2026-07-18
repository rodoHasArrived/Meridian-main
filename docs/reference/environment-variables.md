---
title: Environment Variable Reference
status: active
owner: core-team
reviewed: 2026-07-17
audience: developers-and-operators
---

# Environment Variable Reference

All configuration can be set via environment variables, following the [12-factor app](https://12factor.net/config) methodology. Environment variables **take precedence** over `appsettings.json` values.

## Naming Convention

- Variables prefixed with `MDC_` are the canonical form
- Legacy variables (without prefix) are also supported for backwards compatibility
- Use double underscore (`__`) for .NET configuration binding: `ALPACA__KEYID` maps to `Alpaca:KeyId`

## High-Risk Runtime and Security Controls (`MDC_*`)

These variables control auth, mutation safety, runtime mode, and diagnostics behavior. Treat them as operator-only controls.

| Variable | Config Path | Description | Risk if misconfigured |
|----------|-------------|-------------|-----------------------|
| `MDC_API_KEY` | Runtime auth middleware | Enables API-key enforcement for `/api/*` routes. | Leaving unset in shared/non-local environments can expose mutation endpoints. |
| `MDC_AUTH_MODE` | Runtime auth middleware | Auth mode selector for UI/API auth pipeline. | Incorrect mode can disable expected permission checks. |
| `MDC_USERS` | Runtime auth bootstrap | JSON array of operator accounts using `passwordHash` values. | Missing hashes leave required auth unconfigured; plaintext `password` values are ignored. |
| `MDC_DEMO_USERS` | Runtime auth bootstrap | Development/Test-only JSON array of demo accounts using `passwordHash` values. | Must not be used as a production credential source. |
| `MDC_USERNAME` | Runtime auth bootstrap | Legacy single-user bootstrap username used with `MDC_PASSWORD_HASH`. | Intended only for bootstrap/local use; prefer governed accounts. |
| `MDC_PASSWORD_HASH` | Runtime auth bootstrap | Legacy single-user bootstrap password hash. | Unsupported or missing hashes fail closed when auth is required. |
| `MDC_PACKAGED_BUILD` | Runtime auth/credential policy | Marks packaged installs; auth is required by default and provider env fallback is disabled. | Leaving unset in customer packaging can permit development defaults. |
| `MERIDIAN_CUSTOMER_BUILD` | Runtime auth/credential policy | Marks customer builds; auth is required by default and provider env fallback is disabled. | Leaving unset in customer packaging can permit development defaults. |
| `MDC_PROVIDER_ALLOW_ENV_FALLBACK` | Provider credential migration | Explicitly allows provider secrets to be read from environment variables. | Should be temporary; can bypass the encrypted provider credential vault. |
| `MDC_DISABLE_RATE_LIMIT` | Runtime rate-limiter toggle | Disables mutation/global API throttles. | Can allow unsafe burst traffic against execution and lending mutations. |
| `MDC_FIXTURE_MODE` | Host/runtime mode | Enables fixture/test behavior in workstation flows. | Running fixture mode in production-like environments can bypass normal gates. |
| `MDC_SYNTHETIC_MODE` | `DataSource` convenience toggle | Forces synthetic/offline provider posture. | Can hide live-provider failures if accidentally left enabled. |
| `MDC_DEBUG` | `Serilog:MinimumLevel` override path | Raises logging verbosity for troubleshooting. | Can increase sensitive log exposure and operational noise. |
| `MDC_LOG_LEVEL` | `Serilog:MinimumLevel:Default` | Explicit minimum log level override. | Overly verbose output can leak operational details and overwhelm alerting. |
| `MDC_SHUTDOWN_TOKEN` | Internal host shutdown guard | Supervisor-to-host fallback capability. Installed releases generate it, store it only in a current-user DPAPI sidecar, and never expose it through runtime JSON. It is not user configuration. | Manually setting, logging, or sharing it breaks the intended current-user capability boundary. |
| `MDC_LIFECYCLE_PIPE` | Internal supervisor bridge | Per-install current-user named-pipe name injected into the host by the lifecycle supervisor. It is not user configuration. | Pointing a host at an unrelated pipe breaks status/restart coordination and session evidence. |

## Core Configuration

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_DATA_ROOT` | `DataRoot` | Root directory for data storage | No | `data` |
| `MDC_COMPRESS` | `Compress` | Enable gzip compression (`true`/`false`) | No | `false` |
| `MDC_DATASOURCE` | `DataSource` | Streaming provider: `IB`, `Alpaca`, `Polygon`, `StockSharp`, `NYSE` | No | `IB` |

Installed releases do not require users to set `MDC_DATA_ROOT`: the lifecycle supervisor injects the
resolved manifest data root, defaulting to `%LOCALAPPDATA%\Meridian\Data`.

## Lifecycle Supervisor and Release Packaging

These variables support development or release machinery. They are not end-user setup steps.

| Variable | Consumer | Description | Default |
|----------|----------|-------------|---------|
| `MDC_POSTGRES_PAYLOAD_ROOT` | Consumer setup build | Root containing runtime-specific `win-x64` and `win-arm64` PostgreSQL payloads used to build the signed installer. | Required packaging input unless `-PostgreSqlPayloadRoot` is supplied. |
| `MDC_POSTGRES_HOME` | Lifecycle supervisor development fallback | PostgreSQL installation root used only when neither the manifest nor bundled `database\bin` supplies binaries. | None; production installers bundle PostgreSQL. |
| `MDC_LIFECYCLE_PIPE` | Host bridge | Supervisor-injected current-user pipe name described above. | Derived from the canonical installation path. |
| `MDC_SHUTDOWN_TOKEN` | Host bridge | Supervisor-injected guarded HTTP fallback capability described above. | Generated per installation and DPAPI-protected. |

Dedicated database mode also injects local connection strings into the host process. Treat them as
runtime-owned values; do not persist the generated value into checked-in configuration. External
database mode instead reads the manifest-named connection-string environment variable and remains
strictly non-owning.

## Alpaca Provider

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_ALPACA_KEY_ID` | `Alpaca:KeyId` | Alpaca API key ID | When using Alpaca | — |
| `MDC_ALPACA_SECRET_KEY` | `Alpaca:SecretKey` | Alpaca API secret key | When using Alpaca | — |
| `MDC_ALPACA_FEED` | `Alpaca:Feed` | Data feed: `iex` (free), `sip` (paid) | No | `iex` |
| `MDC_ALPACA_SANDBOX` | `Alpaca:UseSandbox` | Use paper trading endpoint | No | `false` |
| `MDC_ALPACA_QUOTES` | `Alpaca:SubscribeQuotes` | Subscribe to quote data | No | `false` |
| `ALPACA_KEY_ID` | `Alpaca:KeyId` | Legacy alias for `MDC_ALPACA_KEY_ID` | — | — |
| `ALPACA_SECRET_KEY` | `Alpaca:SecretKey` | Legacy alias for `MDC_ALPACA_SECRET_KEY` | — | — |
| `ALPACA__KEYID` | `Alpaca:KeyId` | .NET config binding format | — | — |
| `ALPACA__SECRETKEY` | `Alpaca:SecretKey` | .NET config binding format | — | — |

## Polygon Provider

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `POLYGON_API_KEY` | `Backfill:Providers:Polygon:ApiKey` | Polygon.io API key | When using Polygon | — |
| `POLYGON__APIKEY` | `Polygon:ApiKey` | .NET config binding format | — | — |

## Interactive Brokers

IB credentials are managed via TWS/Gateway, not environment variables. However, StockSharp IB connector settings can be set:

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STOCKSHARP_IB_HOST` | `StockSharp:InteractiveBrokers:Host` | TWS/Gateway hostname | No | `127.0.0.1` |
| `MDC_STOCKSHARP_IB_PORT` | `StockSharp:InteractiveBrokers:Port` | TWS/Gateway port | No | `4002` |
| `MDC_STOCKSHARP_IB_CLIENT_ID` | `StockSharp:InteractiveBrokers:ClientId` | Client ID | No | `1` |

## Historical Data Provider API Keys

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `TIINGO_API_TOKEN` | `Backfill:Providers:Tiingo:ApiToken` | Tiingo API token | When using Tiingo | — |
| `TIINGO__TOKEN` | — | .NET config binding alias | — | — |
| `FINNHUB_API_KEY` | `Backfill:Providers:Finnhub:ApiKey` | Finnhub API key | When using Finnhub | — |
| `FINNHUB__TOKEN` | — | .NET config binding alias | — | — |
| `ALPHA_VANTAGE_API_KEY` | `Backfill:Providers:AlphaVantage:ApiKey` | Alpha Vantage API key | When using Alpha Vantage | — |
| `ALPHAVANTAGE__APIKEY` | — | .NET config binding alias | — | — |
| `NYSE__APIKEY` | `NYSE:ApiKey` | NYSE market data API key | When using NYSE | — |
| `NASDAQ__APIKEY` | — | Nasdaq Data Link API key | When using Nasdaq | — |

## Storage Configuration

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STORAGE_NAMING` | `Storage:NamingConvention` | File naming: `BySymbol`, `ByDate`, `ByType`, `Flat` | No | `BySymbol` |
| `MDC_STORAGE_PARTITION` | `Storage:DatePartition` | Partitioning: `None`, `Daily`, `Hourly`, `Monthly` | No | `Daily` |
| `MDC_STORAGE_RETENTION_DAYS` | `Storage:RetentionDays` | Days to retain data before cleanup | No | — (no limit) |
| `MDC_STORAGE_MAX_MB` | `Storage:MaxTotalMegabytes` | Maximum storage size in MB | No | — (no limit) |

## Backfill Configuration

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_BACKFILL_ENABLED` | `Backfill:Enabled` | Enable historical backfill | No | `false` |
| `MDC_BACKFILL_PROVIDER` | `Backfill:Provider` | Backfill provider to use | No | `composite` |
| `MDC_BACKFILL_SYMBOLS` | `Backfill:Symbols` | Comma-separated symbol list | No | — |
| `MDC_BACKFILL_FROM` | `Backfill:From` | Backfill start date (YYYY-MM-DD) | No | — |
| `MDC_BACKFILL_TO` | `Backfill:To` | Backfill end date (YYYY-MM-DD) | No | — |

## Appsettings Schema Mapping (Quick Index)

Use this together with:

- [`config/appsettings.sample.json`](../../config/appsettings.sample.json)
- [`config/appsettings.schema.json`](../../config/appsettings.schema.json)

| Schema section (`appsettings`) | Common `MDC_*` environment overrides | Notes |
|---|---|---|
| `DataRoot`, `Compress`, `DataSource` | `MDC_DATA_ROOT`, `MDC_COMPRESS`, `MDC_DATASOURCE` | Core runtime mode and storage root. |
| `Backfill:*` | `MDC_BACKFILL_ENABLED`, `MDC_BACKFILL_PROVIDER`, `MDC_BACKFILL_SYMBOLS`, `MDC_BACKFILL_FROM`, `MDC_BACKFILL_TO` | Backfill execution posture and scope. |
| `Storage:*` | `MDC_STORAGE_NAMING`, `MDC_STORAGE_PARTITION`, `MDC_STORAGE_RETENTION_DAYS`, `MDC_STORAGE_MAX_MB` | Retention and file-layout controls. |
| `StockSharp:*` | `MDC_STOCKSHARP_*` | Connector mode and adapter credential wiring. |
| `Alpaca:*` | `MDC_ALPACA_*` | Alpaca feed + credential override path. |

## StockSharp Connector Configuration

### Core Settings

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STOCKSHARP_ENABLED` | `StockSharp:Enabled` | Enable StockSharp connector | No | `false` |
| `MDC_STOCKSHARP_CONNECTOR` | `StockSharp:ConnectorType` | Connector type: `Rithmic`, `IQFeed`, `CQG`, `InteractiveBrokers`, `Custom` | No | — |
| `MDC_STOCKSHARP_ADAPTER_TYPE` | `StockSharp:AdapterType` | Custom adapter type name | No | — |
| `MDC_STOCKSHARP_ADAPTER_ASSEMBLY` | `StockSharp:AdapterAssembly` | Custom adapter assembly name | No | — |
| `MDC_STOCKSHARP_STORAGE_PATH` | `StockSharp:StoragePath` | StockSharp storage directory | No | — |
| `MDC_STOCKSHARP_BINARY` | `StockSharp:UseBinaryStorage` | Use binary storage format | No | `false` |
| `MDC_STOCKSHARP_REALTIME` | `StockSharp:EnableRealTime` | Enable real-time data | No | `true` |
| `MDC_STOCKSHARP_HISTORICAL` | `StockSharp:EnableHistorical` | Enable historical data | No | `false` |

### Rithmic

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STOCKSHARP_RITHMIC_SERVER` | `StockSharp:Rithmic:Server` | Rithmic server address | When using Rithmic | — |
| `MDC_STOCKSHARP_RITHMIC_USERNAME` | `StockSharp:Rithmic:UserName` | Username | When using Rithmic | — |
| `MDC_STOCKSHARP_RITHMIC_PASSWORD` | `StockSharp:Rithmic:Password` | Password | When using Rithmic | — |
| `MDC_STOCKSHARP_RITHMIC_CERTFILE` | `StockSharp:Rithmic:CertFile` | Certificate file path | No | — |
| `MDC_STOCKSHARP_RITHMIC_PAPER` | `StockSharp:Rithmic:UsePaperTrading` | Use paper trading | No | `false` |

### IQFeed

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STOCKSHARP_IQFEED_HOST` | `StockSharp:IQFeed:Host` | IQFeed server host | No | `127.0.0.1` |
| `MDC_STOCKSHARP_IQFEED_LEVEL1_PORT` | `StockSharp:IQFeed:Level1Port` | Level 1 data port | No | `5009` |
| `MDC_STOCKSHARP_IQFEED_LEVEL2_PORT` | `StockSharp:IQFeed:Level2Port` | Level 2 data port | No | `9200` |
| `MDC_STOCKSHARP_IQFEED_LOOKUP_PORT` | `StockSharp:IQFeed:LookupPort` | Lookup/history port | No | `9100` |
| `MDC_STOCKSHARP_IQFEED_PRODUCT_ID` | `StockSharp:IQFeed:ProductId` | IQFeed product ID | When using IQFeed | — |
| `MDC_STOCKSHARP_IQFEED_PRODUCT_VERSION` | `StockSharp:IQFeed:ProductVersion` | Product version | No | — |

### CQG

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_STOCKSHARP_CQG_USERNAME` | `StockSharp:CQG:UserName` | CQG username | When using CQG | — |
| `MDC_STOCKSHARP_CQG_PASSWORD` | `StockSharp:CQG:Password` | CQG password | When using CQG | — |
| `MDC_STOCKSHARP_CQG_DEMO` | `StockSharp:CQG:UseDemoServer` | Use demo server | No | `false` |

## Precedence Order

Configuration values are resolved in this order (last wins):

1. **Default values** — hardcoded in C# record definitions
2. **`appsettings.json`** — file-based configuration
3. **Environment variables** — overrides from the environment
4. **CLI flags** — command-line arguments (highest priority)

## Security Best Practices

- **Never** commit API keys to `appsettings.json`; use Meridian's provider credential vault for saved provider secrets.
- Use environment provider credentials only for Development/Test or explicit migration with `MDC_PROVIDER_ALLOW_ENV_FALLBACK=true`.
- Store operator passwords only as supported password hashes in `MDC_USERS`, `MDC_PASSWORD_HASH`, or the governed account store.
- Use a `.env` file only for local development (add to `.gitignore`).
- In production, use your platform's secret management (Docker secrets, Kubernetes secrets, etc.) to supply bootstrap hashes and vault/root settings.
- The system warns at startup if credentials are detected in the config file

## Viewing Effective Configuration

To see which configuration values are active and where they come from:

```bash
# Via API endpoint
curl http://localhost:8080/api/config/effective

# Via CLI
dotnet run -- --show-config
```

The `/api/config/effective` endpoint returns each setting with a `source` annotation (`default`, `config`, or `env:VAR_NAME`).
