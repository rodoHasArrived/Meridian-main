# Dependencies

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use this page to find a dependency's purpose and its authoritative manifest. Version pins belong in
the manifests, not in a second hand-maintained package table. A central pin alone does not mean a
package is referenced by every project or included in a default workstation build.

## Authoritative manifests

| Concern | Source of truth |
| --- | --- |
| .NET SDK selection and roll-forward | [`global.json`](../global.json) |
| NuGet versions and shared Lean version property | [`Directory.Packages.props`](../Directory.Packages.props) |
| Shared MSBuild configuration | [`Directory.Build.props`](../Directory.Build.props) |
| Direct dependencies, framework references, optional conditions | The consuming `.csproj`/`.fsproj`; find its owner through the [source registry](source/README.md) |
| Browser workstation dependencies/scripts | [`package.json`](../src/Meridian.Ui/dashboard/package.json) and resolved [`package-lock.json`](../src/Meridian.Ui/dashboard/package-lock.json) |
| Repository Node tooling | Root [`package.json`](../package.json) and [`package-lock.json`](../package-lock.json) |
| Documentation Python dependencies | [`build/scripts/docs/requirements.txt`](../build/scripts/docs/requirements.txt); specialist tools retain their own requirements files |

For NuGet changes, use the [Central Package Management Guide](development/central-package-management.md).
Project `PackageReference` entries normally omit versions; retain conditional references and reviewed
compatibility/security pins when updating the central manifest.

## Framework and language selection

- Most runtime projects target `net10.0`; use `global.json` for SDK selection rather than installing
  an arbitrary SDK version from an old setup guide.
- .NET 10 normally defaults to C# 14. Some projects explicitly retain `<LangVersion>13</LangVersion>`,
  including Execution, Execution.Sdk, Risk, and Strategies. Inspect the consuming project before
  assuming a repository-wide language version.
- F# compiler/language behavior comes from the selected SDK and any project overrides. The centrally
  pinned `FSharp.Core` package is a runtime library version, not a declaration that the solution uses
  F# 8 (or any other single language-version number).
- The WPF application targets `net10.0-windows10.0.19041.0` on Windows and an empty `net10.0` stub on
  other hosts. See [desktop testing](development/desktop-testing-guide.md) for platform requirements.

## Platform And Runtime

### Configuration & Hosting

| Package | Purpose |
| --- | --- |
| Microsoft.Extensions.Configuration | Configuration abstractions |
| Microsoft.Extensions.Configuration.Json | JSON config provider |
| Microsoft.Extensions.Configuration.Binder | Bind config to options |
| Microsoft.Extensions.Configuration.EnvironmentVariables | Env var config provider |
| Microsoft.Extensions.Configuration.CommandLine | CLI config provider |
| Microsoft.Extensions.DependencyInjection | DI container |
| Microsoft.Extensions.DependencyInjection.Abstractions | DI abstractions |
| Microsoft.Extensions.Hosting | Generic host support |
| Microsoft.AspNetCore.App | Web dashboard + HTTP endpoints |
| Microsoft.AspNetCore.OpenApi | OpenAPI support |
| Swashbuckle.AspNetCore | Swagger generation |
| System.CommandLine | CLI parsing support |

### Logging & Observability

| Package | Purpose |
| --- | --- |
| Serilog | Structured logging |
| Serilog.AspNetCore | ASP.NET Core integration |
| Serilog.Sinks.Console | Console sink |
| Serilog.Sinks.File | File sink |
| Serilog.Extensions.Logging | Microsoft.Extensions.Logging bridge |
| Serilog.Settings.Configuration | Config-based logging |
| prometheus-net | Metrics collector |
| prometheus-net.AspNetCore | ASP.NET Core metrics |
| OpenTelemetry | Tracing API |
| OpenTelemetry.Api | Tracing abstractions |
| OpenTelemetry.Extensions.Hosting | Host integration |
| OpenTelemetry.Instrumentation.AspNetCore | ASP.NET Core instrumentation |
| OpenTelemetry.Instrumentation.Http | HttpClient instrumentation |
| OpenTelemetry.Exporter.Console | Console exporter |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | OTLP exporter |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | Prometheus exporter |

### Resilience & Networking

| Package | Purpose |
| --- | --- |
| Polly | Resilience policies |
| Polly.Extensions | Extensions for Polly |
| Microsoft.Extensions.Http | HttpClientFactory |
| Microsoft.Extensions.Http.Polly | Polly integration |
| System.Net.WebSockets.Client | WebSocket client |
| System.Net.Http.Json | JSON HttpClient helpers |
| SSH.NET | SSH/SFTP integration support |

### Storage & Data Formats

| Package | Purpose |
| --- | --- |
| System.Text.Json | JSON serialization |
| Newtonsoft.Json | JSON compatibility |
| System.IO.Compression | Gzip compression |
| Apache.Arrow | Columnar data structures |
| Npgsql | PostgreSQL access |
| Parquet.Net | Parquet storage |
| K4os.Compression.LZ4.Streams | LZ4 compression support |
| ZstdSharp.Port | Zstandard compression support |
| System.Threading.Channels | High-throughput pipelines |
| System.IO.Pipelines | High-performance I/O |

### Data & Analytics

| Package | Purpose |
| --- | --- |
| QuantConnect.Lean | Lean engine integration |
| QuantConnect.Lean.Engine | Lean engine runtime |
| QuantConnect.Common | Lean shared types |
| QuantConnect.Indicators | Technical indicators |
| Skender.Stock.Indicators | Technical analysis helpers |
| System.Reactive | Reactive extensions |

### Desktop, scripting, and interoperability

| Package | Purpose |
| --- | --- |
| CommunityToolkit.Mvvm | MVVM helpers |
| MaterialDesignThemes | WPF styling/components |
| MaterialDesignColors | WPF color resources |
| Microsoft.CodeAnalysis.CSharp.Scripting | QuantScript C# scripting |
| AvalonEdit | Text editor surface |
| ScottPlot.WPF | WPF charting |
| ModelContextProtocol | MCP protocol support |
| ModelContextProtocol.AspNetCore | ASP.NET Core MCP host support |
| Sharpino | F# event-sourcing support |
| Websocket.Client | High-performance WebSocket client |
| FluentValidation | Validation for configuration/options |

### Optional integrations

**Interactive Brokers:** the official API SDK is supplied locally, not restored as a NuGet package.
[`Meridian.Infrastructure.csproj`](../src/Meridian.Infrastructure/Meridian.Infrastructure.csproj)
defines separate build modes:

| Mode | Build setting | Meaning |
| --- | --- | --- |
| Standard | Defaults | No official SDK/native connection proof |
| Smoke | `EnableIbApiSmoke=true` | Compiles against the repository's smoke stub; does not prove TWS/Gateway connectivity |
| Vendor | `EnableIbApiVendor=true` plus a resolvable official SDK | Compiles against an official `CSharpAPI.csproj` or `CSharpAPI.dll` |

Use `IBApiProjectPath` or `IBApiDllPath` for an explicit SDK input, or the documented layout under
`IBApiRoot` (default `external/IBApi`). Vendor builds fail if the SDK cannot be resolved, and smoke
and vendor modes cannot be combined. The legacy `IBAPI` compilation constant still activates vendor
resolution, but the named properties are the documented entrypoint. Follow
[Interactive Brokers onboarding](operators/provider-onboarding-interactive-brokers.md) for commands,
SDK placement, paper connectivity, and operational validation.

**Lean:** [`Meridian.csproj`](../src/Meridian/Meridian.csproj) defaults `EnableLeanIntegration` to
`false`; normal builds exclude the Lean sources and its four package references. Opt in with
`-p:EnableLeanIntegration=true` for both restore/build as documented in the
[Lean integration guide](integrations/lean-integration.md). All four pins share
`QuantConnectLeanVersion` in the central package manifest. Compiling the optional source is not
proof of a configured Lean runtime or a release-ready deployment.

**SFTP:** Infrastructure includes the `SSH.NET` reference and `SFTP` compilation symbol only when
`EnableSftp=true`. Inspect that project condition when evaluating the build's dependency surface.

---

## Tests

Testing versions are centrally pinned in [Directory.Packages.props](../Directory.Packages.props);
the owning test project determines which packages and runner it uses.

| Package | Purpose |
| --- | --- |
| Microsoft.NET.Test.Sdk | Test host/runtime |
| Microsoft.AspNetCore.Mvc.Testing | ASP.NET Core integration testing |
| xunit | Test framework |
| xunit.runner.visualstudio | Visual Studio test runner integration |
| FluentAssertions | Assertion library |
| Moq | Mocking |
| NSubstitute | Mocking/substitution |
| coverlet.collector | Coverage collection |
| FsUnit.xUnit | F# assertion helpers |
| TngTech.ArchUnitNET | Architecture tests |
| TngTech.ArchUnitNET.xUnit | xUnit adapter for architecture tests |

## Browser and documentation tooling

The dashboard manifest owns React, routing, icons, TypeScript, Vite, Vitest, Playwright, and the
accessibility/lint tooling. Its lockfile records the resolved graph; use `npm ci` in that package
for a lockfile-based install. The root Node package separately owns asset/diagram tooling and
pass-through dashboard scripts. Do not update one package's lockfile to represent changes in the other.

Python documentation tools use their checked-in requirements files, including PyYAML for standard
YAML parsing. See [Engineering](engineering/README.md#buildtestrun) and
[documentation contribution](development/documentation-contribution-guide.md) for the validation
lane matching the manifest you change. Use the published artifact's SBOM for its resolved release
dependency inventory; this purpose map is not an SBOM or a vulnerability scan.
