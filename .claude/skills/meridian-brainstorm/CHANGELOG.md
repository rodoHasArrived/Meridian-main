# meridian-brainstorm — Changelog

## v2.0.0 (2026-07-28)

Realignment to the current product charter. The skill had drifted to a v1.3.0 snapshot (2026-03-19)
that framed Meridian as a market-data collection platform for hobbyist quants, academics, and
institutional trading desks. That framing no longer matches the repository or the canonical design
document.

### Changed

- **Product framing rewritten** — Meridian is now described as a self-hosted financial operations
  platform selling *proven numbers* to fund administrators, private fund managers/fund CFOs, RIAs,
  family offices, and hybrid institutional teams. Fund management is documented as a first-class
  specialization, not the root model. The canonical operator spine
  (`Import → Validate → Reconcile → Investigate → Approve → Report`) replaces the four-pillar
  (ADR-016) framing as the organizing idea
- **Project statistics replaced** — v1.3.0 claimed 868 source files, 261 test files, 22 main
  projects, and 33 CI/CD workflows. Now sourced from the design charter §5.2 measurement table
  (2026-07-28): 41 source projects, ~242,000 lines of C# across 2,902 files, 12,736 lines of F#,
  ~295,000 lines of TypeScript/TSX, 845 `/api/...` routes, 22 provider adapter families,
  6 statement connector formats, 142 ledger classes, ~12,900 xUnit facts/theories
- **Wave posture added** — closed baselines (W1-W5, W5X FREX/FINOPS/CONNECT, W7, W9-ASSET-010),
  active rows (`W5X-EVIDENCE-001`, `W5X-STMT-ONBOARD-001`, `W8-WPF-PARITY-001`,
  `W8-UX-CONSOL-001`), and the ranked W9 slate, with the roadmap registry named as the only live
  status source
- **WPF lane corrected** — was described as a "retained compatibility surface" with WPF product/UI
  scope closed; it is an active co-equal operator UI lane whose current focus is web-UI parity
  (`W8-WPF-PARITY-001`)
- **Personas replaced** — Hobbyist Quant Developer / Academic / Institutional gave way to grouped
  codes over the canonical 24-role Persona Matrix (design charter §4.2): OPS, ACCT, INV, GOV, EXEC,
  STAKE, ADMIN. The Financial Operations Professional is named as the primary operator, and the
  summary-table audience key changed from `H/Q/I` accordingly
- **User Experience Lens rewritten** as the Operator Experience Lens — evidence-one-click-away,
  self-explaining blocked states, and the two co-equal UI lanes replace "Meridian is a desktop
  application people monitor for hours"
- **Mode table updated** — added **Activation** (wire dormant capability), **Evidence &
  Governance**, and **Adoption / Onboarding**; renamed Persona-Focused to Role-Focused; retargeted
  Competitive and UX triggers to the current product surface. Growth mode's community/evangelism
  framing was folded into Adoption
- **Idea requirements expanded** — every idea now states its evidence and governance impact
  (what proof it creates, what it blocks when absent, what approval it requires)
- **Session ledger corrected** — `brainstorm-history.jsonl` is tracked in the repository, not
  gitignored; the documented entry shape now matches the real records
- **`references/idea-dimensions.md` rebuilt** — the anchor table is reorganized into seven verified
  sections (import/providers, reconciliation, ledger/close, fund economics, evidence, reporting,
  execution/research, platform seams) with all paths checked against the tree on 2026-07-28;
  `GracefulShutdownService` repointed to `src/Meridian.Platform/Runtime/`; `UiApiRoutes` recorded at
  `src/Meridian.Contracts/Api/`. The ten market-data-era dimension categories were replaced with
  eleven current-domain categories, and an **Activation Targets** section was added from the
  charter's activation register
- **`references/competitive-landscape.md` rebuilt** — Bloomberg/Databento/Polygon/QuestDB
  positioning was the whole file; it is now scoped to the Trading/Data lane, with close-and-controls
  managers (BlackLine, Trintech), fund administration suites (Carta, FundStudio), asset servicing
  (SS&C Advent Geneva, eFront, Addepar), and ledger APIs (Modern Treasury) as the primary
  categories. The differentiation matrix and moat list follow design charter §1.4

### Added

- **Non-Negotiable Guardrails section** — seven root workspaces, no mobile lane, truth discipline,
  governed autonomy, Meridian owns ledger truth, deferred expansion boundaries, claim rules, and
  activation-before-expansion. Ideas that violate these are documented as wrong ideas
- **Handoffs section** — routes to `meridian-blueprint`, `meridian-roadmap-strategist`,
  `meridian-simulated-user-panel`, and `meridian-repo-navigation`, matching the Codex lane
- **Durable-artifact step** in the completion workflow — brainstorms that produce documents write to
  `docs/product/<topic>-brainstorm-<yyyy-mm>.md` as a dated working design input, never a status
  source

---

## v1.3.0 (2026-03-19)

### Changed
- **Project statistics updated** — 868 source files (856 C# + 12 F#), 261 test files, 22 main projects, 33 CI/CD workflows; previous v1.2.0 stats were 779 source files, 15 projects, 27 workflows
- **Solution layout updated** — added 7 new projects: `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.Execution`, `Meridian.Execution.Sdk`, `Meridian.Ledger`, `Meridian.Mcp`, `Meridian.McpServer`, `Meridian.Risk`, `Meridian.Strategies`; removed stale `Meridian.Execution` / `Meridian.Strategies` references (all projects now use `Meridian.*` namespace)
- **Dependency graph updated** — added allowed deps for `Backtesting`, `Backtesting.Sdk`, `Execution.Sdk`, `Ledger`, `Risk`, `Mcp/McpServer`; added forbidden rule: `Ledger → any other Meridian project` (zero-dependency leaf)
- **Provider inventory updated** — added `TwelveData` as 11th historical provider; updated streaming count from "2" to "5" in competitive landscape matrix
- **ADR table expanded** — added ADR-015 (Strategy Execution Contract: `IOrderGateway` + `IExecutionContext`) and ADR-016 (Four-Pillar Architecture)
- **Competitive landscape updated** — differentiation matrix now reflects backtesting engine (live), paper trading (live), strategy execution (live), and MCP/AI tooling (unique to Meridian)
- **New key abstractions documented** — `IOrderGateway`, `IExecutionGateway`, `IExecutionContext`, `IRiskValidator`, `IRiskRule`, `IStrategyLifecycle`, `Ledger`/`IReadOnlyLedger`, `IBacktestStrategy`, `IBacktestContext`
- **Idea anchor table expanded** — 15 new entries covering execution, risk, strategies, backtesting SDK, and ledger abstractions
- **Project description updated** — from "market data collection tool" to "four-pillar algorithmic trading platform" per ADR-016

---

## v1.2.0 (2026-03-16)

### Added
- **Summary table (Ideas at a Glance)** — every brainstorm output now opens with a triage table (Idea | Effort | Audience | Impact | Depends On) before the narrative ideas; S/M/L/XL effort keys; lets users triage in 30 seconds
- **Explicit mode detection** — Step 0 now requires a one-line mode declaration at the top of the response (`**Mode detected:** [Mode Name] — [reasoning]`); prevents silent mode mismatches; ambiguous requests state both modes
- **Skill Improvement mode** — added as an explicit mode in the mode table; triggers when the user asks how the skills themselves can be improved; applies the brainstorm process reflexively
- **Competitive signals in every synthesis** — synthesis section now always includes 2-3 sentences from `references/competitive-landscape.md` on how competitors handle the brainstorm space; was previously only active in Competitive mode
- **Idea continuity / session ledger** — documented `brainstorm-history.jsonl` convention at `.claude/skills/meridian-brainstorm/brainstorm-history.jsonl` (gitignored); opens each session with "Previous sessions covered: X. Unexplored areas: Y."
- **Codebase anchor table** in `references/idea-dimensions.md` — 35-entry table mapping concept names to file paths and class names; makes ideas immediately navigable; covers all major interfaces, sinks, validators, providers, and WPF classes
- **Shared project context** — SKILL.md now references `../_shared/project-context.md` for authoritative stats, ADR table, and file paths; updated project context section to match actual current state (779 files, 266 test files, 27 CI workflows, 5 streaming providers)

### Changed
- Updated project context section: 779 source files (was unstated), 266 test files, 13 main projects, 5 streaming providers (Alpaca, Polygon, IB, StockSharp, NYSE)
- `references/idea-dimensions.md`: added frontmatter pointer to `_shared/project-context.md`; prepended codebase anchor table before existing dimension categories
- Mode table: added explicit trigger phrases for each mode; reordered to match frequency of use; added "Skill Improvement" mode

---

## v1.1.0 (2026-02-28)

### Added
- Added competitive mode with `references/competitive-landscape.md`
- Added UX / Information Design mode
- Added Technical Debt / Code Quality mode

### Changed
- Expanded persona descriptions for Hobbyist, Academic, and Institutional audiences
- Added WPF-specific UX principles to "The User Experience Lens" section

---

## v1.0.0 (2026-02-01)

### Added
- Initial skill release with 9 brainstorm modes
- `references/idea-dimensions.md` with 10 seeded concept categories
- Synthesis section format with highest-leverage idea, platform bets, and sequencing
