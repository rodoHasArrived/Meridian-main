# Interactive Brokers Manual Validation Checklist

This folder is reserved for sanitized proof from a local paper-trading validation run.

Use the scenario folders below when collecting evidence:

- `bootstrap/`
- `server-version/`
- `market-data-entitlements/`
- `disconnect-reconnect/`

## Capture Checklist

1. Build Meridian with vendor mode:

   ```powershell
   dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
     -c Release `
     -p:EnableWindowsTargeting=true `
     -p:EnableIbApiVendor=true
   ```

2. Start paper TWS or IB Gateway on `127.0.0.1:7497`.
3. Confirm Meridian IB status reports:
   - `buildMode = vendor`
   - `runtimeTarget = paper`
   - socket `ready = true`
4. Verify:
   - trade subscriptions
   - quote subscriptions
   - market depth subscriptions
   - historical daily bars
   - historical intraday bars
5. Submit, modify, and cancel a paper order.
6. Verify:
   - `nextValidId`
   - `openOrder`
   - `orderStatus`
   - `execDetails`
   - `position`
   - `accountSummary`
7. If Client Portal is enabled, verify portfolio import against `IBClientPortal.BaseUrl`, not the TWS socket port.

## Suggested Sanitized Evidence

- short markdown summary per scenario
- redacted logs
- endpoint responses with account identifiers removed
- screenshots with account numbers obscured

Do not commit vendor SDK files, credentials, session cookies, or unredacted account identifiers.
