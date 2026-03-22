# StockSharp Connector Guide

**Last Updated:** 2026-03-21

This guide documents the StockSharp connector types currently recognized by Meridian and shows the minimum configuration shape for each one.

The runtime wiring is implemented in:

- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpConnectorCapabilities.cs`
- `src/Meridian.Core/Config/StockSharpConfig.cs`

## Overview

Meridian's StockSharp integration is an optional connector-runtime path. It is best used when you need a broker/feed that does not fit Meridian's native WebSocket-style providers.

Supported named connector types in the current code:

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

## Failure Modes to Expect

- Unknown connector type without `AdapterType`: Meridian throws a `NotSupportedException` listing the built-in connector names and the `AdapterType` / `AdapterAssembly` settings needed for custom adapters.
- Missing platform/package support for a named connector: Meridian throws a `NotSupportedException` that calls out `EnableStockSharp=true`, the missing StockSharp package surface, and this guide.
- Missing adapter load/contract wiring in custom mode: Meridian throws a `NotSupportedException` that explains whether the type could not be loaded or whether it failed the `IMessageAdapter` contract check, and points back to this guide.
- Connector-side prerequisites not running locally: connection attempts fail even if configuration is valid.
