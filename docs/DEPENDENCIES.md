# Dependencies

This document lists the primary dependencies used by the Meridian solution.

**Source of truth:** package versions are centrally managed in `Directory.Packages.props` via Central Package Management (CPM). Individual project files reference packages without version numbers. See [Central Package Management Guide](development/central-package-management.md) for details.

## Core Framework

- **.NET 9.0** - Target framework (`net9.0`)
- **C# 13** - Language version (default for .NET 9.0) with nullable reference types and records

---

## Meridian (Main Project)

### Configuration & Hosting

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.Configuration | 10.0.2 | Configuration abstractions |
| Microsoft.Extensions.Configuration.Json | 9.0.12 | JSON config provider |
| Microsoft.Extensions.Configuration.Binder | 10.0.2 | Bind config to options |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 9.0.12 | Env var config provider |
| Microsoft.Extensions.Configuration.CommandLine | 9.0.12 | CLI config provider |
| Microsoft.Extensions.DependencyInjection | 9.0.12 | DI container |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.12 | DI abstractions |
| Microsoft.Extensions.Hosting | 9.0.12 | Generic host support |
| Microsoft.AspNetCore.App | (framework) | Web dashboard + HTTP endpoints |

### Logging & Observability

| Package | Version | Purpose |
|---------|---------|---------|
| Serilog | 4.3.0 | Structured logging |
| Serilog.Sinks.Console | 6.1.1 | Console sink |
| Serilog.Sinks.File | 6.0.0 | File sink |
| Serilog.Extensions.Logging | 8.0.0 | Microsoft.Extensions.Logging bridge |
| Serilog.Settings.Configuration | 8.0.4 | Config-based logging |
| prometheus-net | 8.2.1 | Metrics collector |
| prometheus-net.AspNetCore | 8.2.1 | ASP.NET Core metrics |
| OpenTelemetry | 1.15.0 | Tracing API |
| OpenTelemetry.Api | 1.15.0 | Tracing abstractions |
| OpenTelemetry.Extensions.Hosting | 1.15.0 | Host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.0 | ASP.NET Core instrumentation |
| OpenTelemetry.Instrumentation.Http | 1.15.0 | HttpClient instrumentation |
| OpenTelemetry.Exporter.Console | 1.14.0 | Console exporter |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.0 | OTLP exporter |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.14.0-beta.1 | Prometheus exporter |

### Resilience & Networking

| Package | Version | Purpose |
|---------|---------|---------|
| Polly | 8.6.5 | Resilience policies |
| Polly.Extensions | 8.6.5 | Extensions for Polly |
| Microsoft.Extensions.Http | 9.0.12 | HttpClientFactory |
| Microsoft.Extensions.Http.Polly | 9.0.12 | Polly integration |
| System.Net.WebSockets.Client | 4.3.2 | WebSocket client |
| System.Net.Http.Json | 10.0.2 | JSON HttpClient helpers |

### Storage & Data Formats

| Package | Version | Purpose |
|---------|---------|---------|
| System.Text.Json | 10.0.2 | JSON serialization |
| Newtonsoft.Json | 13.0.4 | JSON compatibility |
| System.IO.Compression | 4.3.0 | Gzip compression |
| Parquet.Net | 5.5.0 | Parquet storage |
| System.Threading.Channels | 10.0.2 | High-throughput pipelines |
| System.IO.Pipelines | 10.0.2 | High-performance I/O |

### Data & Analytics

| Package | Version | Purpose |
|---------|---------|---------|
| QuantConnect.Lean | 2.5.17414 | Lean engine integration |
| QuantConnect.Lean.Engine | 2.5.17414 | Lean engine runtime |
| QuantConnect.Common | 2.5.17414 | Lean shared types |
| QuantConnect.Indicators | 2.5.17414 | Technical indicators |
| Skender.Stock.Indicators | 2.7.1 | Technical analysis helpers |
| System.Reactive | 6.1.0 | Reactive extensions |

### Tooling

| Package | Version | Purpose |
|---------|---------|---------|
| BenchmarkDotNet | 0.15.8 | Performance benchmarking |
| Websocket.Client | 5.3.0 | High-performance WebSocket client |
| FluentValidation | 12.1.1 | Validation for configuration/options |

### Interactive Brokers (Optional)

The IB API is not bundled via NuGet in this repo. To enable IB support:
- Install the IB API package/dll manually
- Build with the `IBAPI` compilation constant

---

## Tests

Testing dependencies are defined in the individual test projects under `tests/`. Refer to each test `.csproj` for the authoritative versions.
