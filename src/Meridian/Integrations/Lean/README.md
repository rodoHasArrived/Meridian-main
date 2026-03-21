# Lean Engine Integration

This directory contains the integration between Meridian and QuantConnect's Lean Engine, enabling sophisticated algorithmic trading strategies to leverage high-fidelity market microstructure data.

## Overview

The Lean Engine integration allows you to:
- **Backtest trading algorithms** using Meridian's tick-by-tick trade data
- **Leverage BBO quotes** for spread-aware and microstructure-based strategies
- **Access raw market depth** and order flow statistics
- **Utilize production-quality data** collected from Interactive Brokers, Alpaca, or Polygon

## Architecture

The integration consists of three main components:

### 1. Custom BaseData Types

Custom data types that extend Lean's `BaseData` class to represent Meridian events:

- **`MeridianTradeData`** - Tick-by-tick trade events with:
  - Trade price and size
  - Exchange information
  - Trade conditions/flags
  - Aggressor side inference
  - Sequence numbers for ordering

- **`MeridianQuoteData`** - Best bid/offer (BBO) quotes with:
  - Bid/ask prices and sizes
  - Mid price and spread
  - Exchange routing information
  - Quote imbalance metrics

### 2. Custom Data Provider

**`MeridianDataProvider`** implements Lean's `IDataProvider` interface to:
- Read JSONL files from Meridian's data directory
- Automatically decompress `.jsonl.gz` files
- Map Meridian's file organization to Lean's data feed

### 3. Sample Algorithm

**`SampleLeanAlgorithm`** demonstrates:
- Subscribing to custom data types
- Processing tick-by-tick trades and quotes
- Implementing microstructure-aware trading logic
- Monitoring spread, imbalance, and aggressor metrics

## Quick Start

### Step 1: Collect Market Data

First, run Meridian to gather market data:

```bash
cd Meridian
dotnet run --project src/Meridian/Meridian.csproj
```

This will collect data to `./data/` in JSONL format.

### Step 2: Configure Lean Data Path

Ensure Lean can find the Meridian data. You have two options:

**Option A: Use MeridianDataProvider (Recommended)**

The custom data provider automatically handles path mapping:

```csharp
// In your Lean config or algorithm initialization
var dataProvider = new MeridianDataProvider("./data");
```

**Option B: Symlink or Copy Data**

Create a symlink from Lean's data folder to Meridian's data:

```bash
ln -s /path/to/Meridian/data /path/to/Lean/Data/marketdatacollector
```

### Step 3: Run a Lean Algorithm

Use the sample algorithm or create your own:

```bash
cd Lean
dotnet run --project Launcher/bin/Release/Launcher.dll --algorithm-type-name SampleLeanAlgorithm
```

## Data File Organization

Meridian organizes data files as:

```
data/
└── {Symbol}/
    ├── trade/
    │   ├── 2024-01-01.jsonl
    │   └── 2024-01-02.jsonl.gz
    ├── bboquote/
    │   ├── 2024-01-01.jsonl
    │   └── 2024-01-02.jsonl.gz
    └── l2snapshot/
        └── 2024-01-01.jsonl
```

The custom BaseData types automatically construct the correct file paths based on:
- Symbol name
- Data type (trade, bboquote, l2snapshot)
- Date

## Creating Custom Algorithms

### Basic Template

```csharp
using QuantConnect;
using QuantConnect.Algorithm;
using Meridian.Integrations.Lean;

public class MyAlgorithm : QCAlgorithm
{
    public override void Initialize()
    {
        SetStartDate(2024, 1, 1);
        SetEndDate(2024, 1, 31);
        SetCash(100000);

        // Subscribe to Meridian data
        AddData<MeridianTradeData>("SPY", Resolution.Tick);
        AddData<MeridianQuoteData>("SPY", Resolution.Tick);
    }

    public override void OnData(Slice data)
    {
        // Access trade data
        if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
        {
            Debug($"Trade: {trade.TradePrice:F2} x {trade.TradeSize}");
        }

        // Access quote data
        if (data.ContainsKey("SPY") && data["SPY"] is MeridianQuoteData quote)
        {
            Debug($"Quote: {quote.BidPrice:F2} / {quote.AskPrice:F2}");
        }
    }
}
```

### Advanced Example: Spread Arbitrage

```csharp
public class SpreadArbitrageAlgorithm : QCAlgorithm
{
    private decimal _normalSpread;
    private RollingWindow<decimal> _spreadWindow;

    public override void Initialize()
    {
        SetStartDate(2024, 1, 1);
        SetCash(100000);

        AddData<MeridianQuoteData>("SPY", Resolution.Tick);
        _spreadWindow = new RollingWindow<decimal>(1000);
    }

    public override void OnData(Slice data)
    {
        if (data.ContainsKey("SPY") && data["SPY"] is MeridianQuoteData quote)
        {
            var spreadBps = (quote.Spread / quote.MidPrice) * 10000;
            _spreadWindow.Add(spreadBps);

            if (_spreadWindow.IsReady)
            {
                _normalSpread = _spreadWindow.Average();

                // Trade when spread is abnormally wide
                if (spreadBps > _normalSpread * 2)
                {
                    Debug($"Wide spread detected: {spreadBps:F2} bps vs normal {_normalSpread:F2}");
                    // Implement mean reversion strategy
                }
            }
        }
    }
}
```

### Advanced Example: Order Flow Imbalance

```csharp
public class OrderFlowAlgorithm : QCAlgorithm
{
    private RollingWindow<MeridianTradeData> _trades;
    private decimal _buyVolume;
    private decimal _sellVolume;

    public override void Initialize()
    {
        SetStartDate(2024, 1, 1);
        SetCash(100000);

        AddData<MeridianTradeData>("SPY", Resolution.Tick);
        _trades = new RollingWindow<MeridianTradeData>(100);
    }

    public override void OnData(Slice data)
    {
        if (data.ContainsKey("SPY") && data["SPY"] is MeridianTradeData trade)
        {
            _trades.Add(trade);

            // Calculate order flow imbalance
            if (trade.AggressorSide == "Buy")
                _buyVolume += trade.TradeSize;
            else if (trade.AggressorSide == "Sell")
                _sellVolume += trade.TradeSize;

            if (_trades.IsReady)
            {
                var imbalance = (_buyVolume - _sellVolume) / (_buyVolume + _sellVolume);

                if (imbalance > 0.3m) // Strong buying pressure
                {
                    if (!Portfolio.Invested)
                        SetHoldings("SPY", 0.5);
                }
                else if (imbalance < -0.3m) // Strong selling pressure
                {
                    if (Portfolio.Invested)
                        Liquidate("SPY");
                }
            }
        }
    }
}
```

## Data Properties Reference

### MeridianTradeData Properties

| Property | Type | Description |
|----------|------|-------------|
| `TradePrice` | `decimal` | Execution price |
| `TradeSize` | `decimal` | Number of shares |
| `Exchange` | `string` | Exchange code (e.g., "NSDQ", "NYSE") |
| `Conditions` | `List<string>` | Trade conditions/flags |
| `SequenceNumber` | `long` | Sequential ordering number |
| `AggressorSide` | `string` | "Buy", "Sell", or "Unknown" |

### MeridianQuoteData Properties

| Property | Type | Description |
|----------|------|-------------|
| `BidPrice` | `decimal` | Best bid price |
| `BidSize` | `decimal` | Best bid size |
| `AskPrice` | `decimal` | Best ask price |
| `AskSize` | `decimal` | Best ask size |
| `MidPrice` | `decimal` | (Bid + Ask) / 2 |
| `Spread` | `decimal` | Ask - Bid |
| `SequenceNumber` | `long` | Sequential ordering number |
| `BidExchange` | `string` | Exchange with best bid |
| `AskExchange` | `string` | Exchange with best ask |

## Performance Considerations

### Data Volume

Meridian captures every tick, which can be substantial:
- **SPY**: ~100,000-500,000 ticks per day
- **AAPL**: ~50,000-200,000 ticks per day
- **File sizes**: 10-50 MB per symbol per day (compressed)

### Backtesting Performance

For optimal backtesting performance:

1. **Use compressed data**: `.jsonl.gz` files are 5-10x smaller
2. **Filter events**: Only subscribe to data types you need
3. **Batch processing**: Process multiple symbols in parallel
4. **Memory management**: Use `RollingWindow` instead of `List` for large datasets

### Example: Efficient Data Loading

```csharp
public override void Initialize()
{
    // Only subscribe to trades if you don't need quotes
    AddData<MeridianTradeData>("SPY", Resolution.Tick);

    // Use indicators instead of storing raw data
    _vwap = VWAP("SPY", 20);

    // Use rolling windows for bounded memory
    _recentTrades = new RollingWindow<MeridianTradeData>(1000);
}
```

## Troubleshooting

### Issue: "File not found" errors

**Solution**: Verify data file paths:

```bash
# Check data directory structure
ls -la data/SPY/trade/

# Ensure files exist for the backtest date range
ls data/SPY/trade/2024-01-*.jsonl
```

### Issue: No data being loaded

**Solution**: Check file format and parsing:

```bash
# Verify JSONL format (one JSON object per line)
head -1 data/SPY/trade/2024-01-01.jsonl | jq .

# Check for MarketEvent structure
head -1 data/SPY/trade/2024-01-01.jsonl | jq '.Type'
```

### Issue: Performance degradation

**Solution**: Optimize data access:

1. Use compressed files (`.jsonl.gz`)
2. Reduce date range for initial tests
3. Use `Resolution.Minute` or higher for non-tick strategies
4. Enable Lean's data caching

## Integration with Lean Features

### Indicators

Meridian data works seamlessly with Lean's indicators:

```csharp
// Standard indicators work with custom data
_sma = SMA(_symbol, 20, Resolution.Minute);
_rsi = RSI(_symbol, 14, Resolution.Minute);

// Access indicator values
if (_sma.IsReady && Securities[_symbol].Price > _sma)
{
    SetHoldings(_symbol, 0.5);
}
```

### Universe Selection

Use custom data in universe selection:

```csharp
AddUniverse<MeridianTradeData>(
    "MyUniverse",
    Resolution.Daily,
    data => data.OrderByDescending(x => x.TradeSize).Take(10).Select(x => x.Symbol)
);
```

### Risk Management

Combine with Lean's risk management:

```csharp
// Set maximum position size
Settings.FreePortfolioValuePercentage = 0.05m;

// Use trailing stops
StopMarketOrder(_symbol, -quantity, Securities[_symbol].Price * 0.98m);
```

## Advanced Topics

### Multi-Provider Data Fusion

Meridian supports multiple data providers. You can compare data quality:

```csharp
AddData<MeridianTradeData>("SPY_IB", Resolution.Tick);
AddData<MeridianTradeData>("SPY_ALPACA", Resolution.Tick);

// Compare timestamps, prices, and sequence gaps
```

### Integrity Event Monitoring

Meridian emits integrity events. Create a custom data type to monitor data quality:

```csharp
public class IntegrityEventData : BaseData
{
    // Implement Reader to parse integrity events
    // Use for data quality monitoring and filtering
}
```

### Order Book Reconstruction

For L2 depth data, create a custom algorithm that reconstructs the order book:

```csharp
public class OrderBookAlgorithm : QCAlgorithm
{
    private Dictionary<decimal, decimal> _bids = new();
    private Dictionary<decimal, decimal> _asks = new();

    // Read L2Snapshot events and maintain order book state
    // Calculate liquidity imbalance, microprice, etc.
}
```

## Resources

- **Lean Documentation**: https://www.quantconnect.com/docs/
- **Lean GitHub**: https://github.com/QuantConnect/Lean
- **Meridian Documentation**: `../../docs/`
- **Sample Algorithms**: This directory

## License

This integration maintains compatibility with:
- **Lean Engine**: Apache 2.0 License
- **Meridian**: See project LICENSE file

## Contributing

Contributions to improve the Lean integration are welcome:

1. Add support for additional data types (L2Snapshot, OrderFlow)
2. Implement more sophisticated sample algorithms
3. Optimize data loading and parsing
4. Add unit tests for custom data types

---

**Version:** 1.4.0
**Last Updated:** 2026-01-04
**See Also:** [lean-integration.md](../../../../docs/lean-integration.md) | [DEPENDENCIES.md](../../../../DEPENDENCIES.md)
