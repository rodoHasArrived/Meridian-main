# Lean Engine Integration Guide

**Status:** ✅ Implemented
**Version:** 1.6.1
**Last Updated:** 2026-01-30

This guide provides comprehensive instructions for integrating Meridian with QuantConnect's Lean algorithmic trading engine.

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
        ├── SampleLeanAlgorithm
        ├── SpreadArbitrageAlgorithm
        └── OrderFlowAlgorithm

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

- .NET 8.0 SDK or later
- Meridian (this project)
- QuantConnect Lean (optional for standalone testing)

### Step 1: Add NuGet Packages

The required packages are already included in `Meridian.csproj`:

```xml
<PackageReference Include="QuantConnect.Lean" Version="2.5.17315" />
<PackageReference Include="QuantConnect.Lean.Engine" Version="2.5.17269" />
<PackageReference Include="QuantConnect.Common" Version="2.5.17315" />
<PackageReference Include="QuantConnect.Indicators" Version="2.5.17212" />
```

### Step 2: Restore Packages

```bash
cd Meridian
dotnet restore
```

### Step 3: Verify Installation

```bash
dotnet build
```

## Quick Start

### 1. Collect Market Data

Run Meridian to gather data:

```bash
dotnet run --project src/Meridian/Meridian.csproj
```

Data will be stored in `./data/` as:
```
data/
└── SPY/
    ├── trade/
    │   ├── 2024-01-01.jsonl
    │   └── 2024-01-02.jsonl
    └── bboquote/
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

The custom data types automatically look for data in Lean's data folder. You can:

**Option A: Use custom data provider**

```csharp
var dataProvider = new MeridianDataProvider("./data");
```

**Option B: Symlink data directory**

```bash
# Linux/Mac
ln -s /path/to/Meridian/data /path/to/Lean/Data/meridian

# Windows (as Administrator)
mklink /D C:\Lean\Data\meridian C:\Meridian\data
```

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
| `Conditions` | `List<string>` | Trade condition codes |
| `SequenceNumber` | `long` | Sequential ordering number |
| `AggressorSide` | `string` | "Buy", "Sell", or "Unknown" |

#### Usage Example

```csharp
public override void OnData(Slice data)
{
    if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
    {
        // Filter out odd lot trades
        if (trade.Conditions.Contains("ODD_LOT"))
            return;

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
- Path mapping from Lean's data folder to Meridian's data directory
- Efficient stream-based file reading

### Usage

```csharp
// In Lean configuration or algorithm
var dataProvider = new MeridianDataProvider("./data");

// The data provider automatically handles:
// - Path construction: data/SPY/trade/2024-01-01.jsonl
// - Gzip decompression: data/SPY/trade/2024-01-01.jsonl.gz
// - Error handling and logging
```

## Algorithm Examples

See the `src/Meridian/Integrations/Lean/` directory for complete examples:

- **SampleLeanAlgorithm**: Basic usage of trade and quote data
- **SpreadArbitrageAlgorithm**: Mean reversion on spread widening
- **OrderFlowAlgorithm**: Order flow imbalance strategies

## Configuration

### Lean Configuration File

Add Meridian data types to your Lean `config.json`:

```json
{
  "data-folder": "./data",
  "data-provider": "Meridian.Integrations.Lean.MeridianDataProvider"
}
```

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

This produces the expected path structure: `{Symbol}/{Type}/{Date}.jsonl`

## Performance Optimization

### Data Volume Considerations

Tick data is large:
- **SPY**: ~200,000 trades/day = ~30 MB/day (compressed)
- **Backtesting**: Reading millions of events can be slow

### Optimization Strategies

#### 1. Use Compressed Data

```bash
# Enable compression in appsettings.json
{
  "Storage": {
    "CompressOutput": true
  }
}
```

Compression provides 5-10x size reduction with minimal read overhead.

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

## Production Readiness Checklist

✅ **Code Quality**
- Clean, well-documented code
- Follows C# naming conventions
- XML documentation comments
- Error handling throughout

✅ **Performance**
- Stream-based file reading
- Supports compressed files
- Efficient JSON parsing
- Bounded memory usage with RollingWindow examples

✅ **Compatibility**
- Works with existing Meridian file organization
- Compatible with Lean Engine 2.5.x
- Supports .NET 8.0
- Apache 2.0 license compatible

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
