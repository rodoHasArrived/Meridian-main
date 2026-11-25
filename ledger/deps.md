# Dependency upgrades ledger (scheduled "deps" task)

## Batches

| Date | PR | Packages and versions | PR state |
|---|---|---|---|
| 2026-11-25 | [#3745](https://github.com/rodoHasArrived/Meridian-main/pull/3745) (`claude/deps-20261125`) | NuGet: FSharp.Core 11.0.101, Microsoft.CodeAnalysis.CSharp.Scripting 5.12.0, Microsoft.Extensions.TimeProvider.Testing 10.11.1, Microsoft.NET.Test.Sdk 18.12.0, coverlet.collector 10.1.1, ModelContextProtocol(+AspNetCore) 1.4.2, Npgsql 10.0.4, OTel Exporter.Console 1.15.3 / Instrumentation.AspNetCore 1.15.2 / Instrumentation.Http 1.15.1, Polly(+Extensions) 8.9.1, QuestPDF 2025.7.4, Skender.Stock.Indicators 2.7.3, Swashbuckle.AspNetCore 10.3.1, System.Security.Cryptography.ProtectedData 9.0.23, Testcontainers.PostgreSql 4.17.0, TngTech.ArchUnitNET(+xUnit) 0.13.5, Websocket.Client 5.5.1, ZstdSharp.Port 0.8.8. npm: @types/node 26.6.10, @types/react 19.3.2, lucide-react 1.80.0, react-router-dom 7.18.7, typescript-eslint ^8.74.0 | draft, open; local build + 26 .NET CI test projects + dashboard build/lint/test green |

## Deferred or failing

| Package | Version | Reason / error summary | Date | Status |
|---|---|---|---|---|
| Microsoft.Extensions.* / Microsoft.AspNetCore.{Mvc.Testing,TestHost,OpenApi} | 10.0.15 | Exact pins guarded by tests/scripts/test_central_package_versions.py; Dependabot #3722/#3622 cover them. Not a failure. | 2026-11-25 | held (pinned) |
| System.Text.Json, System.IO.Pipelines, QuantConnect Lean, Snappier | — | Guarded pins | 2026-11-25 | held (pinned) |
| Moq | 4.21.0 | Documented hold: Castle.Core 5.2.1 breaks NSubstitute DIMs (castleproject/Core#684); fix only on unreleased Castle.Core 6.0 | 2026-11-25 | held until Castle.Core 6 ships |
| OpenTelemetry core family | 1.21.0 | Six minors; Prometheus.AspNetCore 1.14.0-beta.1 must move to 1.21.0-beta.1 together; 1.21 drops net8/net9 | 2026-11-25 | deferred (needs reviewed move) |
| Parquet.Net | 5.6.1 | Storage/durability path; wants Parquet round-trip evidence | 2026-11-25 | deferred (not failing) |
| QuestPDF | 2025.12.4 | Calendar versioning; contents unconfirmed | 2026-11-25 | deferred |
| ZstdSharp.Port | 0.9.1 | 0.x minor (zstd 1.6.0 port) | 2026-11-25 | deferred |
| global.json SDK | 10.0.100 | No patch bump attempted; only 10.0.100 installed in runner | 2026-11-25 | unchanged |

No package has failed build or tests so far.

## Major upgrades available

| Package | Current | Latest major | Breaking changes | Affected areas | Effort | Last checked |
|---|---|---|---|---|---|---|
| .NET 11 family (M.E.*, ASP.NET Core) | 10.0.x | 11.0.0 | ASP.NET 11 needs net11/SDK 11; Microsoft.OpenApi 3.x; IHost.RunAsync throws on BackgroundService failure; empty config strings override; restore no longer uses higher global-folder versions | whole solution (TFM) | High | 2026-11-25 |
| Npgsql | 10.0.4 | 11.0.0 | needs M.E.* >=11; DI package merged; multiplexing + large-object APIs removed; pool metric labels | storage, ledger, Postgres | Medium (blocked on M.E. 11) | 2026-11-25 |
| Serilog.Extensions.Logging / Settings.Configuration | 10.x | 11.0.0 | dependency bump to M.E. 11 only | logging | Low (blocked on M.E. 11) | 2026-11-25 |
| ModelContextProtocol | 1.4.2 | 2.4.0 | MCP 2026-07-28 spec; stateless HTTP default; Roots/Sampling/Logging deprecated (MCP9005); Tasks split out; tool w/o inputSchema throws; ServerInfo nullable (2.3) | MCP server | Medium | 2026-11-25 |
| Parquet.Net | 5.5.0 | 6.1.0 | untyped DataColumn/column APIs removed; typed ReadOnlyMemory writers; async-dispose only (3rd-party, unconfirmed) | storage/archival | Medium-High | 2026-11-25 |
| Sharpino | 4.9.4 | 7.0.5 | async refreshable cache; optimistic-lock stored procs; fail-fast Evolve + correlation_id DB patch; 7.0.5 needs Npgsql 11 on net11 | F# event sourcing, PG schema | High | 2026-11-25 |
| System.Reactive | 6.1.0 | 7.0.0 | WPF/WinForms APIs off compile surface (add System.Reactive.Wpf); drops net6/7 | WPF, reactive streams | Low-Medium | 2026-11-25 |
| NSubstitute | 5.3.0 | 6.2.0 | net8/netstandard2.0 only; obsolete APIs removed | tests | Low-Medium | 2026-11-25 |
| FsUnit.xUnit | 7.1.1 | 8.0.0 | drops net6, MTP v2; no API breaks listed (Dependabot #3418) | F# tests | Low | 2026-11-25 |
| xunit.runner.visualstudio | 3.1.5 | 4.0.1 | for xUnit v3 4.x; compat with 3.x unconfirmed | test discovery | Low-Medium | 2026-11-25 |
| QuestPDF | 2025.7.4 | 2026.9.2 | license v3.0; TranslateX->OffsetX; system fonts off, missing glyph throws; XPS removed; native asset rename | Meridian.Documents | Medium | 2026-11-25 |
| npm typescript | 5.9.3 | 7.0.2 | 6.0 deprecations become errors; strict default; no programmatic API until 7.1 (typescript-eslint alias) | dashboard build/lint | Medium-High (via 6.0) | 2026-11-25 |
| npm jsdom | 30.1.3 | 31.0.1 | new CSS engine; parsing pauses at blocking scripts (Dependabot #3721) | dashboard tests | Low-Medium | 2026-11-25 |
| npm @testing-library/jest-dom | 7.0.1 | 8.0.0 | drops vitest <5 (dashboard on 5.1.1) | dashboard tests | Low | 2026-11-25 |

## Notes
- Regenerate the dashboard lockfile with npm 11 (`npx -y npm@11.21.3 install`); runner npm 10 strips `libc` metadata.
- No advisories from `dotnet list package --vulnerable --include-transitive` or `npm audit` on 2026-11-25. ledger/security.md did not exist.

## Run log
- 2026-11-25: first recorded run. Opened draft PR #3745 (22 NuGet entries, 5 npm). Validation green; nothing reverted.
