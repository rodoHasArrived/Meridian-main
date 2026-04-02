# Dependencies

This document lists the primary dependencies used by the Meridian solution.

**Source of truth:** package versions are centrally managed in `Directory.Packages.props` via Central Package Management (CPM). Individual project files reference packages without version numbers. See [Central Package Management Guide](development/central-package-management.md) for details.

## Core Framework

- **.NET 9.0** - Target framework (`net9.0`)
- **C# 13** - Language version (default for .NET 9.0) with nullable reference types and records
- **F# 8** - Used in the solution's F# projects and interop layers

---

## Platform And Runtime

### Configuration & Hosting

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.Configuration | 10.0.5 | Configuration abstractions |
| Microsoft.Extensions.Configuration.Json | 10.0.5 | JSON config provider |
| Microsoft.Extensions.Configuration.Binder | 10.0.5 | Bind config to options |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.5 | Env var config provider |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.5 | CLI config provider |
| Microsoft.Extensions.DependencyInjection | 10.0.5 | DI container |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.5 | DI abstractions |
| Microsoft.Extensions.Hosting | 10.0.5 | Generic host support |
| Microsoft.AspNetCore.App | (framework) | Web dashboard + HTTP endpoints |
| Microsoft.AspNetCore.OpenApi | 9.0.14 | OpenAPI support |
| Swashbuckle.AspNetCore | 10.1.7 | Swagger generation |
| System.CommandLine | 2.0.0-beta4.22272.1 | CLI parsing support |

### Logging & Observability

| Package | Version | Purpose |
|---------|---------|---------|
| Serilog | 4.3.1 | Structured logging |
| Serilog.AspNetCore | 10.0.0 | ASP.NET Core integration |
| Serilog.Sinks.Console | 6.1.1 | Console sink |
| Serilog.Sinks.File | 7.0.0 | File sink |
| Serilog.Extensions.Logging | 10.0.0 | Microsoft.Extensions.Logging bridge |
| Serilog.Settings.Configuration | 10.0.0 | Config-based logging |
| prometheus-net | 8.2.1 | Metrics collector |
| prometheus-net.AspNetCore | 8.2.1 | ASP.NET Core metrics |
| OpenTelemetry | 1.15.1 | Tracing API |
| OpenTelemetry.Api | 1.15.1 | Tracing abstractions |
| OpenTelemetry.Extensions.Hosting | 1.15.1 | Host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.1 | ASP.NET Core instrumentation |
| OpenTelemetry.Instrumentation.Http | 1.15.0 | HttpClient instrumentation |
| OpenTelemetry.Exporter.Console | 1.15.1 | Console exporter |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.1 | OTLP exporter |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.14.0-beta.1 | Prometheus exporter |

### Resilience & Networking

| Package | Version | Purpose |
|---------|---------|---------|
| Polly | 8.6.6 | Resilience policies |
| Polly.Extensions | 8.6.6 | Extensions for Polly |
| Microsoft.Extensions.Http | 10.0.5 | HttpClientFactory |
| Microsoft.Extensions.Http.Polly | 10.0.5 | Polly integration |
| System.Net.WebSockets.Client | 4.3.2 | WebSocket client |
| System.Net.Http.Json | 10.0.5 | JSON HttpClient helpers |
| SSH.NET | 2024.2.0 | SSH/SFTP integration support |

### Storage & Data Formats

| Package | Version | Purpose |
|---------|---------|---------|
| System.Text.Json | 10.0.5 | JSON serialization |
| Newtonsoft.Json | 13.0.4 | JSON compatibility |
| System.IO.Compression | 4.3.0 | Gzip compression |
| Apache.Arrow | 22.1.0 | Columnar data structures |
| Npgsql | 10.0.2 | PostgreSQL access |
| Parquet.Net | 5.5.0 | Parquet storage |
| K4os.Compression.LZ4.Streams | 1.3.8 | LZ4 compression support |
| ZstdSharp.Port | 0.8.7 | Zstandard compression support |
| System.Threading.Channels | 10.0.5 | High-throughput pipelines |
| System.IO.Pipelines | 10.0.5 | High-performance I/O |

### Data & Analytics

| Package | Version | Purpose |
|---------|---------|---------|
| QuantConnect.Lean | 2.5.17414 | Lean engine integration |
| QuantConnect.Lean.Engine | 2.5.17414 | Lean engine runtime |
| QuantConnect.Common | 2.5.17414 | Lean shared types |
| QuantConnect.Indicators | 2.5.17414 | Technical indicators |
| Skender.Stock.Indicators | 2.7.1 | Technical analysis helpers |
| System.Reactive | 6.1.0 | Reactive extensions |

### Desktop, scripting, and interoperability

| Package | Version | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm | 8.2.2 | MVVM helpers |
| MaterialDesignThemes | 5.2.0 | WPF styling/components |
| MaterialDesignColors | 5.2.0 | WPF color resources |
| Microsoft.CodeAnalysis.CSharp.Scripting | 5.0.0 | QuantScript C# scripting |
| AvalonEdit | 6.3.1.120 | Text editor surface |
| ScottPlot.WPF | 5.0.55 | WPF charting |
| ModelContextProtocol | 1.2.0 | MCP protocol support |
| ModelContextProtocol.AspNetCore | 1.2.0 | ASP.NET Core MCP host support |
| Sharpino | 4.9.1 | F# event-sourcing support |
| Websocket.Client | 5.3.0 | High-performance WebSocket client |
| FluentValidation | 12.1.1 | Validation for configuration/options |

### Interactive Brokers (Optional)

The IB API is not bundled via NuGet in this repo. To enable IB support:
- Install the IB API package/dll manually
- Build with the `IBAPI` compilation constant

---

## Tests

Testing dependencies are also centrally pinned in `Directory.Packages.props`.

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.NET.Test.Sdk | 18.3.0 | Test host/runtime |
| Microsoft.AspNetCore.Mvc.Testing | 9.0.12 | ASP.NET Core integration testing |
| xunit | 2.9.3 | Test framework |
| xunit.runner.visualstudio | 3.1.5 | Visual Studio test runner integration |
| FluentAssertions | 8.9.0 | Assertion library |
| Moq | 4.20.72 | Mocking |
| NSubstitute | 5.3.0 | Mocking/substitution |
| coverlet.collector | 8.0.1 | Coverage collection |
| FsUnit.xUnit | 7.1.1 | F# assertion helpers |
| TngTech.ArchUnitNET | 0.13.3 | Architecture tests |
| TngTech.ArchUnitNET.xUnit | 0.13.3 | xUnit adapter for architecture tests |
