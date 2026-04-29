# QuantScript Environment Blueprint

**Owner:** Desktop / Research Engineering
**Audience:** Research, desktop, and backtesting contributors
**Last Updated:** 2026-04-21
**Status:** Implemented baseline plus combined follow-on wave delivered

---

## Summary

QuantScript is Meridian's interactive C# research scripting environment inside the desktop workstation. The core project, WPF page, compiler and runner path, tests, sample-script baseline, and notebook-style cell execution workflow are now implemented. The 2026-04-21 combined follow-on wave also landed async-first script APIs, dual-mode parameter registration, local execution history, file-backed templates, and shared Research run-browser integration for exact single-backtest executions. This document now serves as the high-level product-placement and architecture-intent reference, while [`quant-script-page-implementation-guide.md`](quant-script-page-implementation-guide.md) carries the detailed screen, service, and implementation guidance.

QuantScript is not part of the default Wave 1-6 core operator-readiness path. Treat it as an optional research-product track unless a specific roadmap decision pulls it forward.

---

## 2026-04-21 Combined Follow-on Wave

The current implementation now includes the follow-on work that connects QuantScript execution more cleanly to the wider Research workspace:

- **Async-first script APIs.**
  `DataProxy` now exposes `PricesAsync`, `TradesAsync`, `OrderBookAsync`, `SecMasterAsync`, and `CorporateActionsAsync`, while `BacktestProxy` exposes `RunAsync()` and forwards the active script cancellation token into the backtest engine. The synchronous calls remain as compatibility wrappers only.
- **Dual-mode parameter registration.**
  Static sidebar discovery now checks sources in this order: literal `Param<T>(...)` calls, `[ScriptParam]` declarations, then legacy `// @param` comments. After the first execution, runtime `Param<T>()` registrations become authoritative and refresh the sidebar metadata without discarding user-entered overrides.
- **Local execution history.**
  Every script and notebook execution is persisted under `<resolved DataRoot>/_quantscript/runs` as JSON written with source-generated serialization and atomic file replacement. Records include document metadata, parameter snapshots, success/failure, console preview, metrics, plot titles, captured backtest count, and any mirrored Research run id.
- **Shared Research run integration for exact single-backtest executions.**
  When a QuantScript execution captures exactly one `BacktestResult`, the run is mirrored into the existing `StrategyRunWorkspaceService` flow using `strategyId = "quantscript-" + slug(documentTitle)` and `strategyName = documentTitle`. The shared run parameter set includes QuantScript metadata such as `documentPath`, `documentKind`, `executionId`, plus the resolved sidebar parameters. Executions with zero backtests or more than one backtest remain local-only; multi-backtest runs emit a warning that shared mirroring was skipped.
- **Research-surface reuse instead of duplicate comparison UI.**
  QuantScript now reuses the existing Strategy Runs browser, run-detail surface, and compare flow rather than introducing a second comparison system inside the scripting page.
- **File-backed templates and in-page run history.**
  The desktop page now exposes a `Templates` toolbar action, a `TEMPLATES` section in the Local Data tab, and a `Run History` tab for browsing local execution records and jumping into shared Research views when a mirrored run exists.

---

## Implemented Baseline

The current repository already includes:

- `src/Meridian.QuantScript/` with the scripting engine, data access surface, statistics vocabulary, portfolio helpers, and plotting infrastructure
- `src/Meridian.Wpf/Views/QuantScriptPage.xaml` and `src/Meridian.Wpf/ViewModels/QuantScriptViewModel.cs` for the desktop scripting surface, including notebook document browsing, cell selection, and checkpointed replay
- `src/Meridian.QuantScript/Documents/` and `src/Meridian.QuantScript/Compilation/NotebookExecutionSession.cs` for notebook persistence and stale-cell replay control
- `src/Meridian.Wpf/Services/QuantScriptExecutionHistoryService.cs` and `src/Meridian.Wpf/Models/QuantScriptExecutionHistoryModels.cs` for local execution persistence under the resolved Meridian data root
- `src/Meridian.Wpf/Services/QuantScriptTemplateCatalogService.cs` plus `src/Meridian.Wpf/Templates/QuantScript/` for the built-in file-backed template catalog
- `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs` integration for mirroring exact single-backtest QuantScript executions into the shared Research run browser, run detail, and compare surfaces
- tests covering compiler, runner, checkpoint continuation, notebook session state, async APIs, runtime parameter registration, template loading, local execution history, and Strategy Runs integration
- sample-script and Research-surface integration that make QuantScript a real workstation capability rather than a blueprint-only concept

---

## Product Role

- give researchers a fast local scripting surface over Meridian-collected data
- connect scripting, charting, metrics, diagnostics, and backtest-adjacent analysis inside one desktop workflow
- reuse Meridian services and data contracts instead of creating a separate research stack
- complement the Research workspace rather than replace the shared run, promotion, or governance workflows

---

## Scope

### In scope

- interactive C# scripting in the WPF workstation
- script and notebook document browsing, cell-based execution, parameter inputs, console output, charts, metrics, and diagnostics
- access to existing Meridian data, analytics, and backtesting services through explicit runner abstractions
- persisted local scripts and repeatable research workflows

### Out of scope

- treating Python or R notebooks as a required Meridian platform dependency
- replacing workstation research, promotion, or governance flows with a standalone scripting product
- elevating QuantScript ahead of the Wave 1-6 operator-readiness path by default

---

## Architecture Direction

- keep QuantScript as a workstation-integrated research tool, not a second application stack
- keep execution isolated behind compiler and runner services plus explicit capability injection
- persist every execution locally under the resolved Meridian data root, then mirror only exact single-backtest runs into shared Strategy Runs infrastructure
- keep shared run-browser, run-detail, and comparison workflows inside the existing Research surface instead of building parallel QuantScript-only run management
- route detailed UI, ViewModel, and interaction design to the page implementation guide instead of duplicating it here

---

## Remaining Optional Work

- broader sample-script and research-template library beyond the starter catalog
- richer dataframe/alignment semantics and optimization-specific UI such as heatmaps
- parent/child lineage, multi-backtest publication rules, and deeper research observability seams where that adds real operator value
- tighter integration with optional L3 inference and multi-instance research follow-ons where that adds real operator value

These are optional research-depth improvements, not blockers for the current workstation-integrated QuantScript implementation.

---

## Related Documents

- [QuantScript Page Implementation Guide](quant-script-page-implementation-guide.md)
- [L3 Inference Implementation Plan](l3-inference-implementation-plan.md)
- [QuantScript L3 Multi-instance Round 2 Roadmap](quantscript-l3-multiinstance-round2-roadmap.md)
- [ROADMAP.md](../status/ROADMAP.md)
