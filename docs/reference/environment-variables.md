# Environment Variable Reference

All configuration can be set via environment variables, following the [12-factor app](https://12factor.net/config) methodology. Environment variables **take precedence** over `appsettings.json` values.

## Naming Convention

- Variables prefixed with `MDC_` are the canonical form
- Legacy variables (without prefix) are also supported for backwards compatibility
- Use double underscore (`__`) for .NET configuration binding: `ALPACA__KEYID` maps to `Alpaca:KeyId`

## Core Configuration

| Variable | Config Path | Description | Required | Default |
|----------|------------|-------------|----------|---------|
| `MDC_DATA_ROOT` | `DataRoot` | Root directory for data storage | No | `data` |
| `MDC_COMPRESS` | `Compress` | Enable gzip compression (`true`/`false`) | No | `false` |
| `MDC_DATASOURCE` | `DataSource` | Streaming provider: `IB`, `Alpaca`, `Polygon`, `StockSharp`, `NYSE` | No | `IB` |

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

- **Never** commit API keys to `appsettings.json` — use environment variables
- Use a `.env` file for local development (add to `.gitignore`)
- In production, use your platform's secret management (Docker secrets, Kubernetes secrets, etc.)
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
