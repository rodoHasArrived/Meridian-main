# Pilot Operator Quickstart

This runbook gets a new operator from zero to a validated pilot workflow:

1. Configure credentials safely.
2. Validate providers before ingesting data.
3. Run your first backtest.
4. Promote a strategy to paper trading.
5. Troubleshoot the most common pilot blockers.

## 1) Credential setup

1. Copy `config/appsettings.sample.json` to `config/appsettings.json`.
2. Keep secrets in environment variables (never commit credentials into JSON).
3. Confirm required provider variables from the sample config header.

### Example (PowerShell)

```powershell
$env:ALPACA_KEY_ID="<your-key-id>"
$env:ALPACA_SECRET_KEY="<your-secret-key>"
$env:MDC_API_KEY="<operator-api-key>"
```

### Example (bash)

```bash
export ALPACA_KEY_ID="<your-key-id>"
export ALPACA_SECRET_KEY="<your-secret-key>"
export MDC_API_KEY="<operator-api-key>"
```

## 2) Provider validation

Run preflight checks before opening workstation flows:

```bash
# Validate config and dependency wiring
 dotnet run --project src/Meridian/Meridian.csproj -- --quick-check

# Verify provider connectivity and credentials
 dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity

# Validate startup without collection
 dotnet run --project src/Meridian/Meridian.csproj -- --dry-run
```

If any provider fails, resolve credentials/entitlements first, then re-run checks.

## 3) First backtest

1. Start Meridian in web mode:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --mode web
```

2. Backfill a narrow historical window (`SPY`, `AAPL`, ~1 month).
3. Run one baseline strategy with default risk limits.
4. Record run ID, fill count, and PnL summary for your pilot log.

## 4) Promote to paper

Promotion gate for pilot operators:

- Backtest completed with no unresolved data-quality alerts.
- Risk controls enabled (position limits, drawdown, rate throttle).
- Provider failover config verified for the pilot symbol set.
- Operator can open workstation session and inspect run/portfolio/ledger views.

After gates pass, create a paper session and monitor for one trading day.

## 5) Troubleshooting

### Authentication failures (`401`)

- Ensure `MDC_API_KEY` is set for the host process.
- Send `X-Api-Key` for `/api/*` routes.

### Provider unavailable / stale feed

- Re-check provider-specific environment variables.
- Validate account entitlements and paper/live endpoint mode.
- Switch temporarily to `Synthetic` data source for local diagnosis.

### Backfill job stalls

- Inspect rate-limit settings under `Backfill` and provider priorities.
- Check `data/_logs/` for throttling and transport errors.

### Workstation page does not load

- Confirm `/workstation` route is reachable.
- Verify web mode startup and static asset output.

## Related docs

- [Getting Started index](README.md)
- [Generated configuration schema](../generated/configuration-schema.md)
- [Provider setup guides](../providers/README.md)
- [Production status](../status/production-status.md)
