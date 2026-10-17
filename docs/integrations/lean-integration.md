# Lean Engine Integration Guide

**Status:** active
**Scope:** optional integration source; excluded from default builds
**Version:** 1.6.1
**Last Updated:** 2026-01-30

This guide covers the optional JSONL readers and sample algorithm for QuantConnect Lean. The
normal workstation build excludes these sources and packages. Enable them explicitly as described
below; their presence does not certify a Lean deployment or Meridian production readiness.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Installation](#installation)
4. [Quick Start](#quick-start)
5. [Custom Data Types](#custom-data-types)
6. [Data Provider](#data-provider)
7. [Algorithm Examples](#algorithm-examples)
8. [Configuration](#configuration)
9. [Performance Optimization](#performance-optimization)
10. [Troubleshooting](#troubleshooting)

## Overview

The Lean Engine integration enables you to:

- **Backtest algorithms** using Meridian's high-fidelity tick data
- **Access microstructure data** including order flow, aggressor side, and exchange routing
- **Leverage Lean's ecosystem** of 200+ technical indicators and risk management tools
- **Build production strategies** on institutional-grade market data

### Why This Integration?

Traditional market data feeds provide only basic OHLCV bars or quotes. Meridian captures:
- Every tick-by-tick trade with sequence numbers and conditions
- Best bid/offer updates with exchange routing information
- Order book snapshots and depth updates
- Integrity events for data quality monitoring

This granular data enables sophisticated strategies that exploit market microstructure inefficiencies.

## Architecture

### Integration Components

```
Meridian
├── Data Collection (IB, Alpaca, Polygon)
│   └── JSONL Storage (./data/)
│
└── Lean Integration
    ├── Custom BaseData Types
    │   ├── MeridianTradeData
    │   └── MeridianQuoteData
    │
    ├── Data Provider
    │   └── MeridianDataProvider (IDataProvider)
    │
    └── Sample Algorithms
        └── SampleLeanAlgorithm

Lean Engine
├── Algorithm Framework
├── Backtesting Engine
├── Indicators Library
└── Data Feed System
    └── Consumes Meridian data via custom types
```

### Data Flow

1. **Collection**: Meridian captures market data from providers
2. **Storage**: Events stored as JSONL files in `./data/`
3. **Lean Reads**: Custom BaseData types read JSONL files via `GetSource()` and `Reader()`
4. **Algorithm Consumes**: Lean algorithms receive data via `OnData(Slice)`

## Installation

### Prerequisites

- .NET 10 SDK selected by [`global.json`](../../global.json)
- Meridian (this project)
- QuantConnect Lean (optional for standalone testing)

### Step 1: Enable the optional build

[`Meridian.csproj`](../../src/Meridian/Meridian.csproj) defaults `EnableLeanIntegration` to
`false`. Setting it to `true` includes the Lean sources and these package references:

```xml
<PackageReference Include="QuantConnect.Lean" />
<PackageReference Include="QuantConnect.Lean.Engine" />
<PackageReference Include="QuantConnect.Common" />
<PackageReference Include="QuantConnect.Indicators" />
```

Versions come from `QuantConnectLeanVersion` in
[`Directory.Packages.props`](../../Directory.Packages.props), not versions copied into the project.

### Step 2: Restore Packages

Run from the repository root:

```bash
dotnet restore src/Meridian/Meridian.csproj -p:EnableLeanIntegration=true
```

### Step 3: Verify Installation

```bash
dotnet build src/Meridian/Meridian.csproj -c Release -p:EnableLeanIntegration=true
```

## Quick Start

### 1. Collect Market Data

Complete [local setup](../start/README.md) and the applicable [provider setup](../operators/README.md)
before collecting data. A configured collector can then run from the repository root:

```bash
dotnet run --project src/Meridian/Meridian.csproj
```

With `DataRoot=data`, `Storage.NamingConvention=BySymbol`, and daily partitions, the default
writer uses event-type names with their original case:
```
data/
└── SPY/
    ├── Trade/
    │   ├── 2024-01-01.jsonl
    │   └── 2024-01-02.jsonl
    └── BboQuote/
        ├── 2024-01-01.jsonl
        └── 2024-01-02.jsonl
```

### 2. Create a Simple Algorithm

Create `MyFirstAlgorithm.cs`:

```csharp
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using Meridian.Integrations.Lean;

namespace MyAlgorithms
{
    public class MyFirstAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2024, 1, 1);
            SetEndDate(2024, 1, 5);
            SetCash(100000);

            // Subscribe to Meridian data
            AddData<MeridianTradeData>("SPY", Resolution.Tick);

            Log("Algorithm initialized with Meridian data");
        }

        public override void OnData(Slice data)
        {
            if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
            {
                Log($"Trade: {trade.TradePrice:F2} x {trade.TradeSize} - {trade.AggressorSide}");
            }
        }
    }
}
```

### 3. Configure Data Path

The current [`GetSource` implementations](../../src/Meridian/Integrations/Lean/MeridianTradeData.cs)
use the **Lean process's** `MDC_DATA_ROOT` (default `./data`) and append
`marketdatacollector/{UPPERCASE_SYMBOL}/trade/{yyyy-MM-dd}.jsonl` or
`marketdatacollector/{UPPERCASE_SYMBOL}/bboquote/{yyyy-MM-dd}.jsonl`. They do not derive this path
from Lean's `data-folder` setting or the collector's storage policy.

Stage a separate Lean input tree with those names. On a case-sensitive filesystem, the default
collector's `Trade` and `BboQuote` folders need lowercase links or copies; linking only the data
root is insufficient. For example, in the shell used to launch Lean:

```bash
mkdir -p /tmp/meridian-lean/marketdatacollector/SPY
ln -s /absolute/path/to/data/SPY/Trade /tmp/meridian-lean/marketdatacollector/SPY/trade
ln -s /absolute/path/to/data/SPY/BboQuote /tmp/meridian-lean/marketdatacollector/SPY/bboquote
export MDC_DATA_ROOT=/tmp/meridian-lean
```

Keep the collector's own `DataRoot` unchanged. Use a writable staging location appropriate for
your operating system, and verify a known date/symbol before launching a full backtest.
These readers return an empty source in live mode; they are backtest readers, not live-feed adapters.

## Custom Data Types

### MeridianTradeData

Represents individual trade executions with full microstructure detail.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Symbol` | `Symbol` | Security symbol |
| `Time` | `DateTime` | Trade timestamp (UTC) |
| `Value` | `decimal` | Trade price (used by Lean as primary value) |
| `TradePrice` | `decimal` | Execution price |
| `TradeSize` | `decimal` | Number of shares |
| `Exchange` | `string` | Exchange code (e.g., "NSDQ", "NYSE") |
| `Conditions` | `List<string>` | Currently initialized empty by the reader; raw/canonical conditions are not projected |
| `SequenceNumber` | `long` | Sequential ordering number |
| `AggressorSide` | `string` | "Buy", "Sell", or "Unknown" |

#### Usage Example

```csharp
public override void OnData(Slice data)
{
    if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
    {
        // Detect aggressive buying
        if (trade.AggressorSide == "Buy" && trade.TradeSize > 10000)
        {
            Debug($"Large buy order: {trade.TradeSize} shares @ {trade.TradePrice:F2}");
            SetHoldings("SPY", 0.5);
        }
    }
}
```

### MeridianQuoteData

Represents best bid/offer updates with spread and imbalance metrics.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Symbol` | `Symbol` | Security symbol |
| `Time` | `DateTime` | Quote timestamp (UTC) |
| `Value` | `decimal` | Mid price (used by Lean as primary value) |
| `BidPrice` | `decimal` | Best bid price |
| `BidSize` | `decimal` | Best bid size |
| `AskPrice` | `decimal` | Best ask price |
| `AskSize` | `decimal` | Best ask size |
| `MidPrice` | `decimal` | (BidPrice + AskPrice) / 2 |
| `Spread` | `decimal` | AskPrice - BidPrice |
| `SequenceNumber` | `long` | Sequential ordering number |
| `BidExchange` | `string` | Exchange with best bid |
| `AskExchange` | `string` | Exchange with best ask |

#### Usage Example

```csharp
public override void OnData(Slice data)
{
    if (data.ContainsKey("SPY") && data["SPY"] is MeridianQuoteData quote)
    {
        // Calculate spread in basis points
        var spreadBps = (quote.Spread / quote.MidPrice) * 10000;

        // Calculate quote imbalance
        var totalSize = quote.BidSize + quote.AskSize;
        if (totalSize > 0)
        {
            var imbalance = (quote.BidSize - quote.AskSize) / totalSize;

            if (imbalance > 0.5m)
                Debug($"Strong bid pressure: {imbalance:P2}");
        }

        // Monitor spread widening
        if (spreadBps > 10)
            Debug($"Wide spread: {spreadBps:F2} bps");
    }
}
```

## Data Provider

The `MeridianDataProvider` implements Lean's `IDataProvider` interface to read JSONL files.

### Features

- Automatic `.jsonl.gz` decompression
- Direct file lookup, then `.gz` fallback and an alternate path under its configured root
- Efficient stream-based file reading

### Usage

```csharp
// Direct provider use; Lean host registration is a separate configuration step.
using var dataProvider = new MeridianDataProvider("/tmp/meridian-lean");
using var stream = dataProvider.Fetch(
    "/tmp/meridian-lean/marketdatacollector/SPY/trade/2024-01-01.jsonl");
// Fetch checks this file, then the same path with .gz appended.
// Missing or unreadable data returns Stream.Null and records a failed request.
```

## Algorithm Examples

[`SampleLeanAlgorithm`](../../src/Meridian/Integrations/Lean/SampleLeanAlgorithm.cs) is the
committed example for trades, quotes, spread monitoring, and order-flow accumulation. Separate
`SpreadArbitrageAlgorithm` and `OrderFlowAlgorithm` classes are not shipped.

## Configuration

### Lean Configuration File

The provider type can be selected by the Lean host once the optional Meridian assembly is built
and available to its type loader:

```json
{
  "data-folder": "./data",
  "data-provider": "Meridian.Integrations.Lean.MeridianDataProvider"
}
```

Constructing `MeridianDataProvider` in an algorithm does not register it with the engine.
Confirm host/provider activation using the Lean version you deploy. The data-source paths still
follow `MDC_DATA_ROOT` and the staging layout above.

### Data File Organization

Ensure Meridian uses a consistent file organization:

```json
{
  "Storage": {
    "NamingConvention": "BySymbol",
    "DatePartition": "Daily"
  }
}
```

This produces `{Symbol}/{Type}/{Date}.jsonl`, with `Trade`/`BboQuote` type folders.
The Lean staging layout above accounts for the readers' lowercase folder names.

## Performance Optimization

### Data Volume Considerations

Tick data is large:
- **SPY**: ~200,000 trades/day = ~30 MB/day (compressed)
- **Backtesting**: Reading millions of events can be slow

### Optimization Strategies

#### 1. Use Compressed Data

Use the top-level `Compress` option in the collector configuration:

```json
{
  "Compress": true
}
```

`MDC_COMPRESS=true` is the corresponding environment override. The custom provider handles gzip;
verify it is the active Lean provider before relying on `.jsonl.gz` fallback. Compression ratios
and read cost depend on the captured data.

#### 2. Limit Data Scope

```csharp
public override void Initialize()
{
    // Only subscribe to data you need
    AddData<MeridianTradeData>("SPY", Resolution.Tick);
    // Don't subscribe to quotes if not needed
}
```

#### 3. Use Aggregated Resolutions

```csharp
// Use minute bars instead of ticks for slower strategies
AddData<MeridianTradeData>("SPY", Resolution.Minute);
```

#### 4. Implement Data Filtering

```csharp
public override void OnData(Slice data)
{
    if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
    {
        // Skip small trades
        if (trade.TradeSize < 100)
            return;

        // Process only large trades
        ProcessTrade(trade);
    }
}
```

## Troubleshooting

### Problem: "File not found" errors

**Cause**: Data files don't exist for the requested date range

**Solution**:
1. Check data directory: `ls data/SPY/trade/`
2. Verify date range in algorithm matches available data
3. Collect data for required dates

### Problem: No data being received in OnData()

**Cause**: Incorrect file path or parsing errors

**Solution**:
1. Enable Lean logging: Set log level to DEBUG
2. Verify JSONL format: `head -1 data/SPY/trade/2024-01-01.jsonl | jq .`
3. Check for JSON parsing errors in logs

### Problem: Slow backtest performance

**Cause**: Too much tick data

**Solution**:
1. Use compressed files (`.jsonl.gz`)
2. Reduce date range for testing
3. Use higher resolution (Minute vs Tick)
4. Filter events in OnData()

### Problem: Out of memory errors

**Cause**: Large rolling windows or data structures

**Solution**:
```csharp
// Bad: Unbounded list
private List<Trade> _allTrades = new();

// Good: Bounded rolling window
private RollingWindow<MeridianTradeData> _trades = new(1000);
```

## Performance Characteristics

The figures below are historical estimates from the 2026-01-30 guide, not retained benchmark
evidence for the current optional build. Measure your own capture, hardware, and Lean deployment.

### Data Volume

Typical tick data volume per symbol per day:
- **SPY**: 100,000-500,000 ticks = 10-50 MB (compressed)
- **AAPL**: 50,000-200,000 ticks = 5-30 MB (compressed)

### Backtest Performance

On typical hardware:
- **1 day tick data**: 10-30 seconds
- **1 month tick data**: 5-10 minutes
- **1 year tick data**: 1-2 hours

### Optimizations
- Use compressed files (5-10x smaller)
- Filter unnecessary events in OnData()
- Use RollingWindow instead of List
- Aggregate to higher resolutions when possible

---

## Integration Verification

Before relying on this optional integration, build with `EnableLeanIntegration=true`, confirm the
provider is registered in the Lean host, and replay a known trade and quote file through `Reader`
and `OnData`. Check directory case, gzip fallback, parse-failure logs, and the empty `Conditions`
projection. A normal Meridian build does not compile this lane. Release acceptance remains owned
by the [implementation and readiness tracker](../product/implementation-todo-list.md).

---

## Files Reference

### Integration Files

```
src/Meridian/Integrations/Lean/
├── MeridianTradeData.cs       (Custom BaseData for trades)
├── MeridianQuoteData.cs       (Custom BaseData for quotes)
├── MeridianDataProvider.cs    (IDataProvider implementation)
├── SampleLeanAlgorithm.cs                (Working example algorithm)
└── README.md                             (Quick reference)
```

---

## Next Steps

- Review the sample algorithms in `src/Meridian/Integrations/Lean/`
- Explore Lean's documentation: https://www.quantconnect.com/docs/
- Join the QuantConnect community forum
- Contribute improvements to the integration

---

**See Also:** [Architecture](../architecture/overview.md) | [Configuration](../HELP.md#configuration) | [Lean Integration README](https://github.com/rodoHasArrived/Meridian/blob/main/src/Meridian/Integrations/Lean/README.md)
