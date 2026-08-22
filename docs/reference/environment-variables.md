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
- **Canonical does not mean higher precedence.** Where both spellings exist, the bare one wins:
  `ConfigEnvironmentOverride` applies the legacy aliases after the `MDC_` entries. Set one
  spelling per setting rather than relying on which takes effect.
- Use double underscore (`__`) for .NET configuration binding: `ALPACA__KEYID` maps to `Alpaca:KeyId`

## High-Risk Runtime and Security Controls (`MDC_*`)

These variables control auth, mutation safety, runtime mode, and diagnostics behavior. Treat them as operator-only controls.

| Variable | Config Path | Description | Risk if misconfigured |
|----------|-------------|-------------|-----------------------|
| `MDC_API_KEY` | Runtime auth middleware | Enables API-key enforcement for `/api/*` routes. | Leaving unset in shared/non-local environments can expose mutation endpoints. |
| `MDC_API_KEY_ROLE` | Runtime auth middleware | Role a validated API key carries. `ReadOnly` (explicit or defaulted), `Analysis`, and `Executive` hold no `Manage`, `Modify`, `Execute`, or `Admin` permission, so all three are limited to `GET`, `HEAD`, and `OPTIONS`, plus routes declaring `ExportData` and the few `POST` routes carrying an explicit non-mutating declaration (each still enforces its own permission). See the method cap in `docs/reference/api-reference.md`. | Naming a broad role turns a single shared key into that role's full reach; an unknown value fails requests closed. |
| `MDC_AUTH_MODE` | Runtime auth middleware | Auth mode selector for UI/API auth pipeline. | Incorrect mode can disable expected permission checks. |
| `MDC_ANONYMOUS_ROLE` | Runtime auth middleware | Role an unauthenticated caller carries when `MDC_AUTH_MODE=optional`; unset means no authorization, so governed routes refuse anonymous callers. `ReadOnly`, `Analysis`, and `Executive` are additionally limited to `GET`, `HEAD`, and `OPTIONS`, plus routes declaring `ExportData` and the few `POST` routes carrying an explicit non-mutating declaration, matching the API-key rule. It does not create a login session: session-owned mutations remain unavailable, while an explicitly scoped local/demo operator can use read-only workstation bootstrap routes. | Setting it grants every anonymous caller that role on permission-gated routes — appropriate for a single-operator local deployment, not for a shared host. |
| `MDC_ANONYMOUS_TENANT` | Runtime auth middleware | Tenant and company authority an anonymous caller carries in optional mode. Without it the `/api/workstation/*` group still refuses a non-demo caller; the seeded demo host supplies its own tenant. | Names which stored records the anonymous caller reads and writes; point it at the deployment's own tenant, not a shared one. |
| `MDC_USERS` | Runtime auth bootstrap | JSON array of operator accounts using `passwordHash` values. | Missing hashes leave required auth unconfigured; plaintext `password` values are ignored. |
| `MDC_DEMO_USERS` | Runtime auth bootstrap | Development/Test-only JSON array of demo accounts using `passwordHash` values. | Must not be used as a production credential source. |
| `MDC_USERNAME` | Runtime auth bootstrap | Legacy single-user bootstrap username used with `MDC_PASSWORD_HASH`. | Intended only for bootstrap/local use; prefer governed accounts. |
| `MDC_BOOTSTRAP_TOKEN` | Runtime auth bootstrap | One-time token that authorizes creating the first operator account when no credential exists yet. The lifecycle supervisor generates one and passes it to the host it starts; a from-source launch must set it explicitly, or the login surface has no route to a first credential. | Treat as a credential: anyone holding it can create the initial account. Do not reuse or persist it beyond first-run. |
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
| `MDC_DATASOURCE` | `DataSource` | Real-time streaming provider: `IB`, `Alpaca`, `NYSE`, `Synthetic`. `ConfigEnvironmentOverride.ParseDataSource` accepts **any** defined `DataSourceKind`, rejecting only undefined values, so a backfill-only kind such as `Yahoo` is *not* refused here — it is accepted and then fails later, when `CollectorModeRunner` asks `ProviderRegistry` for a streaming client that `ProviderFeatureRegistration.Registry.cs` never registered. `Polygon` parses and registers a streaming factory, but that factory constructs `PolygonMarketDataClient` without passing `PolygonOptions`, so it always runs with an empty API key; no environment variable reaches it. | No | `Synthetic` |
| `MDC_SYMBOLS` | `Symbols` | Comma-separated symbols to subscribe, replacing the configured list (`SPY,QQQ`). Values are uppercased because `SymbolConfigValidator` matches `^[A-Z0-9\-\.\/]+$`. The new entries inherit `SubscribeTrades`, `SubscribeDepth`, and `DepthLevels` from the first configured symbol, so this changes *which* symbols are collected, not *how* — a config that disabled depth is not silently re-enabled. With nothing configured, trades are on and **depth is off**, because the environment cannot know whether the selected provider advertises `Level2Book`. Contract identity is never inherited: symbols needing per-symbol fields (options, preferreds with a `LocalSymbol`) must be configured in JSON instead. Unset **or empty** leaves the configured list alone — `ApplyOverrides` treats an empty value as absent for every variable. A non-empty value that yields no symbols (`" , , "`) fails startup rather than silently subscribing to nothing. | No | config file |

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
| `ALPACA_KEY_ID` | `Alpaca:KeyId` | Bare alias that **takes precedence over** `MDC_ALPACA_KEY_ID`: `ApplyOverrides` applies it later, and the backfill path reads it directly via `ProviderCredentialResolver` ahead of any configured value. Set this one, or unset it, if both exist. | — | — |
| `ALPACA_SECRET_KEY` | `Alpaca:SecretKey` | Bare alias that **takes precedence over** `MDC_ALPACA_SECRET_KEY`, for the same reason. | — | — |
| `ALPACA__KEYID` | `Alpaca:KeyId` | .NET config binding format | — | — |
| `ALPACA__SECRETKEY` | `Alpaca:SecretKey` | .NET config binding format | — | — |

## Polygon Provider

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `POLYGON_API_KEY` | `Backfill:Providers:Polygon:ApiKey` | Polygon.io API key | When using Polygon | — |
| `POLYGON__APIKEY` | `Polygon:ApiKey` | .NET config binding format | — | — |

## Interactive Brokers

IB *credentials* are managed via TWS/Gateway, not environment variables. The *connection* settings
are environment-configurable:

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_IB_HOST` | `IB:Host` | TWS/Gateway host | No | `127.0.0.1` |
| `MDC_IB_PORT` | `IB:Port` | TWS/Gateway socket port | No | `7497` (paper-safe) |
| `MDC_IB_CLIENT_ID` | `IB:ClientId` | API client id; must be unique per concurrent connection | No | `1` |
| `MDC_IB_PAPER` | `IB:UsePaperTrading` | Use the paper-trading account (`true`/`false`) | No | `true` |
| `MDC_IB_SUBSCRIBE_DEPTH` | `IB:SubscribeDepth` | Request Level 2 market depth (`true`/`false`) | No | `true` |
| `MDC_IB_DEPTH_LEVELS` | `IB:DepthLevels` | Depth levels to request | No | `10` |
| `MDC_IB_TICK_BY_TICK` | `IB:TickByTick` | Use tick-by-tick streams (`true`/`false`) | No | `true` |

### IB Client Portal

The Client Portal HTTP surface is separate from the TWS/Gateway socket used for market data,
historical data, and order routing.

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_IB_CLIENT_PORTAL_ENABLED` | `IBClientPortal:Enabled` | Enable the Client Portal gateway path (`true`/`false`) | No | `false` |
| `MDC_IB_CLIENT_PORTAL_BASE_URL` | `IBClientPortal:BaseUrl` | Client Portal gateway base URL | No | `https://localhost:5000` |
| `MDC_IB_CLIENT_PORTAL_ALLOW_SELF_SIGNED` | `IBClientPortal:AllowSelfSignedCertificates` | Accept the gateway's self-signed certificate (`true`/`false`). Only ever honoured for loopback hosts — a non-loopback gateway must present a valid certificate regardless of this setting. | No | `true` |

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

## Database Persistence

**Without any of these variables, every money-path store (ledger, security master, fund
accounts, fund structure, direct lending, asset operations, banking, money market, reporting,
scoped access) runs in-memory and loses its data on restart.** Hosts log a loud
`PERSISTENCE: NONE`/`PARTIAL` warning, report it in the `postgresql` readiness check, and the
browser workstation shows a persistent red banner until persistence is configured.

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `MERIDIAN_DATABASE_URL` | Unified PostgreSQL connection for **all** store domains. Accepts `postgres://user:pass@host:port/db` URLs or Npgsql keyword form. Propagated at startup into every unset `MERIDIAN_*_CONNECTION_STRING`. | No | — (in-memory stores) |
| `MERIDIAN_USE_INMEMORY_GOVERNANCE` | Selects file-backed governance stores instead of PostgreSQL. Without it, and without a connection string for the fund-accounts and fund-structure domains, startup fails closed with a diagnostic naming the missing variables (`StorageFeatureRegistration.EnsureGovernancePersistenceProfile`). Rejected when the environment resolves to Production. Local and development scenarios only; `--seed-demo` and `--demo` set it for you. | No | unset (persistence required) |
| `MERIDIAN_LEDGER_CONNECTION_STRING` | Ledger journal store (per-domain override; wins over `MERIDIAN_DATABASE_URL`). | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` | Security Master store (also inherited by Direct Lending unless its dedicated variable is set). | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` | Fund accounts governance store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING` | Fund structure governance store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING` | Asset operations projection store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_BANKING_CONNECTION_STRING` | Banking store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_MONEY_MARKET_CONNECTION_STRING` | Money market fund store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING` | Scoped access assignment store. | No | inherits `MERIDIAN_DATABASE_URL` |
| `MERIDIAN_REPORTING_CONNECTION_STRING` | Reporting stores. | No | inherits the ledger connection |
| `MERIDIAN_DIRECT_LENDING_CONNECTION_STRING` | Direct lending store (dedicated/test databases only). | No | inherits the Security Master connection |

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
| `Alpaca:*` | `MDC_ALPACA_*` | Alpaca feed + credential override path. |

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
