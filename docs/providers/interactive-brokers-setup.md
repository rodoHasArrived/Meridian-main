# Interactive Brokers API Setup Guide

This document provides instructions for setting up the Interactive Brokers API (IBApi) with the Meridian project.

## Overview

The Interactive Brokers API is **not available as a standard NuGet package** and must be installed manually. The Meridian uses conditional compilation (`#if IBAPI`) to allow the project to build with or without IB API support.

## Installation Options

### Option 1: Manual DLL Reference (Recommended for Development)

1. **Download the IB API**
   - Visit: https://www.interactivebrokers.com/en/trading/tws-api.php
   - Download the "TWS API" for Windows, Linux, or macOS
   - Current version (as of 2026): 10.19 or later

2. **Extract and Build IBApi**

   **For Windows:**
   ```powershell
   # Extract the downloaded archive
   # Navigate to: TWS API\source\CSharpClient

   # Build the solution
   cd "C:\TWS API\source\CSharpClient"
   dotnet build CSharpAPI.sln -c Release

   # The DLL will be in: bin\Release\netX.X\
   ```

   **For Linux/macOS:**
   ```bash
   # Extract the archive
   # Navigate to: TWS API/source/CSharpClient

   cd ~/TWS\ API/source/CSharpClient
   dotnet build CSharpAPI.sln -c Release
   ```

3. **Copy the DLL to Your Project**
   ```bash
   # Create a lib directory in your solution
   mkdir -p Meridian/lib/IBApi

   # Copy the DLL
   cp path/to/CSharpAPI/bin/Release/net6.0/CSharpAPI.dll Meridian/lib/IBApi/
   ```

4. **Update Meridian.csproj**

   Uncomment and modify the IBApi reference in `Meridian.csproj`:

   ```xml
   <ItemGroup>
     <!-- Interactive Brokers API - Local Reference -->
     <Reference Include="IBApi">
       <HintPath>..\..\lib\IBApi\CSharpAPI.dll</HintPath>
       <Private>true</Private>
     </Reference>
   </ItemGroup>

   <PropertyGroup>
     <DefineConstants>$(DefineConstants);IBAPI</DefineConstants>
   </PropertyGroup>
   ```

### Option 2: Build from Source and Reference Project

1. **Clone IB API Source**
   ```bash
   # Download and extract TWS API
   # Copy the CSharpClient folder to your solution
   cp -r "TWS API/source/CSharpClient" Meridian/external/IBApi
   ```

2. **Add as Project Reference**

   Add to `Meridian.sln`:
   ```
   dotnet sln Meridian.sln add external/IBApi/CSharpAPI.csproj
   ```

3. **Reference in Meridian.csproj**
   ```xml
   <ItemGroup>
     <ProjectReference Include="..\..\external\IBApi\CSharpAPI.csproj" />
   </ItemGroup>

   <PropertyGroup>
     <DefineConstants>$(DefineConstants);IBAPI</DefineConstants>
   </PropertyGroup>
   ```

### Option 3: Compile-Only Smoke Build

**Warning**: This uses Meridian's local compile-only `IBApi` stub and is meant only to keep the IB-gated code path buildable in automation.

1. **Meridian smoke-build path**

   Meridian now supports an opt-in infrastructure-only smoke build using a local compile stub for the `IBApi` surface:

   ```powershell
   ./scripts/dev/build-ibapi-smoke.ps1
   ```

   Equivalent manual build:

   ```powershell
   dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj `
     -c Release `
     -p:EnableWindowsTargeting=true `
     -p:EnableIbApiSmoke=true
   ```

   This path is intended for compile verification only. It does not prove live connectivity to TWS/Gateway or compatibility with the official vendor DLL.

### Option 4: Build Without IB API

If you don't need Interactive Brokers support, the project will build successfully without the API:

```bash
# Build without IBAPI defined
dotnet build

# The IBMarketDataClient will use IBSimulationClient internally
# This keeps the IB provider surface buildable and testable without a live IB installation
```

## Enabling IB API Support

Once the IBApi DLL or project reference is added, enable it by defining the `IBAPI` constant:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);IBAPI</DefineConstants>
</PropertyGroup>
```

Or pass it during build:
```bash
dotnet build -p:DefineConstants="IBAPI"
```

## TWS/IB Gateway Setup

### Prerequisites

1. **Interactive Brokers Account**
   - Live or Paper Trading account
   - Enable API access in account management

2. **TWS or IB Gateway**
   - Download from: https://www.interactivebrokers.com/en/trading/tws.php
   - Install and configure for API access

### Configuration Steps

1. **Enable API Connections**

   In TWS/IB Gateway:
   - Navigate to: **File → Global Configuration → API → Settings**
   - Check: **Enable ActiveX and Socket Clients**
   - Set **Socket Port**: `7497` (paper trading) or `7496` (live trading)
   - Check: **Allow connections from localhost**
   - For remote connections, add trusted IPs
   - Uncheck: **Read-Only API** (if you need trading capabilities)

2. **Configure Permissions**
   - Check: **Download open orders on connection**
   - Check: **Use negative numbers to bind automatic orders**
   - Set **Master API Client ID** (optional)

3. **Market Data Subscriptions**
   - Ensure you have active market data subscriptions for the symbols you want to collect
   - Check subscriptions in Account Management → Market Data Subscriptions

### Connection Parameters

Default connection settings in `EnhancedIBConnectionManager.cs`:

```csharp
_conn = new EnhancedIBConnectionManager(
    _router,
    host: "127.0.0.1",  // TWS/Gateway host
    port: 7497,          // Paper trading port (7496 for live)
    clientId: 1          // Unique client ID
);
```

Customize in `appsettings.json`:

```json
{
  "InteractiveBrokers": {
    "Host": "127.0.0.1",
    "Port": 7497,
    "ClientId": 1,
    "Enabled": true
  }
}
```

## Troubleshooting

### Build Errors

**Error**: `The type or namespace name 'IBApi' could not be found`

**Solution**:
- Ensure IBApi DLL/project is referenced correctly
- Verify `IBAPI` constant is defined
- Check HintPath in .csproj points to correct location

### Connection Errors

**Error**: `Connection refused` or `Unable to connect to TWS`

**Solutions**:
1. Verify TWS/IB Gateway is running
2. Check API settings are enabled (see Configuration Steps above)
3. Verify port number (7497 for paper, 7496 for live)
4. Check firewall settings allow localhost connections
5. Ensure only one client with the same ClientId is connected

**Error**: `Market data farm connection is inactive`

**Solutions**:
1. Wait a few seconds - connection may still be establishing
2. Check market data subscriptions in account management
3. Verify market is open (some data only available during market hours)
4. Check IB service status: https://www.interactivebrokers.com/en/support/systemStatus.php

### Data Issues

**Error**: `No market data permissions for requested instrument`

**Solutions**:
1. Subscribe to required market data in Account Management
2. Verify symbol format (use `Symbol` for stocks, `Forex` for currencies)
3. Check exchange is specified correctly in `SymbolConfig`

**Error**: `Historical data request pacing violation`

**Solutions**:
1. Implement rate limiting (IB has strict request limits)
2. Wait 10+ seconds between historical data requests
3. Reduce concurrent subscription count
4. Use tick-by-tick data instead of historical bars when possible

## API Documentation

### Official Resources

- **IB API Guide**: https://interactivebrokers.github.io/tws-api/
- **API Reference**: https://interactivebrokers.github.io/tws-api/classIBApi_1_1EClient.html
- **Market Data Types**: https://interactivebrokers.github.io/tws-api/market_data_type.html
- **Tick Types**: https://interactivebrokers.github.io/tws-api/tick_types.html

### Community Resources

- **IB API Users Group**: https://groups.io/g/twsapi
- **Stack Overflow**: Tag `interactive-brokers-api`
- **IB Insync** (Python reference): https://github.com/erdewit/ib_insync

## Testing Without Live Connection

### Using IBSimulationClient

Build without `IBAPI` defined to use the stub implementation:

```bash
dotnet run --project src/Meridian/Meridian.csproj
```

The `IBMarketDataClient` will automatically delegate to `IBSimulationClient`.

That simulation path is intentional: the provider stays visible and testable in non-`IBAPI` builds, but it is not a substitute for a real TWS/Gateway connection.

### Self-Test Mode

Run built-in self-tests:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
```

This tests the data pipeline without requiring any provider connection.

## Production Deployment

### Recommendations

1. **Use IB Gateway Instead of TWS**
   - Lighter weight
   - No GUI overhead
   - More suitable for server deployment
   - Same API interface

2. **Implement Connection Monitoring**
   - The `EnhancedIBConnectionManager` includes automatic reconnection
   - Monitor `ConnectionStatus` events
   - Set up alerts for prolonged disconnections

3. **Configure Rate Limits**
   - IB has strict message rate limits
   - Implement throttling in subscription manager
   - Batch subscription requests

4. **Handle Market Hours**
   - Some data types only available during market hours
   - Implement schedule-aware subscription logic
   - Handle graceful degradation when markets closed

5. **Use Paper Trading for Development**
   - Always develop against paper trading account
   - Port 7497 for paper trading
   - Identical API to live trading

## License and Terms

- The Interactive Brokers API is proprietary software
- Review IB's API license agreement before redistribution
- Commercial use requires active IB account
- Market data subscriptions are billed separately

## Alternative Providers

If IB API setup is too complex, consider:

1. **Alpaca** - Simple WebSocket API, no special SDK required (already supported)
2. **Polygon** - REST and WebSocket APIs (stub implementation ready)
3. **Alpha Vantage** - Free tier available
4. **Yahoo Finance** (historical data only)

---

**Version:** 1.7.0
**Last Updated:** 2026-03-21
**TWS API Version:** 10.19+
**Tested With:** .NET 9.0
**See Also:** [Getting Started](../getting-started/README.md) | [Configuration](../HELP.md#configuration) | [Operator Runbook](../operations/operator-runbook.md)
