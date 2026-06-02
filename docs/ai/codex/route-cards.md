# Codex Route Cards

Use these cards after the generated repo-navigation map identifies a subsystem. They are a compact
Codex-facing view of owner projects, first docs, likely entrypoints, and validation lanes.

The canonical source remains `../generated/repo-navigation.json`; refresh generated navigation
instead of editing generated artifacts when routes, projects, or authoritative docs change.

## Host And Composition

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian`, `src/Meridian.Application`, `src/Meridian.Contracts`, `src/Meridian.Core` |
| First docs | `docs/architecture/module-map.md`, `docs/ai/generated/repo-navigation.md`, `docs/ai/ai-known-errors.md` |
| Entrypoints | `src/Meridian/Program.cs`, `src/Meridian.Application/Composition`, `src/Meridian.Application/Pipeline`, `src/Meridian.Contracts` |
| Validation | Focused `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "<contract or endpoint filter>"`; broaden only when composition or shared contracts change broadly |

## Providers And Storage

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Infrastructure`, `src/Meridian.ProviderSdk`, `src/Meridian.Storage` |
| First docs | `docs/ai/claude/CLAUDE.providers.md`, `docs/ai/claude/CLAUDE.storage.md`, provider or storage source README |
| Entrypoints | `src/Meridian.ProviderSdk/IMarketDataClient.cs`, `src/Meridian.Infrastructure/Adapters`, `src/Meridian.Storage/Archival`, `src/Meridian.Storage/Interfaces` |
| Validation | Focused provider/storage tests, provider smoke scripts, or the narrow provider-validation lane that covers the adapter |

## Desktop And UI Workflows

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, `src/Meridian.Wpf`, `src/Meridian.Ui/dashboard` |
| First docs | `.codex/AGENTS.md`, `docs/engineering/README.md`, `docs/operators/README.md`, `docs/ai/navigation/README.md` |
| Entrypoints | `src/Meridian.Ui.Shared/Endpoints`, `src/Meridian.Ui/dashboard/src`, `src/Meridian.Wpf/Shell`, `src/Meridian.Wpf/ViewModels` |
| Validation | Browser: targeted Vitest or `npm --prefix src/Meridian.Ui/dashboard run test`; WPF: focused `tests/Meridian.Wpf.Tests` filter; shared DTOs: endpoint tests plus UI consumer tests |

## Backtesting And Strategy Analytics

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript` |
| First docs | `docs/ai/generated/repo-navigation.md`, nearest source README, `docs/ai/ai-known-errors.md` |
| Entrypoints | `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript` |
| Validation | Focused backtesting, replay, or QuantScript test project filters tied to the changed contract |

## Execution, Risk, And Strategies

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Execution`, `src/Meridian.Execution.Sdk`, `src/Meridian.Risk`, `src/Meridian.Strategies` |
| First docs | `docs/architecture/module-map.md`, nearest source README, trading readiness or execution status docs when operator behavior changes |
| Entrypoints | `src/Meridian.Execution/Interfaces/IOrderGateway.cs`, `src/Meridian.Risk/IRiskRule.cs`, `src/Meridian.Strategies/Interfaces` |
| Validation | Focused execution/risk/strategy tests; include replay or workstation readiness checks when user-facing trading readiness changes |

## Domain, Ledger, And FSharp

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Domain`, `src/Meridian.Ledger`, `src/Meridian.FSharp`, `src/Meridian.FSharp.Ledger`, `src/Meridian.FSharp.Trading` |
| First docs | `docs/ai/claude/CLAUDE.fsharp.md`, `docs/architecture/module-map.md`, nearest source README |
| Entrypoints | `src/Meridian.Domain/Collectors`, ledger services, F# aggregate projects |
| Validation | Focused C# and F# boundary tests; keep interop expectations explicit before changing thresholds or labels |

## MCP Integration

| Field | Start here |
| --- | --- |
| Projects | `src/Meridian.Mcp` |
| First docs | `docs/ai/navigation/README.md`, `docs/ai/README.md`, generated repo-navigation artifacts |
| Entrypoints | `src/Meridian.Mcp/Program.cs`, MCP tools/resources/prompts, retained MCP server tests |
| Validation | Focused MCP host/tool tests plus navigation freshness when repo-navigation payloads change |

## AI Docs And Codex Tooling

| Field | Start here |
| --- | --- |
| Projects | `docs/ai`, `.codex`, `tools/codex`, `build/scripts/docs`, `scripts/ai` |
| First docs | `../assistant-workflow-contract.md`, `../documentation-ownership.md`, `README.md`, this quickstart set, `.codex/skills/README.md` |
| Entrypoints | `build/scripts/docs/check-ai-inventory.py`, `build/scripts/docs/check-codex-skills.py`, `build/scripts/docs/check-ai-contract-drift.py`, `.codex/skills/*/SKILL.md`, `.codex/agents/*.toml` |
| Validation | `python build/scripts/docs/check-codex-skills.py --summary`; `python build/scripts/docs/check-ai-inventory.py --summary`; `python build/scripts/docs/validate-skill-packages.py`; `git diff --check -- <paths>` |

Additional AI contract notes for this lane:

- Generated artifacts: do not hand-edit `docs/ai/generated/*` and `docs/generated/*`; rerun generators when schema or routing truth changes.
- Lane policy: use `../parallel-task-manifest-template.md` and `../agent-handoff-checklist.md` when Codex tasks overlap across AI/agent domains.
- Context budget: set `../work-modes.md` before expanding beyond local registry/doc-file edits.
