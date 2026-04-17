# QuantScript Environment Blueprint

**Owner:** Desktop / Research Engineering
**Audience:** Research, desktop, and backtesting contributors
**Last Updated:** 2026-04-16
**Status:** Implemented baseline with optional post-core follow-ons

---

## Summary

QuantScript is Meridian's interactive C# research scripting environment inside the desktop workstation. The core project, WPF page, compiler and runner path, tests, sample-script baseline, and notebook-style cell execution workflow are now implemented. This document now serves as the high-level product-placement and architecture-intent reference, while [`quant-script-page-implementation-guide.md`](quant-script-page-implementation-guide.md) carries the detailed screen, service, and implementation guidance.

QuantScript is not part of the default Wave 1-6 core operator-readiness path. Treat it as an optional research-product track unless a specific roadmap decision pulls it forward.

---

## Implemented Baseline

The current repository already includes:

- `src/Meridian.QuantScript/` with the scripting engine, data access surface, statistics vocabulary, portfolio helpers, and plotting infrastructure
- `src/Meridian.Wpf/Views/QuantScriptPage.xaml` and `src/Meridian.Wpf/ViewModels/QuantScriptViewModel.cs` for the desktop scripting surface, including notebook document browsing, cell selection, and checkpointed replay
- `src/Meridian.QuantScript/Documents/` and `src/Meridian.QuantScript/Compilation/NotebookExecutionSession.cs` for notebook persistence and stale-cell replay control
- `tests/Meridian.QuantScript.Tests/` covering compiler, runner, checkpoint continuation, notebook session state, statistics, plot queue, and portfolio-builder behavior
- sample-script and research-surface integration that make QuantScript a real workstation capability rather than a blueprint-only concept

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
- keep outputs aligned with shared backtest and run contracts where that materially helps workflow continuity
- route detailed UI, ViewModel, and interaction design to the page implementation guide instead of duplicating it here

---

## Remaining Optional Work

- deeper workflow integration with shared run, export, and observability seams
- broader sample-script and research-template library
- tighter integration with optional L3 inference and multi-instance research follow-ons where that adds real operator value

These are optional research-depth improvements, not blockers for the core operator-ready workstation path.

---

## Related Documents

- [QuantScript Page Implementation Guide](quant-script-page-implementation-guide.md)
- [L3 Inference Implementation Plan](l3-inference-implementation-plan.md)
- [QuantScript L3 Multi-instance Round 2 Roadmap](quantscript-l3-multiinstance-round2-roadmap.md)
- [ROADMAP.md](../status/ROADMAP.md)
