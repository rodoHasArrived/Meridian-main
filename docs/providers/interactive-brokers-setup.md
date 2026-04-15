# Interactive Brokers Native Setup

This guide covers Meridian's three Interactive Brokers modes and the preferred local vendor setup for real TWS/Gateway connectivity.

Use this together with [provider-confidence-baseline.md](provider-confidence-baseline.md) and [provider-validation-matrix.md](../status/provider-validation-matrix.md).

## Modes

| Mode | Build switch | What it is for | What it is not |
|---|---|---|---|
| Guidance | none | Default repo build. Keeps IB visible in UX and returns setup guidance. | Real market data, historical bars, or brokerage routing. |
| Smoke | `-p:EnableIbApiSmoke=true` | Compile-only verification of the `#if IBAPI` code path in automation. | Vendor compatibility or live connectivity proof. |
| Vendor | `-p:EnableIbApiVendor=true` | Native Interactive Brokers runtime using the official SDK from a local ignored folder. | CI-default behavior. Requires local TWS/Gateway and entitlements. |

## Official SDK Source

Meridian does not commit the Interactive Brokers SDK. The official download source is [Interactive Brokers API Software](https://interactivebrokers.github.io/).

The official site currently lists:

- Latest release: `10.45`, dated March 30, 2026
- Stable release: `10.37`, dated May 28, 2025

Keep the vendor files local only. The repo ignores:

- `external/IBApi/`
- `external/IBApi-*.msi`

## Preferred Local Layout

Place the official SDK under:

```text
external/IBApi/
```

Meridian vendor mode resolves the SDK in this order:

1. `IBApiProjectPath`
2. `IBApiDllPath`
3. Auto-discovered prebuilt DLL under `$(IBApiRoot)\TWS API\source\CSharpClient\client\bin\$(Configuration)\net8.0\CSharpAPI.dll`
4. Auto-discovered prebuilt DLL under `$(IBApiRoot)\TWS API\source\CSharpClient\client\bin\$(Configuration)\netstandard2.0\CSharpAPI.dll`
5. Auto-discovered project under `$(IBApiRoot)\TWS API\source\CSharpClient\client\CSharpAPI.csproj`

If none of those resolve, the build fails with setup guidance.

`IBApiRoot` defaults to `external/IBApi`.

## Install Steps

1. Download the official Windows installer or archive from [Interactive Brokers API Software](https://interactivebrokers.github.io/).
2. Extract or install the SDK into `external/IBApi/`.
3. If you want Meridian to use the vendor SDK directly, build Meridian with:

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
  -c Release `
  -p:EnableWindowsTargeting=true `
  -p:EnableIbApiVendor=true
```

4. If you want to point at a different local SDK location, override one or more of:

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
  -c Release `
  -p:EnableWindowsTargeting=true `
  -p:EnableIbApiVendor=true `
  -p:IBApiRoot="D:\vendor\IBApi"
```

or

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
  -c Release `
  -p:EnableWindowsTargeting=true `
  -p:EnableIbApiVendor=true `
  -p:IBApiDllPath="D:\vendor\IBApi\TWS API\source\CSharpClient\client\bin\Release\net8.0\CSharpAPI.dll"
```

Legacy `-p:DefineConstants=IBAPI` remains supported for advanced/local workflows, but `EnableIbApiVendor=true` is the preferred switch.

## Smoke Build

Use the smoke path when you only need compile verification:

```powershell
./scripts/dev/build-ibapi-smoke.ps1
```

Equivalent manual command:

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
  -c Release `
  -p:EnableWindowsTargeting=true `
  -p:EnableIbApiSmoke=true
```

Do not combine smoke mode with vendor mode.

## Runtime Configuration

Meridian now separates:

- `IB`: socket/TWS/Gateway configuration for streaming, historical data, and brokerage
- `IBClientPortal`: HTTP configuration for Client Portal portfolio/account import

Paper-safe defaults:

- `IB.Port = 7497`
- `IB.ClientId = 1`
- `IB.UsePaperTrading = true`
- `IBClientPortal.BaseUrl = https://localhost:5000`
- `IBClientPortal.AllowSelfSignedCertificates = true`

Example:

```json
{
  "DataSource": "IB",
  "IB": {
    "Host": "127.0.0.1",
    "Port": 7497,
    "ClientId": 1,
    "UsePaperTrading": true,
    "SubscribeDepth": true,
    "DepthLevels": 10,
    "TickByTick": true
  },
  "IBClientPortal": {
    "Enabled": false,
    "BaseUrl": "https://localhost:5000",
    "AllowSelfSignedCertificates": true
  }
}
```

Important:

- Do not point `IBClientPortal.BaseUrl` at the TWS/Gateway socket port.
- Client Portal import is HTTP(S); TWS/Gateway market data and brokerage use the socket connection.
- Live routing requires explicit opt-in by setting `IB.UsePaperTrading` to `false` and using the correct live port.

## Environment Overrides

Socket settings:

- `MDC_IB_HOST`
- `MDC_IB_PORT`
- `MDC_IB_CLIENT_ID`
- `MDC_IB_PAPER`
- `MDC_IB_SUBSCRIBE_DEPTH`
- `MDC_IB_DEPTH_LEVELS`
- `MDC_IB_TICK_BY_TICK`

Client Portal settings:

- `MDC_IB_CLIENT_PORTAL_ENABLED`
- `MDC_IB_CLIENT_PORTAL_BASE_URL`
- `MDC_IB_CLIENT_PORTAL_ALLOW_SELF_SIGNED`

## TWS / Gateway Setup

In TWS or IB Gateway:

1. Open `File -> Global Configuration -> API -> Settings`
2. Enable `ActiveX and Socket Clients`
3. Use `7497` for paper or `7496` for live unless your local installation is customized
4. Allow localhost connections
5. Disable `Read-Only API` if you want order routing
6. Enable `Download open orders on connection` if you want order state synchronized on connect

Meridian native vendor mode uses those socket settings for:

- `IBMarketDataClient`
- `IBHistoricalDataProvider`
- `IBBrokerageGateway`

## Client Portal Setup

Client Portal is optional and only used for portfolio/account import.

Typical local workflow:

1. Start IB Client Portal Gateway
2. Confirm it is reachable at `https://localhost:5000`
3. Set `IBClientPortal.Enabled = true`
4. Leave `AllowSelfSignedCertificates = true` for the default local certificate unless you have installed a trusted cert

## Status Endpoint

The IB status endpoint now reports:

- `buildMode`: `guidance`, `smoke`, or `vendor`
- `runtimeTarget`: `paper` or `live`
- socket readiness separately from Client Portal readiness

Use it to confirm whether the current process is only guidance/smoke or truly vendor-enabled.

## Validation Commands

Repo-grounded checks:

```powershell
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true -p:EnableIbApiSmoke=true
dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release -p:EnableWindowsTargeting=true -p:EnableIbApiVendor=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~IBRuntimeGuidanceTests|FullyQualifiedName~IBBrokerageGatewayTests|FullyQualifiedName~IBEndpointTests|FullyQualifiedName~ConfigValidatorTests|FullyQualifiedName~ConfigEnvironmentOverrideTests"
```

Manual paper-trading acceptance:

1. Connect to paper TWS/Gateway
2. Verify trades, quotes, and depth subscriptions
3. Verify historical daily and intraday bars
4. Submit, modify, and cancel a paper order
5. Verify positions, open orders, and account summary callbacks
6. Verify Client Portal portfolio import with `IBClientPortal.BaseUrl`

Store sanitized evidence under:

```text
artifacts/provider-validation/interactive-brokers/<yyyy-mm-dd>/
```

Recommended scenario folders:

- `bootstrap/`
- `server-version/`
- `market-data-entitlements/`
- `disconnect-reconnect/`

## Version Compatibility

Meridian validates the IB server version during socket connect through `IBApiVersionValidator`.

That validation checks the server handshake version, not the marketing version shown in the TWS title bar.

If the server is too old, Meridian fails fast with guidance to upgrade TWS/Gateway.
