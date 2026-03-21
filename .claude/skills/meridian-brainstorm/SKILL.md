---
name: meridian-brainstorm
description: >
  Brainstorming, ideation, and creative feature exploration skill for the Meridian project.
  Use this skill whenever the user wants to generate new ideas, features, or improvements for Meridian,
  or when they ask "what could we add", "how could we improve", "what would be valuable", "what features should we build",
  or any variant of creative/generative thinking about the project. Also trigger when the user describes a pain point,
  a user persona (hobbyist, academic, institutional), or a domain problem (latency, data quality, accessibility,
  backtesting, compliance) and wants ideas for solving it. Also trigger for architecture or refactoring brainstorms,
  user growth and adoption strategy discussions, or technical debt and code quality improvement ideation.
  Trigger even if the user just says "brainstorm" or "give me ideas" in the context of this project. This skill
  produces detailed idea writeups with implementation sketches, audience fit analysis, effort ratings, and concrete
  next steps.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads markdown references on demand
  and may append to a local brainstorming history ledger when the host permits filesystem writes.
metadata:
  owner: meridian-ai
  version: "1.1"
  spec: open-agent-skills-v1
---
# Meridian — Brainstorming & Ideation Skill

Generate high-value, implementable ideas for the Meridian platform. Every idea should feel like a natural extension of the program — something that makes the existing experience richer, clearer, and more capable, not a bolt-on afterthought.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md) — authoritative stats, provider list, key abstraction file paths, storage design, ADR table. Read this before generating ideas that reference specific classes, interfaces, or provider names.

---

## Integration Pattern

Every brainstorming task follows this 4-step workflow:

### 1 — GATHER CONTEXT (MCP)
- Fetch the GitHub issue, discussion, or feature request that prompted the brainstorm
- Read `../_shared/project-context.md` for authoritative project stats and abstraction paths
- Review `references/competitive-landscape.md` for competitive signals relevant to the request

### 2 — ANALYZE & PLAN (Agents)
- Detect the brainstorm mode (Open Exploration, Problem-Focused, Persona-Focused, etc.) using the mode table
- Check the `brainstorm-history.jsonl` ledger to avoid repeating previously covered themes
- Plan the idea set: quantity targets, audience personas, and effort tiers to cover

### 3 — EXECUTE (Skills + Manual)
- Emit the mode declaration and summary table before writing any ideas
- Write each idea as a natural narrative with anchor, user moment, implementation shape, and tradeoffs
- Synthesize: call out the highest-leverage idea, platform bets, and sequencing recommendation

### 4 — COMPLETE (MCP)
- Append a new entry to `brainstorm-history.jsonl` recording today's session themes
- If the session produced actionable proposals, create GitHub issues for the top 1-3 ideas
- Create a PR or discussion thread via GitHub to share the brainstorm output with stakeholders

---

## Core Philosophy: Complementary Extension

The best ideas for Meridian aren't isolated features. They're extensions that **amplify what already exists**. Before generating any idea, ask:

1. **What does Meridian already do well nearby?** Find the existing capability this idea extends, deepens, or connects to. An idea with no anchor to current functionality is probably the wrong idea.
2. **What would a user actually see and feel?** Every idea must have a concrete UI or interaction moment. If you can't describe what the user clicks, reads, or watches — the idea isn't finished yet.
3. **Does this make the whole program more coherent?** The best features make users think "of course this is here." They reduce the number of tools a user needs open, surfacing the right information at the right time within Meridian itself.
4. **Is the information presented clearly?** Data-dense applications live or die on information hierarchy. Every idea that touches the UI should consider: what's the most important thing on screen? What's secondary? What can be progressive-disclosed or hidden until needed?

---

## Project Context

**What Meridian is:**
A provider-agnostic .NET 9 / C# 13 platform for real-time and historical market data collection, backtesting, live execution, and strategy lifecycle management. Streams from Interactive Brokers TWS, Alpaca Markets, Polygon, NYSE, and (via StockSharp) 90+ additional providers. Sub-2ms event pipeline, JSONL/Parquet storage, WebSocket + REST APIs, OpenTelemetry observability, WPF desktop app + web dashboard, MCP server for AI tooling.

**Tech stack:** C# 13 (infrastructure), F# 8 (domain models), .NET 9, WPF (desktop UI), Docker, Prometheus/Grafana, OpenTelemetry, Bounded Channels, WAL storage, JSONL + Parquet (simultaneous), GitHub Actions CI/CD.

**Four-pillar architecture (ADR-016):**
- **Data Collection** — streaming ingestion, historical backfill, storage, data quality monitoring
- **Backtesting** — historical tick replay, fill simulation, portfolio metrics, Ledger-based P&L
- **Execution** — live/simulated order routing, `IOrderGateway`, `PaperTradingGateway`, broker adapters, pre-trade risk (`Meridian.Risk`)
- **Strategies** — strategy lifecycle management, run archive, paper→live promotion workflow

**Current capabilities:**

- Real-time streaming: trades, quotes, L2 order book depth from 5 providers
- Historical backfill: 11 providers with auto-fallback chain
- Multi-provider failover via `FailoverAwareMarketDataClient`
- Backtesting engine with tick-by-tick replay, fill models, and double-entry `Ledger` P&L
- Paper trading gateway (`PaperTradingGateway`) for risk-free strategy validation
- Strategy lifecycle: register, run, pause, promote paper→live
- Pre-trade risk validation via `CompositeRiskValidator` / `IRiskRule`
- WPF desktop app: MVVM with `BindableBase`, real-time status, config management
- Web dashboard: live status, data browser, API endpoints
- MCP server layer (`Meridian.Mcp` / `Meridian.McpServer`) exposing tools + prompts for AI agents
- Deployment: Docker Compose, systemd, Kubernetes
- Observability: OpenTelemetry tracing, Prometheus metrics, Grafana dashboards
- CI/CD: 33 GitHub Actions workflows for build, test, and automated documentation

**What's coming:** StockSharp 90+ provider activation, Python-accessible layer, Arrow Flight / DuckDB integration, assembly-level SIMD hot-path optimizations, full WPF UX parity (6 pages still showing placeholder data), live broker adapters beyond paper trading.

**Current count (authoritative: `../_shared/project-context.md`):** 868 source files (856 C# + 12 F#), 261 test files, 22 main projects, 33 CI/CD workflows.

---

## Audience Personas

### 🎯 Hobbyist Quant Developer

Software developer or data scientist learning quant finance. Frustrated by setup complexity and the gap between "collecting data" and "doing something with it." Wants quick wins, low cost, and integration with tools they already know (Jupyter, pandas). Low risk tolerance — prefers paper trading and sandboxed environments.

### 🎓 Academic / Researcher

Quantitative finance PhD, financial economist, or ML researcher. Cares deeply about data reproducibility, provenance, and quality validation. Institutional data is prohibitively expensive. Needs citation-ready data, audit trails, and bulk export to research infrastructure (HDF5, Arrow, DuckDB).

### 🏦 Institutional / Professional

Prop trading firm, hedge fund ops, or quant analyst at an asset manager. Pain points are vendor lock-in, latency SLAs, compliance, and disaster recovery. Values reliability, throughput, and support. Low tolerance for infrastructure failures.

---

## The User Experience Lens

**Apply this lens to every idea.** Meridian is a desktop application people monitor for hours. The quality of the experience — how information is organized, how status is communicated, how configuration feels, how errors surface — determines whether someone keeps using it or switches to a script.

**Principles for UI-touching ideas:**

- **Information hierarchy matters.** A streaming data app can easily become a wall of numbers. Every screen should have a clear primary focus, secondary details accessible on hover or drill-down, and a calm default state that doesn't demand attention when things are healthy.
- **Status at a glance.** The user should be able to look at Meridian for 2 seconds and know: is everything OK? Is anything degraded? Is a backfill running? Use color, density, and spatial position to communicate state — not just text.
- **Progressive disclosure.** Show the essential view first. Let users expand into detail when they want it. A symbol's health score is the top layer; the per-provider tick rate breakdown is the drill-down.
- **Contextual actions.** When showing data, show what the user can _do_ with it right there. Viewing a gap in historical data? Offer a one-click backfill. Seeing an anomalous tick? Offer to flag or exclude it.
- **Consistency across views.** Symbols, providers, time ranges, and status indicators should look and behave the same way everywhere in the app. Build a shared visual vocabulary.
- **Respect the WPF medium.** Ideas should feel native to a desktop application — data grids, split panes, keyboard shortcuts, system tray integration, multi-monitor awareness. Not a web app crammed into a window.

---

## How to Run a Brainstorm

### Step 0: Detect Mode (Explicit)

Before generating any ideas, output a one-line mode declaration at the top of your response:

> **Mode detected:** [Mode Name] — *[one sentence explaining why]*

Use the table below to identify the mode. If the request is ambiguous between two modes, state both and explain which one you're optimizing for.

| Mode | Trigger phrases | Approach |
| ------ | --------- | ---------- |
| **Open Exploration** | "What could we build?" / "What's valuable?" / "Give me ideas" | Generate across all dimensions, all personas. Favor ideas that connect multiple existing capabilities. |
| **Problem-Focused** | "How do we solve X?" / "What's the best way to fix Y?" | 3-5 deep ideas targeting the specific pain. Show how each integrates with existing features. |
| **Persona-Focused** | "What do hobbyist quants want?" / "Ideas for academics" / "Institutional use cases" | 5-8 ideas optimized for that audience. Describe the user journey, not just the feature. |
| **Domain-Focused** | "Ideas for latency / storage / UX / data quality" | Technical depth in that domain. Every idea still needs a UI or interaction touchpoint. |
| **Competitive** | "What are others doing?" / "How do we compare to Databento?" | Scan `references/competitive-landscape.md`. Identify gaps; only propose ideas that fit Meridian's architecture naturally. |
| **Quick Wins** | "What's easy to ship?" / "Low-hanging fruit?" | Effort ≤ Medium, impact ≥ High. Emphasize ideas that improve the existing experience, not new surface area. |
| **Architecture / Refactoring** | "How should we restructure X?" / "Refactoring ideas" / "What should we improve architecturally?" | Code structure, MVVM compliance, separation of concerns, testability. Anchor to real patterns (`BindableBase`, `EventPipeline`, `IStorageSink`). Include before/after and migration risk. |
| **User Growth / Adoption** | "How do we get more users?" / "Growth ideas" / "Onboarding friction" | Onboarding, evangelism, community. Map to personas. Span awareness → activation → retention. |
| **Technical Debt / Code Quality** | "What tech debt should we address?" / "Audit ideas" / "Code quality improvements" | Test coverage, static analysis, CI/CD hardening, dead code. Quantify the cost of inaction. |
| **UX / Information Design** | "How should we display X?" / "The UI feels cluttered" / "Dashboard design" | Focus on information architecture, visual hierarchy, interaction flow, clarity. Every idea is UI-first. |
| **Skill Improvement** | "How can the skills be improved?" / "Improve the brainstorm skill" / "Better code reviews" | Apply the brainstorm process reflexively to the skills themselves: eval coverage, output quality, reference freshness, DX. |

### Step 1: Emit the Summary Table

**Before writing any ideas**, output a summary table. This lets the user triage in 30 seconds:

```markdown
## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | [Short name] | S/M/L/XL | H=Hobbyist, Q=Academic, I=Institutional | High/Med/Low | [prereq or —] |
| 2 | ... | | | | |
```

Effort key: **S** = days, **M** = 1-2 weeks, **L** = 1+ month, **XL** = quarter+

### Step 2: Generate Ideas

Write each idea as a **natural narrative**, not a form to fill out. The reader should understand the idea, why it matters, how it works, and what it looks like — in that order.

**What every idea must include** (woven into prose, not as labeled fields):

- **The anchor:** What existing Meridian capability does this extend or complement? Reference real file paths from `../_shared/project-context.md` where helpful (e.g., "extends `IStorageSink` at `src/Meridian.Storage/Interfaces/IStorageSink.cs`").
- **The user moment:** What does the user see, click, or experience? Be specific — describe a screen, a notification, an interaction, not just a backend change.
- **The implementation shape:** Key technical approach — interfaces, patterns, data flow. Enough that a developer could start scoping.
- **The tradeoffs:** What's hard? What could go wrong? What does this cost in complexity?
- **Effort and audience:** Who benefits most? How big is this?

**What to include when relevant** (not every idea needs all of these):

- A rough UI sketch described in words (layout, what's prominent, what's secondary)
- How this feature connects to other features in the app
- Before/after comparison for architecture ideas
- Funnel stage and success metrics for growth ideas
- Debt cost and payoff timeline for tech debt ideas

**Quantity guidelines:**

- Open Exploration: 8-12 ideas across 3+ categories
- Problem/Persona/Domain focused: 4-6 deep ideas
- Quick Wins: 6-8 ideas
- Architecture/Refactoring: 4-6 ideas
- User Growth/Adoption: 5-8 ideas
- Technical Debt/Code Quality: 4-6 ideas
- UX/Information Design: 4-6 ideas

### Step 3: Synthesize

After the ideas, step back and write a synthesis that:

- Identifies the highest-leverage idea (best impact/effort ratio, most complementary to existing features)
- Calls out "platform bets" — ideas that unlock multiple others
- Flags cross-cutting themes (e.g., "three of these ideas all need a shared symbol health model")
- Suggests sequencing: what to build first, what it enables next
- **Competitive signals (always include):** 2-3 sentences from `references/competitive-landscape.md` on how competitors handle this space and which pattern is most adaptable to Meridian's architecture
- For Architecture mode: migration ordering and risk dependencies
- For Growth mode: which funnel stage to invest in first
- For Tech Debt mode: quick wins first, then structural changes
- For UX mode: which screens or views to redesign first based on user frequency and pain

---

## Tone & Output Standards

**Write ideas like you're pitching them to a product-minded developer**, not filling in a form. Each idea should read as a short, compelling argument — "here's what's painful today, here's what we'd build, here's what it looks like, here's why it's worth it."

- **Be specific, not generic.** "Add a Python SDK" is weak. "Add a `marketdata` Python package with an async iterator over the live WebSocket feed, pandas DataFrame output, and a `snap()` convenience method for the last N ticks" is strong.
- **Always describe the user experience.** Even backend optimizations have a user-facing moment: "The backfill that used to take 45 minutes now finishes in 8. The progress bar in the WPF app reflects this — the user sees symbols lighting up green in rapid succession."
- **Show how features connect.** "This data quality scorecard feeds into the existing dashboard's symbol list — the health score appears as a colored dot next to each symbol. Clicking it expands to the detailed breakdown."
- **Acknowledge tradeoffs honestly.** Hidden complexity is the enemy. Name it.
- **Anchor to the codebase.** Reference real abstractions: `IMarketDataClient`, `IHistoricalDataProvider`, `EventPipeline`, `IStorageSink`, `BindableBase`, the Options pattern. Include file paths from `../_shared/project-context.md` when it clarifies the implementation.
- **For architecture ideas:** Show concrete code-level changes, not just diagrams. Reference actual class names and namespaces.
- **For growth ideas:** Be honest about what requires sustained investment vs. one-time effort.
- **For tech debt ideas:** Quantify the cost: "This makes onboarding a new contributor take 2 days instead of 2 hours" beats "this is messy."
- **For UX ideas:** Describe the screen. What's at the top? What's the primary action? What information is visible by default vs. on drill-down? How does this view connect to other views in the app?

---

## Idea Continuity (Session History)

To avoid repeating ideas across sessions, a lightweight ledger can be maintained at `.claude/skills/meridian-brainstorm/brainstorm-history.jsonl` (gitignored by default). Each line is a JSON object:

```json
{"session_date": "2026-03-16", "mode": "Open Exploration", "themes": ["Python SDK", "data quality scorecard", "tiered storage"], "ideas_count": 10}
```

If this file exists, open each brainstorm session with:
> **Previous sessions covered:** [summary of themes from history]. **Unexplored areas:** [gaps].

This prevents the same ideas from appearing session after session and surfaces genuinely new territory.

---

## Idea Generation Reference

When brainstorming, read `references/idea-dimensions.md` for the full seeded concept bank organized by category. The file includes a **codebase anchor table** mapping concept names to file paths for precise implementation references.

**Quick-access dimensions:**

- **Data access:** streaming API, bulk export, query API, Python SDK, gRPC, Arrow Flight, DuckDB integration
- **Data quality:** gap detection, anomaly flagging, cross-provider reconciliation, tick quality scoring, options chain validation
- **Performance:** kernel bypass, lock-free queues, SIMD processing, GC tuning, backpressure metrics
- **Storage:** Parquet/Arrow, time-series DB, tiered cold storage, deduplication, schema evolution, retention policies
- **Integrations:** Jupyter, pandas, QuantConnect, Backtrader, Grafana, dbt, OpenBB, Dagster/Airflow
- **UX:** setup wizard, symbol browser, live visualizer, order book heatmap, config validation, theme system, diagnostics panel
- **Reliability:** multi-provider failover, health scoring, alerting, chaos testing, rolling restart
- **Architecture:** MVVM compliance, DI container, hot/cold path separation, interface segregation, pipeline modularization
- **Growth:** quickstart experience, content series, README optimization, integration showcases, contributor onboarding
- **Code quality:** mutation testing, static analysis gates, dead code elimination, test isolation, error handling patterns

---

## Reference Files

- `references/idea-dimensions.md` — Seeded idea bank organized by category, including codebase anchor table
- `references/competitive-landscape.md` — What Bloomberg, Databento, Polygon, Refinitiv, and open-source tools offer (read for competitive signals in every synthesis section)
- `../_shared/project-context.md` — Authoritative project stats, abstraction file paths, provider inventory
