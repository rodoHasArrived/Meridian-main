# Project Structure

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Meridian lives at `C:\Dev\Meridian-main`. The repository is organized around
source projects, tests, automation, docs, design-system assets, and generated
artifacts.

## Active Source

| Area | Classification | Purpose |
| --- | --- | --- |
| `src/Meridian/` | Active source code | Main CLI and desktop-local API host |
| `src/Meridian.Application/` | Active source code | Application workflows, commands, orchestration, configuration |
| `src/Meridian.Contracts/` | Active source code | Shared DTOs and contracts |
| `src/Meridian.Core/` | Active source code | Core config, exceptions, logging, serialization |
| `src/Meridian.Domain/` | Active source code | Domain events, collectors, market abstractions |
| `src/Meridian.Infrastructure*/` | Active source code | Provider and infrastructure adapters |
| `src/Meridian.Storage/` | Active source code | WAL, sinks, archival, packaging, lineage |
| `src/Meridian.ProviderSdk/` | Active source code | Provider-facing interfaces |
| `src/Meridian.Execution*/` | Active source code | Execution and broker gateway seams |
| `src/Meridian.Risk/` | Active source code | Pre-trade risk rules |
| `src/Meridian.Strategies/` | Active source code | Strategy lifecycle and run read models |
| `src/Meridian.Backtesting*/` | Active source code | Replay, backtesting, and strategy SDK support |
| `src/Meridian.Ledger/`, `src/Meridian.FSharp*/` | Active source code | Ledger, accounting, and F# domain support |
| `src/Meridian.QuantScript/` | Active source code | QuantScript parsing, compilation, and charting support |
| `src/Meridian.Mcp*/` | Active source code | MCP hosts and tool surfaces |
| `src/Meridian.Ui/dashboard/` | Active source code | Browser-first operator workstation |
| `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` | Active source code | Shared UI services, endpoints, and read models |
| `src/Meridian.Wpf/` | Retained active source code | Compatibility and regression support for WPF |

## Tests And Validation

| Area | Classification | Purpose |
| --- | --- | --- |
| `tests/` | Active test code | Unit, integration, UI-service, MCP, F#, and WPF tests |
| `benchmarks/` | Active performance code | BenchmarkDotNet suites |
| `.github/workflows/` | Active CI/CD | Build, test, docs, security, release, screenshot, and maintenance workflows |

## Automation, Scripts, And Configuration

| Area | Classification | Purpose |
| --- | --- | --- |
| `build/scripts/` | Active script | Build, docs, install, publish, hooks, and validation automation |
| `scripts/dev/` | Active script | Desktop/web workflow capture, local runners, and smoke probes |
| `make/`, `Makefile` | Active script | Make targets for environments where `make` is installed |
| `config/` | Active configuration | Sample/schema/local configuration entry points |
| `Directory.*.props`, `global.json`, `NuGet.Config` | Active configuration | SDK, build, and package management |

## Docs, Design, Archive, Generated Output

| Area | Classification | Purpose |
| --- | --- | --- |
| `docs/` | Active documentation | Maintained architecture, development, operations, status, plans, and references |
| `docs/prompts/` | Active documentation | Maintained prompt, agent, and automation guidance indexes |
| `docs/archive/` | Historical reference only | Superseded docs-tree material retained for traceability |
| `Meridian Design System/` | Active design-system assets | Local source of truth for visual foundations, previews, governance tests, and icons |
| `archive/` | Historical reference only | Legacy superseded docs/code kept for traceability |
| `docs/generated/` | Generated documentation | Regenerate by script; do not hand edit generated sections |
| `docs/docfx/api/` | Generated documentation | DocFX API metadata output; regenerate before publishing docs |
| `wwwroot/workstation/` | Generated/build output currently tracked | Host-served web workstation bundle; review before replacing or deleting |
| `src/Meridian.Ui/wwwroot/workstation/` | Generated/build output | Vite build output for the browser workstation |
| `.artifacts/`, `artifacts/`, `bin/`, `obj/`, `TestResults/`, `coverage/`, `node_modules/`, `src/Meridian.Ui/dashboard/artifacts/` | Generated/build output | Local outputs that should not be committed |

## Cleanup Classification Rules

- Safe to delete: ignored `bin/`, `obj/`, `node_modules/`, `TestResults/`,
  `coverage/`, local logs, local publish output, dashboard automation artifacts,
  `.artifacts/`, and local smoke artifacts.
- Safe to consolidate: duplicate generated indexes, stale one-off reports, and
  documentation that repeats command lists already covered by `docs/HELP.md`.
- Needs review: tracked generated API metadata, tracked workstation bundles,
  archived code, and agent/skill prompt copies.
- Should retain: active source, tests, CI workflows, canonical docs, design-system
  assets, and archive material with traceability value.
