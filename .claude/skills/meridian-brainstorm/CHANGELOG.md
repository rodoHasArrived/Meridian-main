# meridian-brainstorm — Changelog

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
