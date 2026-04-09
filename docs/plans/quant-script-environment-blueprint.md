# QuantScript Environment Blueprint

**Owner:** Desktop / Research Engineering
**Audience:** Research, desktop, and backtesting contributors
**Last Updated:** 2026-04-08
**Status:** Delivered baseline with optional post-core follow-ons

---

## Summary

QuantScript is Meridian's interactive C# research scripting environment inside the desktop workstation. The core page, compiler/runner path, tests, and sample-script baseline are already implemented; this document now serves as the high-level product-placement and architecture-intent reference, while [`quant-script-page-implementation-guide.md`](quant-script-page-implementation-guide.md) carries the detailed screen, service, and implementation guidance.

QuantScript is not part of the default Wave 1-6 core operator-readiness path. Treat it as an optional research-product track unless a specific roadmap decision pulls it forward.

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
- script browser, parameter inputs, console output, charts, metrics, and diagnostics
- access to existing Meridian data, analytics, and backtesting services through explicit runner abstractions
- persisted local scripts and repeatable research workflows

### Out of scope

- treating Python or R notebooks as a required Meridian platform dependency
- replacing workstation research, promotion, or governance flows with a standalone scripting product
- elevating QuantScript ahead of the Wave 1-6 operator-readiness path by default

---

## Architecture Direction

- keep QuantScript as a workstation-integrated research tool, not a second application stack
- keep execution isolated behind compiler/runner services and explicit capability injection
- keep outputs aligned with shared backtest and run contracts where that materially helps workflow continuity
- route detailed UI, ViewModel, and interaction design to the page implementation guide instead of duplicating it here

---

## Dependencies And Placement

- QuantScript depends on the platform foundations Meridian already has in data collection, charting, diagnostics, and backtesting
- further QuantScript expansion should normally follow Waves 1-4 and sit alongside other optional research and scale tracks
- if future work deepens this surface, prefer integration with shared run, export, and observability seams over bespoke storage or orchestration paths

---

## Related Documents

- [QuantScript Page Implementation Guide](quant-script-page-implementation-guide.md)
- [L3 Inference Implementation Plan](l3-inference-implementation-plan.md)
- [QuantScript L3 Multi-instance Round 2 Roadmap](quantscript-l3-multiinstance-round2-roadmap.md)
- [ROADMAP.md](../status/ROADMAP.md)
