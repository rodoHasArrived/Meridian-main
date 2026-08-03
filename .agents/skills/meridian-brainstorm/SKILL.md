---
name: meridian-brainstorm
description: >
  Brainstorming, ideation, and creative feature exploration for the Meridian project.
  Use whenever the user wants new ideas, features, or improvements for Meridian,
  or when they ask "what could we add", "how could we improve", "what would be valuable", "what features should we build",
  or any variant of generative thinking about the project. Also trigger when the user describes a pain point,
  an operator role (financial operations, fund accountant, reconciliation analyst, controller, portfolio manager,
  compliance officer, auditor), or a domain problem (import trust, reconciliation breaks, close readiness, NAV support,
  evidence retention, approvals, governed reporting, execution safety). Also trigger for architecture or refactoring
  brainstorms, activation ideas for built-but-unwired capability, adoption and onboarding strategy, or technical debt
  ideation. Trigger even if the user just says "brainstorm" or "give me ideas" here. Produces idea writeups with
  implementation sketches, audience fit, and effort ratings.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads markdown references on demand
  and may append to a local brainstorming history ledger when the host permits filesystem writes.
metadata:
  owner: meridian-ai
  version: "2.0"
  spec: open-agent-skills-v1
---
# Meridian — Brainstorming & Ideation Skill

Generate high-value, implementable ideas for the Meridian platform. Every idea should feel like a natural extension of the program — something that makes the existing operator experience richer, clearer, and more provable, not a bolt-on afterthought.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md) — authoritative platform framing, solution map, key abstraction file paths, and review guardrails. Read this before generating ideas that reference specific classes, interfaces, or surfaces.

---

## Integration Pattern

Every brainstorming task follows this 4-step workflow:

### 1 — GATHER CONTEXT

- Read `../_shared/project-context.md` for the authoritative platform framing and abstraction paths
- Check current product direction in `docs/product/meridian-design-document.md` (the canonical charter) and live status in `docs/roadmap/generated/ROADMAP_SUMMARY.md` — never assert status from memory
- Fetch the GitHub issue, discussion, or feature request that prompted the brainstorm, when there is one
- Review [`references/competitive-landscape.md`](references/competitive-landscape.md) for competitive signals relevant to the request

### 2 — ANALYZE & PLAN

- Detect the brainstorm mode (Open Exploration, Problem-Focused, Activation, etc.) using the mode table
- Check the `brainstorm-history.jsonl` ledger to avoid repeating previously covered themes
- Plan the idea set: quantity targets, operator roles, and effort tiers to cover

### 3 — EXECUTE

- Emit the mode declaration and summary table before writing any ideas
- Write each idea as a natural narrative with anchor, operator moment, implementation shape, and tradeoffs
- Synthesize: call out the highest-leverage idea, platform bets, and sequencing recommendation

### 4 — COMPLETE

- Append a new entry to `brainstorm-history.jsonl` recording today's session themes
- When the session produces a durable artifact, write it to `docs/product/<topic>-brainstorm-<yyyy-mm>.md` and register it as a dated working design input, not a status source
- If the session produced actionable proposals, create GitHub issues for the top 1-3 ideas or open a discussion thread to share the output

---

## Core Philosophy: Complementary Extension

The best ideas for Meridian aren't isolated features. They're extensions that **amplify what already exists**. Before generating any idea, ask:

1. **What does Meridian already do well nearby?** Find the existing capability this idea extends, deepens, wires up, or connects to. An idea with no anchor to current functionality is probably the wrong idea.
2. **What would an operator actually see and do?** Every idea must have a concrete UI or interaction moment. If you can't describe what the operator clicks, reads, approves, or resolves — the idea isn't finished yet.
3. **Does this make a number more provable?** Meridian sells proven numbers: every figure carries a reconstructable chain from source evidence through normalization, validation, reconciliation, ledger impact, approval, report usage, and delivery. Ideas that shorten, strengthen, or expose that chain outrank ideas that only add surface.
4. **Is the information presented clearly?** Evidence-dense applications live or die on information hierarchy. Every idea that touches the UI should consider: what's the most important thing on screen? What's secondary? What can be progressive-disclosed until needed?

---

## Project Context

**What Meridian is:**
A modular, configurable, self-hosted .NET 10 financial operations platform for fund administrators, private fund managers and fund CFOs, registered investment advisors, family offices, and hybrid institutional teams. Fund management is a first-class *specialization*, not the platform root model — core contracts use customer-neutral concepts (organization, entity, portfolio, account, book, period, transaction, evidence, approval, journal, report, audit trail). Trading, research, backtesting, and paper validation are lanes on the same governed evidence spine, not a separate product.

**The operating question every surface must answer:** *Can Meridian prove, book, reconcile, approve, and report this number?*

**The canonical operator spine:** `Import → Validate → Reconcile → Investigate → Approve → Report`

**Tech stack:** .NET 10, C# infrastructure, F# deterministic calculation kernels, React/TypeScript browser workstation, WPF desktop workstation, Docker, Prometheus/Grafana, OpenTelemetry, Bounded Channels, WAL storage, JSONL + Parquet, Postgres-backed stores, GitHub Actions CI/CD.

**Scale of the foundation** (measured 2026-07-28; authoritative table in `docs/product/meridian-design-document.md` §5.2):

- 41 source projects under `src/` (52 projects in `Meridian.sln` including tests and benchmarks)
- ~242,000 lines of C# (2,902 files); 12,736 lines of F# across four calculation projects
- ~295,000 lines of TypeScript/TSX in the browser workstation (720 files, 165 screen modules)
- 497 C# files and 147 XAML views in the WPF desktop workstation
- 845 distinct `/api/...` routes; 22 provider adapter families; 6 statement connector formats
- 142 classes in `src/Meridian.Ledger/`; ~12,900 xUnit facts/theories across 12 .NET test projects

**Wave posture** (live status is always the roadmap registry, never this file):

- **Closed baselines:** provider trust gate (W1), paper trading cockpit and promotion evidence (W2), research-to-paper continuity (W3), ledger reconciliation and governed report packs (W4), accounting records and multi-asset coverage (W5), shared Financial Record Explorers, Financial Operations control center, statement connector library, bounded live-readiness governance, asset accounting event spine
- **Active:** Evidence Vault productization (`W5X-EVIDENCE-001`), statement reconciliation onboarding wedge (`W5X-STMT-ONBOARD-001`), WPF web-UI parity (`W8-WPF-PARITY-001`), browser screen consolidation (`W8-UX-CONSOL-001`)
- **Planned:** Operational Evidence Graph surface (`W5X-OEG-001`), Backtesting Studio (`W6-BTSTUDIO-001`), and the ranked W9 slate — truth/fail-closed (`W9-TRUTH-001`), seeded demo (`W9-DEMO-002`), paper realism (`W9-PAPER-003`), broker fill streaming (`W9-ALPACA-004`), client-grade exports (`W9-REPORT-005`), unitized NAV economics (`W9-NAV-006`), execution safety rules (`W9-SAFETY-007`), route authorization and hash-chained audit (`W9-GOV-008`), institutional ingestion (`W9-INGEST-009`)

**Two co-equal operator UI lanes over one shared seam:** the browser workstation in `src/Meridian.Ui/dashboard/` (built assets in `src/Meridian.Ui/wwwroot/workstation/`) and the WPF desktop workstation in `src/Meridian.Wpf/`. Both consume `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, and `src/Meridian.Contracts/`. Presentation can differ; business state cannot.

---

## Non-Negotiable Guardrails

Ideas that violate these are wrong ideas, however attractive. Check every idea against this list before writing it up.

- **Seven root workspaces only:** `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`. `Research`, `Data Operations`, and `Governance` are compatibility aliases, not new roots. Never propose an eighth root workspace.
- **No mobile lane:** no native iOS/Android, MAUI, React Native, Flutter, or mobile-first workflows. Responsive browser behavior is allowed only to keep the browser workstation usable.
- **Truth discipline:** simulated, sample, or fixture-derived values carry loud labels; unsupported persistence fails closed; missing or stale evidence renders as `review-required` or `blocked`, never as plausible-looking data. Never propose a feature that makes unwired capability look finished.
- **Governed autonomy:** AI may extract, match, summarize, detect discrepancies, and draft. It must never bypass operator approval, retained evidence, ledger controls, period locks, segregation of duties, report release checks, or payment controls.
- **Meridian owns ledger truth:** external accounting systems contribute read-only evidence unless an approved publishing workflow exports Meridian-owned entries.
- **Deferred expansion boundaries** (`docs/product/deferred-expansion-boundaries.md`): live treasury payment execution, enterprise risk, forecasting engines, capital-structure modeling, broad client portal, and no-code workflow design each require a roadmap row defining evidence, approval, and audit gates first. You may brainstorm *toward* the acceptance boundary; do not propose shipping past it.
- **Claim rules:** brainstorm output is an idea, never a status claim. Never describe planned or dormant capability as shipped.
- **Activation before expansion:** the codebase is more capable than the running product. When a dormant capability exists in source, wiring it is a stronger idea than building a new one beside it.

---

## Audience Roles

Meridian's canonical Persona Matrix lives in `docs/product/meridian-design-document.md` §4.2 (24 roles). For brainstorm triage, use these grouped codes in the summary table:

| Code | Roles | What they need from an idea |
| --- | --- | --- |
| **OPS** | Financial Operations Professional (primary operator), Reconciliation Analyst, Data Operations Analyst, Operations Manager, Treasury Operations Specialist | Fewer manual touches, clearer break resolution, trustworthy imports, visible queue state |
| **ACCT** | Investment Accountant, Fund Accountant, Reporting Analyst, Controller | Correct postings, NAV support, defensible close, period-lock discipline, package accuracy |
| **INV** | Portfolio Manager, Investment Analyst, Quantitative Researcher, Trader | Position and valuation truth, research-to-paper continuity, execution feedback, drill-down proof |
| **GOV** | Compliance Officer, Risk Manager, Auditor | Scoped access, retained evidence, audit reconstruction, control enforcement |
| **EXEC** | CFO, CIO | Decision-grade summaries, exception visibility, blocked-output clarity |
| **STAKE** | Fund Investor / LP, RIA Client, Family Beneficiary, Trustee, Board / IC Member | Governed delivery, verifiable statements, entitlement-scoped read access |
| **ADMIN** | System / Security / Integration Administrator | Platform health, connection setup, permission scoping, upgrade safety |

The **Financial Operations Professional is the primary operator**. When an idea has no OPS, ACCT, or GOV benefit, say so explicitly and justify why it still earns build time.

---

## The Operator Experience Lens

**Apply this lens to every idea.** Meridian is an application operators live in for a working day. How information is organized, how state is communicated, how evidence surfaces, and how blockers explain themselves determines whether someone trusts it or falls back to a spreadsheet.

- **Information hierarchy matters.** An evidence-dense app easily becomes a wall of rows. Every screen should have a clear primary focus, secondary details on hover or drill-down, and a calm default state when nothing needs attention.
- **State at a glance.** An operator should look for 2 seconds and know: what is blocked, what is waiting on me, what is stale. Use color, density, and position to communicate state — not just text.
- **Evidence is one click away, never the main event.** Proof should be reachable from any number (drill from a report line to its journal, its source record, its approval) without dominating the working surface.
- **Progressive disclosure.** Show the essential view first; expand into detail on demand. The reconciliation queue's break count is the top layer; the per-row matching detail is the drill-down.
- **Contextual actions.** When showing state, show what the operator can *do* about it. Viewing an unmatched statement row? Offer match, split, and escalate right there.
- **Blocked states must explain themselves.** "Blocked" is only useful with the reason, the owner, and the next action attached.
- **Consistency across views.** Entities, accounts, periods, evidence chips, and status indicators should look and behave the same everywhere. Build on the shared design system, not per-screen variants.
- **Respect both lanes.** Browser-workstation ideas should feel native to a browser operator surface; WPF ideas should preserve dense desktop affordances while consuming the same shared contracts. Neither lane may fork product state.

---

## How to Run a Brainstorm

### Step 0: Detect Mode (Explicit)

Before generating any ideas, output a one-line mode declaration at the top of your response:

> **Mode detected:** [Mode Name] — *[one sentence explaining why]*

Use the table below. If the request is ambiguous between two modes, state both and explain which one you're optimizing for.

| Mode | Trigger phrases | Approach |
| ------ | --------- | ---------- |
| **Open Exploration** | "What could we build?" / "What's valuable?" / "Give me ideas" | Generate across domains and roles. Favor ideas that connect existing capabilities into one proven path. |
| **Problem-Focused** | "How do we solve X?" / "What's the best way to fix Y?" | 3-5 deep ideas targeting the specific pain. Show how each integrates with existing surfaces. |
| **Activation** | "What's built but not wired?" / "What can we turn on?" / "Cheapest high-value win" | Find dormant capability in source, propose the wiring path, and name what it replaces on the live path. Read the activation register in charter §2.1. |
| **Role-Focused** | "What do fund accountants need?" / "Ideas for controllers" / "Auditor use cases" | 5-8 ideas optimized for that role. Describe the working session, not just the feature. |
| **Domain-Focused** | "Ideas for reconciliation / close / evidence / providers / reporting" | Technical depth in that domain. Every idea still needs an operator touchpoint. |
| **Competitive** | "What are others doing?" / "How do we compare to BlackLine?" | Scan `references/competitive-landscape.md`. Identify gaps; only propose ideas that fit Meridian's architecture naturally. |
| **Quick Wins** | "What's easy to ship?" / "Low-hanging fruit?" | Effort ≤ Medium, impact ≥ High. Emphasize ideas that improve the existing path, not new surface area. |
| **Architecture / Refactoring** | "How should we restructure X?" / "Refactoring ideas" | Code structure, seam integrity, shared-contract compliance, testability. Anchor to real patterns (`EventPipeline`, `IStorageSink`, `BindableBase`, endpoint/read-model seams). Include before/after and migration risk. |
| **Evidence & Governance** | "How do we prove X?" / "Audit readiness ideas" / "Approval workflow gaps" | Evidence packets, provenance, approval separation, retention, audit reconstruction. Every idea names what it blocks when absent. |
| **Adoption / Onboarding** | "How do we reduce time-to-value?" / "Onboarding friction" / "Shadow-mode adoption" | Time to First Proof, seeded demo, import wedge, shadow-mode parallel books. Map to roles and to the wedge in charter §22. |
| **Technical Debt / Code Quality** | "What tech debt should we address?" / "Code quality improvements" | Test effectiveness, static analysis, CI hardening, dead code, duplicated seams. Quantify the cost of inaction. |
| **UX / Information Design** | "How should we display X?" / "The screen feels cluttered" / "Workspace design" | Information architecture, visual hierarchy, interaction flow, clarity. Every idea is UI-first and maps to one of the seven roots. |
| **Skill Improvement** | "How can the skills be improved?" / "Improve the brainstorm skill" | Apply the brainstorm process reflexively to the skills themselves: eval coverage, output quality, reference freshness, DX. |

### Step 1: Emit the Summary Table

**Before writing any ideas**, output a summary table. This lets the user triage in 30 seconds:

```markdown
## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | [Short name] | S/M/L/XL | OPS/ACCT/INV/GOV/EXEC/STAKE/ADMIN | High/Med/Low | [prereq or —] |
| 2 | ... | | | | |
```

Effort key: **S** = days, **M** = 1-2 weeks, **L** = 1+ month, **XL** = quarter+

### Step 2: Generate Ideas

Write each idea as a **natural narrative**, not a form to fill out. The reader should understand the idea, why it matters, how it works, and what it looks like — in that order.

**What every idea must include** (woven into prose, not as labeled fields):

- **The anchor:** What existing Meridian capability does this extend, wire, or complement? Reference real file paths (e.g., "extends `StatementMatchingEngine` at `src/Meridian.FinancialOperations/Reconciliation/StatementMatchingEngine.cs`"). Use the anchor table in `references/idea-dimensions.md`.
- **The operator moment:** What does the operator see, click, resolve, or approve? Name the workspace it lands in and the screen it changes.
- **The implementation shape:** Key technical approach — contracts, seams, data flow, persistence. Enough that a developer could start scoping.
- **The evidence and governance impact:** What proof does this create, retain, or expose? What does it block when absent? What approval does it require?
- **The tradeoffs:** What's hard? What could go wrong? What does this cost in complexity?
- **Effort and audience:** Who benefits most? How big is this?

**What to include when relevant** (not every idea needs all of these):

- A rough UI sketch described in words (layout, what's prominent, what's secondary)
- How this feature connects to other features and workspaces
- Before/after comparison for architecture ideas
- Which roadmap row it belongs to, or that it needs a new one
- Debt cost and payoff timeline for tech debt ideas

**Quantity guidelines:**

- Open Exploration: 8-12 ideas across 3+ domains
- Problem / Role / Domain focused: 4-6 deep ideas
- Activation: 4-6 ideas, each naming the dormant capability and the live path it replaces
- Quick Wins: 6-8 ideas
- Architecture/Refactoring: 4-6 ideas
- Evidence & Governance: 4-6 ideas
- Adoption / Onboarding: 5-8 ideas
- Technical Debt/Code Quality: 4-6 ideas
- UX/Information Design: 4-6 ideas

### Step 3: Synthesize

After the ideas, step back and write a synthesis that:

- Identifies the highest-leverage idea (best impact/effort ratio, most complementary to existing capability)
- Calls out "platform bets" — ideas that unlock multiple others
- Flags cross-cutting themes (e.g., "three of these ideas all need the evidence graph projection")
- Suggests sequencing: what to build first, what it enables next
- Names roadmap fit: which existing row absorbs each idea, and which would need a new row
- **Competitive signals (always include):** 2-3 sentences from `references/competitive-landscape.md` on how adjacent products handle this space and which pattern is most adaptable to Meridian's architecture
- For Activation mode: what the wiring replaces, and how the predecessor gets retired
- For Architecture mode: migration ordering and risk dependencies
- For Adoption mode: which stage of Time to First Proof to invest in
- For Tech Debt mode: quick wins first, then structural changes
- For UX mode: which screens to redesign first based on operator frequency and pain

---

## Tone & Output Standards

**Write ideas like you're pitching them to a product-minded developer**, not filling in a form. Each idea should read as a short, compelling argument — "here's what's painful today, here's what we'd build, here's what it looks like, here's why it's worth it."

- **Be specific, not generic.** "Improve reconciliation" is weak. "Promote `StatementMatchingEngine`'s sided matching onto the live statement path so a bank row and a ledger row match as a pair, with a confidence score and a one-click split/escalate action in the Accounting reconciliation queue" is strong.
- **Always describe the operator experience.** Even backend work has a user-facing moment: "The close that used to need a spreadsheet tie-out now shows a readiness score with the three blocking items named, each linking to the record that caused it."
- **Show how features connect.** "This evidence chip appears next to every report line; clicking it opens the same proof drawer the reconciliation queue uses."
- **Acknowledge tradeoffs honestly.** Hidden complexity is the enemy. Name it.
- **Anchor to the codebase.** Reference real abstractions: `EventPipeline`, `IStorageSink`, `IOrderGateway`, `IRiskRule`, `AccountingCloseManagementService`, `EvidenceGraphService`, `LedgerReportPackBuilder`, `WorkstationEndpoints`, the Options pattern, source-generated JSON.
- **For architecture ideas:** show concrete code-level changes, not just diagrams. Reference actual class names and namespaces.
- **For adoption ideas:** be honest about what requires sustained investment vs. one-time effort.
- **For tech debt ideas:** quantify the cost. "Two services own overlapping close state, so a fix has to land twice" beats "this is messy."
- **For UX ideas:** describe the screen. What's at the top? What's the primary action? What's visible by default vs. on drill-down? How does this view connect to the rest of the workspace?

---

## Idea Continuity (Session History)

A ledger of past sessions is maintained at `.agents/skills/meridian-brainstorm/brainstorm-history.jsonl` and tracked in the repository. Each line is a JSON object:

```json
{"session_date": "2026-07-14", "mode": "Problem-Focused / UX", "themes": ["Excel onboarding workbook generator", "multi-sheet XLSX staged review"], "ideas_count": 5, "document_updated": "docs/product/excel-onboarding-workbook-brainstorm-2026-07.md", "notes": "code findings and sequencing"}
```

Read it before generating, and open each brainstorm session with:
> **Previous sessions covered:** [summary of themes from history]. **Unexplored areas:** [gaps].

Append a new line at the end of the session. This prevents the same ideas from appearing session after session and surfaces genuinely new territory.

---

## Idea Generation Reference

When brainstorming, read [`references/idea-dimensions.md`](references/idea-dimensions.md) for the full seeded concept bank organized by domain. The file opens with a **codebase anchor table** mapping concept names to verified file paths.

**Quick-access dimensions:**

- **Import & data trust:** connector coverage, mapping profiles, drift detection, preview/confidence, replay, certified import runs
- **Reconciliation & breaks:** sided matching, tolerance policy, casework, SLA, aging, bulk resolution, root-cause clustering
- **Ledger & accounting:** journals, period lock, close readiness, capital accounts, tax lots, multi-currency, NAV economics
- **Evidence & provenance:** evidence packets, manifests, document extraction, object links, evidence graph, number-level lineage
- **Reporting & delivery:** report packs, report-writer grids, client-grade rendering, provenance drill-through, publication and restatement
- **Portfolio & valuation:** positions, marks, security master, corporate actions, multi-asset coverage, cash ladder
- **Execution & risk:** order gateways, paper realism, fill and cost models, pre-trade rules, kill-switch and safety controls
- **Research & strategy:** strategy lifecycle, backtesting runtime, run comparison, promotion evidence, QuantScript
- **Workstation UX:** the seven roots, screen consolidation, command palette, trust strip, proof drawers, operator focus and queues
- **Governance & tenancy:** scoped authorization, approval separation, audit chain, retention, access review
- **Platform quality:** durability, performance, observability, test effectiveness, CI hardening, activation coverage

---

## Handoffs

- Hand off to `meridian-blueprint` when the user selects one idea for implementation design.
- Hand off to `meridian-roadmap-strategist` when ideas need sequencing into roadmap waves or a new registry row.
- Hand off to `meridian-simulated-user-panel` when ideas need persona critique before selection.
- Hand off to `meridian-repo-navigation` when grounding an idea needs subsystem routing first.

---

## Reference Files

- [`references/idea-dimensions.md`](references/idea-dimensions.md) — Seeded idea bank organized by domain, including the verified codebase anchor table
- [`references/competitive-landscape.md`](references/competitive-landscape.md) — How close managers, fund-administration suites, asset-servicing platforms, ledger APIs, and market-data vendors compare (read for competitive signals in every synthesis section)
- `../_shared/project-context.md` — Authoritative platform framing, solution map, abstraction file paths, review guardrails
- `docs/product/meridian-design-document.md` — Canonical product charter: value proposition, doctrines, persona matrix, implementation baseline, wedge
- `docs/roadmap/generated/ROADMAP_SUMMARY.md` — Live roadmap status; the only source for what is done, active, or planned
