# StockSharp Connector Guide

**Last Updated:** 2026-03-31

This guide documents the StockSharp connector types currently recognized by Meridian and shows the minimum configuration shape for each one.

Use this guide together with [Provider Confidence Baseline](provider-confidence-baseline.md). Meridian currently validates StockSharp in two layers:

- Offline / CI baseline: connector metadata, stub guidance, and representative conversion contracts.
- Manual runtime verification: installed connector packages plus the local vendor software or credentials that each connector needs.

Wave 1 intentionally validates only this adapter set:

| Adapter | Wave 1 role |
|---|---|
| `Rithmic` | validated adapter |
| `IQFeed` | validated adapter |
| `CQG` | validated adapter |
| `InteractiveBrokers` / `IB` | validated adapter |

The following connectors remain recognized in code, but they are optional/example paths outside the Wave 1 confidence gate:

| Adapter | Wave 1 role |
|---|---|
| `Binance` | optional/example connector |
| `Coinbase` | optional/example connector |
| `Kraken` | optional/example connector |

The runtime wiring is implemented in:

- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorCapabilities.cs`
- `src/Meridian.Core/Config/StockSharpConfig.cs`

## Overview

Meridian's StockSharp integration is an optional connector-runtime path. It is best used when you need a broker/feed that does not fit Meridian's native WebSocket-style providers.

Recognized named connector types in the current code:

| Connector | `ConnectorType` | Streaming | Historical | Trades | Quotes | Depth | Notes |
|---|---|---:|---:|---:|---:|---:|---|
| Rithmic | `Rithmic` | Yes | Yes | Yes | Yes | Yes | Futures-focused, certificate commonly required |
| IQFeed | `IQFeed` | Yes | Yes | Yes | Yes | Yes | Requires local IQFeed client |
| CQG | `CQG` | Yes | Yes | Yes | Yes | Yes | Demo-server support available |
| Interactive Brokers | `InteractiveBrokers` or `IB` | Yes | Yes | Yes | Yes | Yes | Runs through TWS or IB Gateway |
| Binance | `Binance` | Yes | Yes | Yes | Yes | Yes | Requires StockSharp crowdfunding package support |
| Coinbase | `Coinbase` | Yes | Yes | Yes | Yes | Yes | Supports sandbox mode |
| Kraken | `Kraken` | Yes | Yes | Yes | Yes | Yes | Configurable order book depth |
| Custom adapter | custom `ConnectorType` + `AdapterType` | Depends | Depends | Depends | Depends | Depends | Use when loading a StockSharp adapter by type name |

The table above describes the named connector types Meridian recognizes in code. It is not a claim that every connector is part of the Wave 1 gate or available in the default build. The current Wave 1 baseline validates the `Rithmic`, `IQFeed`, `CQG`, and `InteractiveBrokers` adapters; the crypto connectors remain optional/example paths until they receive the same evidence treatment.

## Common Settings

All connectors share the top-level `StockSharp` section:

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Rithmic",
    "UseBinaryStorage": false,
    "StoragePath": "data/stocksharp/{connector}",
    "EnableRealTime": true,
    "EnableHistorical": true
  }
}
```

## Connector Examples

### Rithmic

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Rithmic",
    "Rithmic": {
      "Server": "Rithmic Test",
      "UserName": "demo-user",
      "Password": "demo-password",
      "CertFile": "C:/certs/rithmic/client.p12",
      "UsePaperTrading": true
    }
  }
}
```

### IQFeed

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "IQFeed",
    "IQFeed": {
      "Host": "127.0.0.1",
      "Level1Port": 9100,
      "Level2Port": 9200,
      "LookupPort": 9300,
      "ProductId": "MERIDIAN",
      "ProductVersion": "1.0"
    }
  }
}
```

### CQG

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "CQG",
    "CQG": {
      "UserName": "demo-user",
      "Password": "demo-password",
      "UseDemoServer": true
    }
  }
}
```

### Interactive Brokers via StockSharp

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "InteractiveBrokers",
    "InteractiveBrokers": {
      "Host": "127.0.0.1",
      "Port": 7496,
      "ClientId": 1
    }
  }
}
```

### Binance

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Binance",
    "Binance": {
      "ApiKey": "your-key",
      "ApiSecret": "your-secret",
      "UseTestnet": true,
      "MarketType": "Spot",
      "SubscribeOrderBook": true,
      "OrderBookDepth": 20,
      "SubscribeTrades": true
    }
  }
}
```

### Coinbase

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Coinbase",
    "Coinbase": {
      "ApiKey": "your-key",
      "ApiSecret": "your-secret",
      "Passphrase": "your-passphrase",
      "UseSandbox": true,
      "SubscribeOrderBook": true,
      "OrderBookLevel": "level2",
      "SubscribeTrades": true
    }
  }
}
```

### Kraken

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Kraken",
    "Kraken": {
      "ApiKey": "your-key",
      "ApiSecret": "your-secret",
      "SubscribeOrderBook": true,
      "OrderBookDepth": 25,
      "SubscribeTrades": true,
      "SubscribeOhlc": false,
      "OhlcInterval": 1
    }
  }
}
```

## Custom Adapter Mode

If `ConnectorType` is not one of the named options above, Meridian falls back to custom adapter loading. In that case you must provide:

- `AdapterType`
- optionally `AdapterAssembly`
- or `ConnectionParams.AdapterType` / `ConnectionParams.AdapterAssembly`

Example:

```json
{
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "CustomVendor",
    "AdapterType": "Vendor.StockSharp.CustomMessageAdapter",
    "AdapterAssembly": "Vendor.StockSharp",
    "ConnectionParams": {
      "ApiKey": "your-key"
    }
  }
}
```

## Operational Notes

- `ConnectorType` is required for meaningful runtime behavior.
- StockSharp support is optional at build time and must be enabled via `EnableStockSharp=true`.
- Meridian's current StockSharp-enabled build pulls these package surfaces when `EnableStockSharp=true`: `StockSharp.Algo`, `StockSharp.Messages`, `StockSharp.BusinessEntities`, `StockSharp.Rithmic`, `StockSharp.IQFeed`, `StockSharp.Cqg.Com`, and `StockSharp.InteractiveBrokers`.
- Crypto connectors may require separate StockSharp packages or crowdfunding access beyond the packages currently referenced by Meridian.
- Interactive Brokers support exists both natively (`IBMarketDataClient`) and through StockSharp; use the native path when you specifically want Meridian's direct IB integration.

## Repo-Validated Offline Checks

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StockSharpSubscriptionTests|FullyQualifiedName~StockSharpMessageConversionTests|FullyQualifiedName~StockSharpConnectorFactoryTests"
```

These tests validate:

- stub guidance when `STOCKSHARP` is not enabled
- representative connector capability metadata for Rithmic, IQFeed, Interactive Brokers, and Kraken
- conversion contracts for representative futures and equities flows

They do not prove that a given connector package is installed locally or that the corresponding vendor runtime is reachable.

## Validated Adapter Samples

The tests in
`tests/Meridian.Tests/Infrastructure/Providers/StockSharpMessageConversionTests.cs`
lock the domain model contracts produced by each connector path. The sections
below describe the data flow and the expected field values for two representative
connectors: **Rithmic** (futures) and **IQFeed** (equities/options).

### Rithmic — Futures Trade and Depth Sample

Rithmic emits `ExecutionMessage` objects for trade ticks and `QuoteChangeMessage`
objects for order book snapshots. `MessageConverter` converts them to Meridian's
immutable domain records.

**Trade tick (ES front-month contract)**

| Field | StockSharp source | Meridian field |
|---|---|---|
| `msg.ServerTime` | Execution timestamp | `Trade.Timestamp` |
| `msg.TradePrice` | Last trade price | `Trade.Price` |
| `msg.TradeVolume` | Lot size | `Trade.Size` (cast to `long`) |
| `msg.OriginSide` (`Sides.Buy`) | Aggressor indicator | `Trade.Aggressor = AggressorSide.Buy` |
| `msg.SeqNum` | Sequence counter | `Trade.SequenceNumber` |
| `msg.SecurityId.BoardCode` | Exchange code (e.g., `"CME"`) | `Trade.Venue` |

Example validated output for a daily ES bar from a `TimeFrameCandleMessage`:

```csharp
var bar = new HistoricalBar(
    Symbol: "ESM5",
    SessionDate: new DateOnly(2025, 6, 1),
    Open: 5270.00m,
    High: 5285.50m,
    Low: 5265.25m,
    Close: 5280.00m,
    Volume: 1_250_000,
    Source: "stocksharp");

// Derived properties available on HistoricalBar:
// bar.Range       == 20.25m  (High - Low)
// bar.IsBullish   == true    (Close > Open)
// bar.TypicalPrice == (5285.50 + 5265.25 + 5280.00) / 3
```

**Order book snapshot**

`MessageConverter.ToLOBSnapshot` copies each `QuoteChange` entry from
`msg.Bids` / `msg.Asks` into `OrderBookLevel` records (Level 0 = best price).
Mid-price is set to `null` when the book is empty.

```csharp
// Snapshot with 3 bid and 3 ask levels at 09:30 UTC
var snapshot = new LOBSnapshot(
    Timestamp: new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
    Symbol: "ESM5",
    Bids: bids,   // Levels 0–2, descending price
    Asks: asks,   // Levels 0–2, ascending price
    MidPrice: (bids[0].Price + asks[0].Price) / 2m,
    SequenceNumber: 1001,
    Venue: "CME");
```

Rithmic capabilities confirmed in tests (via `StockSharpConnectorCapabilities.GetCapabilities("Rithmic")`):
`SupportsStreaming`, `SupportsHistorical`, `SupportsTrades`, `SupportsDepth`,
`SupportsOrderLog`, `SupportsCandles`. Supported markets include `CME`, `NYMEX`,
`COMEX`, `CBOT`, `ICE`. Supported asset types include `Future` and `FuturesOption`.

---

### IQFeed — Equities BBO Quote Sample

IQFeed emits `Level1ChangeMessage` objects for best-bid/offer updates.
`MessageConverter.ToBboQuote` extracts `BestBidPrice`, `BestBidVolume`,
`BestAskPrice`, and `BestAskVolume` from the `Changes` dictionary.

**BBO quote (AAPL on NASDAQ)**

| Field | Level1Fields key | Meridian field |
|---|---|---|
| `BestBidPrice` | `Level1Fields.BestBidPrice` | `BboQuotePayload.BidPrice` |
| `BestBidVolume` | `Level1Fields.BestBidVolume` | `BboQuotePayload.BidSize` (cast to `long`) |
| `BestAskPrice` | `Level1Fields.BestAskPrice` | `BboQuotePayload.AskPrice` |
| `BestAskVolume` | `Level1Fields.BestAskVolume` | `BboQuotePayload.AskSize` (cast to `long`) |
| Computed | `(bid + ask) / 2` when both > 0 and not crossed | `BboQuotePayload.MidPrice` |
| Computed | `ask - bid` when both > 0 and not crossed | `BboQuotePayload.Spread` |

Example validated output:

```csharp
// Normal market (bid < ask)

## Validated End-to-End Adapter Profile (Baseline)

The currently documented **validated baseline profile** is:

- **Connector:** `Rithmic`
- **Use case:** Futures trade + depth collection
- **Meridian evidence:** conversion and capability contract tests, plus subscription/runtime guidance tests
- **Scope of validation:** adapter mapping, subscription lifecycle semantics, and connector capability metadata
- **Out of scope:** live credential entitlement checks in CI

### Baseline Profile Checklist (Rithmic)

1. `EnableStockSharp=true` is set in build/runtime config.
2. `ConnectorType` is set to `Rithmic`.
3. Required package surfaces resolve for StockSharp runtime (`StockSharp.Algo` and connector-specific packages).
4. Trade and depth subscriptions can be requested without unsupported-connector exceptions.
5. Converted `ExecutionMessage` and `QuoteChangeMessage` payloads map to Meridian domain contracts (as locked by tests).

## Troubleshooting Runbook

Use this sequence when StockSharp startup or subscription fails.

1. **Build-time gate**
   - Symptom: StockSharp code path unavailable.
   - Check: build with `EnableStockSharp=true`.
   - Fix: set build/property flag and rebuild.

2. **Missing runtime package**
   - Symptom: `NotSupportedException` references `StockSharp.Algo` or connector package.
   - Check: required connector assemblies installed and resolvable.
   - Fix: install missing StockSharp package(s) for the selected connector; re-run startup.

3. **Unsupported connector type**
   - Symptom: message lists supported connectors and asks for `AdapterType` / `AdapterAssembly`.
   - Check: `ConnectorType` spelling and whether using named vs custom connector mode.
   - Fix: switch to supported named connector or supply custom adapter metadata.

4. **Credential/runtime handshake failure**
   - Symptom: connect attempts fail after package load succeeds.
   - Check: vendor endpoint reachability, credentials, cert paths (Rithmic), and connector-specific host/port fields.
   - Fix: correct connector config and verify vendor software/session is running.

5. **No market data despite connection**
   - Symptom: connected state but no trades/quotes/depth events.
   - Check: entitlement scope, subscribed symbols/instruments, and market-session timing.
   - Fix: validate vendor entitlements/instrument mapping and retry with known liquid symbols.
var payload = new BboQuotePayload(
    Timestamp: ts,
    Symbol: "AAPL",
    BidPrice: 185.45m,
    BidSize: 1500,
    AskPrice: 185.50m,
    AskSize: 1200,
    MidPrice: 185.475m,
    Spread: 0.05m,
    SequenceNumber: 100,
    Venue: "NASDAQ");

// Crossed market or zero price → MidPrice and Spread are null
var crossedPayload = new BboQuotePayload(
    ...,
    BidPrice: 186.00m,
    AskPrice: 185.90m,   // bid > ask
    MidPrice: null,
    Spread: null,
    ...);
```

IQFeed capabilities confirmed in tests (via `StockSharpConnectorCapabilities.GetCapabilities("IQFeed")`):
`SupportsStreaming`, `SupportsHistorical`, `SupportsTrades`, `SupportsDepth`,
`SupportsOrderLog`, `SupportsCandles`. Supported markets include `NYSE`, `NASDAQ`,
`AMEX`, `CME`. Supported asset types include `Stock`, `ETF`, `Option`, `Future`, `Index`.

---

### Connector Factory Stub Behaviour (non-StockSharp builds)

When `STOCKSHARP` is not defined (the default CI build), every `StockSharpConnectorFactory.Create`
call throws `NotSupportedException`. The error message always contains:

- `EnableStockSharp=true` — the build flag needed to enable the real path.
- `StockSharp.Algo` (or the connector-specific package name) — the NuGet package to install.
- The list of supported named connector types.
- A link to this guide (`docs/providers/stocksharp-connectors.md`).

This is verified in the test file under the connector-factory stub coverage, including named connector guidance paths for Rithmic, IQFeed, Binance, and Kraken.

---

## Failure Modes to Expect

- Unknown connector type without `AdapterType`: Meridian throws a `NotSupportedException` listing the built-in connector names and the `AdapterType` / `AdapterAssembly` settings needed for custom adapters.
- Missing platform/package support for a named connector: Meridian throws a `NotSupportedException` that calls out `EnableStockSharp=true`, the missing StockSharp package surface, and this guide.
- Missing adapter load/contract wiring in custom mode: Meridian throws a `NotSupportedException` that explains whether the type could not be loaded or whether it failed the `IMessageAdapter` contract check, and points back to this guide.
- Connector-side prerequisites not running locally: connection attempts fail even if configuration is valid.
