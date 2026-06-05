# Meridian Codex Skills

This folder contains Meridian's repo-local Codex skills. These are the primary project-scoped
skills for the current AI workflow and should stay aligned with Meridian's active desktop app
operator UI direction, retained browser workstation support, fund-management/trading-platform
scope, W1-W5 operational record baseline, and no-mobile development policy.

Last verified against `README.md`, `docs/roadmap/data/*.yml`,
`docs/roadmap/generated/ROADMAP_SUMMARY.md`, and `docs/ai/assistant-workflow-contract.md`:
2026-06-04.

## Current Skills

| Skill | Entry Point | Purpose |
|-------|-------------|---------|
| `meridian-archive-organizer` | [`SKILL.md`](meridian-archive-organizer/SKILL.md) | Archive stale code/docs and keep the repo structure tidy |
| `meridian-blueprint` | [`SKILL.md`](meridian-blueprint/SKILL.md) | Create implementation-ready Meridian technical blueprints |
| `meridian-brainstorm` | [`SKILL.md`](meridian-brainstorm/SKILL.md) | Generate Meridian-native product and architecture ideas |
| `meridian-browser-workstation` | [`SKILL.md`](meridian-browser-workstation/SKILL.md) | Implement and review browser workstation TypeScript/React changes |
| `meridian-cleanup` | [`SKILL.md`](meridian-cleanup/SKILL.md) | Clean up code and docs without behavior changes |
| `meridian-code-review` | [`SKILL.md`](meridian-code-review/SKILL.md) | Review changes for bugs, regressions, and architecture drift |
| `meridian-docs` | [`SKILL.md`](meridian-docs/SKILL.md) | Maintain Meridian documentation with repo-grounded evidence |
| `meridian-implementation-assurance` | [`SKILL.md`](meridian-implementation-assurance/SKILL.md) | Implement and verify changes with strict Codex gates, explicit evidence, and docs sync |
| `meridian-provider-builder` | [`SKILL.md`](meridian-provider-builder/SKILL.md) | Build and extend provider integrations |
| `meridian-repo-navigation` | [`SKILL.md`](meridian-repo-navigation/SKILL.md) | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | [`SKILL.md`](meridian-roadmap-strategist/SKILL.md) | Refresh roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel` | [`SKILL.md`](meridian-simulated-user-panel/SKILL.md) | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | [`SKILL.md`](meridian-test-writer/SKILL.md) | Write scenario-first Meridian tests |
| `modular-desktop-mvvm` | [`SKILL.md`](modular-desktop-mvvm/SKILL.md) | Implement modular WPF MVVM workstation changes |
| `workstation-screen-composition` | [`SKILL.md`](workstation-screen-composition/SKILL.md) | Compose desktop screens from shared workstation primitives |
| `shared-component-extraction` | [`SKILL.md`](shared-component-extraction/SKILL.md) | Extract repeated desktop patterns into reusable components |
| `provider-management-workflow` | [`SKILL.md`](provider-management-workflow/SKILL.md) | Build secure provider setup, health, credential, and recovery workflows |
| `research-data-acquisition` | [`SKILL.md`](research-data-acquisition/SKILL.md) | Build research acquisition, preview, validation, and lineage workflows |
| `dense-data-grid-inspector-panel` | [`SKILL.md`](dense-data-grid-inspector-panel/SKILL.md) | Build scalable dense grids and inspector panels |
| `diagnostics-audit-timeline` | [`SKILL.md`](diagnostics-audit-timeline/SKILL.md) | Build diagnostics panels, audit timelines, and evidence trails |
| `performance-resource-review` | [`SKILL.md`](performance-resource-review/SKILL.md) | Review memory, CPU, I/O, rendering, concurrency, and lifecycle risks |
| `safe-refactoring` | [`SKILL.md`](safe-refactoring/SKILL.md) | Refactor desktop code incrementally without behavior drift |
| `desktop-test-generation` | [`SKILL.md`](desktop-test-generation/SKILL.md) | Generate focused WPF view-model, command, service, and binding tests |

## Routing Model

Pick the narrowest lane that answers the request, then hand off only when the next phase is
different work:

| Lane | Skill | Boundary |
| --- | --- | --- |
| Orient | `meridian-repo-navigation` | Route first, name owner files/docs, then exit. |
| Ideate | `meridian-brainstorm` | Generate options and tradeoffs; do not write specs. |
| Plan | `meridian-blueprint` | Turn one selected idea into a code-ready design. |
| Implement or verify | `meridian-implementation-assurance` | Build or certify work with evidence and docs sync. |
| Review | `meridian-code-review` | Findings first; no implementation unless asked. |
| Docs | `meridian-docs` | Update docs, guidance, and indexes with current repo evidence. |
| Browser workstation | `meridian-browser-workstation` | Implement or review TypeScript/React dashboard changes in `src/Meridian.Ui/dashboard/`. |
| Test | `meridian-test-writer` | Add scenario-first tests in the right project. |
| Provider | `meridian-provider-builder` | Build provider adapters, then use assurance for rollout proof. |
| Archive | `meridian-archive-organizer` | Classify stale material and preserve useful history. |
| Roadmap | `meridian-roadmap-strategist` | Reconcile plans, delivery waves, and target state. |
| Cleanup | `meridian-cleanup` | Preserve behavior while improving maintainability. |
| Simulated user review | `meridian-simulated-user-panel` | Run persona-backed critique from concrete artifacts. |
| Desktop implementation | `modular-desktop-mvvm` | Implement WPF changes with MVVM, shared seams, tests, and resource guardrails. |
| Desktop composition | `workstation-screen-composition` | Shape new workspaces, tabs, panels, and command surfaces from shared primitives. |
| Component extraction | `shared-component-extraction` | Consolidate repeated controls, templates, commands, view models, and services. |
| Provider workflow | `provider-management-workflow` | Implement secure provider setup, health, credential, validation, and recovery flows. |
| Research acquisition | `research-data-acquisition` | Implement bounded research ingestion, preview, validation, lineage, and handoff flows. |
| Dense data UI | `dense-data-grid-inspector-panel` | Implement virtualized grids, row models, selection, detail tabs, and inspectors. |
| Diagnostics and audit | `diagnostics-audit-timeline` | Implement diagnostics panels, evidence trails, and audit timelines. |
| Resource review | `performance-resource-review` | Review and reduce memory, CPU, I/O, rendering, concurrency, and lifecycle risk. |
| Safe refactor | `safe-refactoring` | Preserve behavior while extracting, consolidating, and simplifying desktop code. |
| Desktop tests | `desktop-test-generation` | Add focused WPF tests for view models, commands, services, bindings, and shell routes. |

## Shared Resources

- [`_shared/project-context.md`](_shared/project-context.md) — current product framing, solution
  map, key abstractions, and review guardrails
- [`_shared/codex-execution-contract.md`](_shared/codex-execution-contract.md) — Codex-only
  execution gates for safe concurrency, narrow validation, cosmetic-churn avoidance, docs sync,
  AI tooling gates, and final response shape
- [`docs/ai/codex/quickstart.md`](../../docs/ai/codex/quickstart.md) — first-10-minutes Codex task
  routing, proof matrix, read budget, and dirty-worktree protocol
- [`docs/ai/codex/route-cards.md`](../../docs/ai/codex/route-cards.md) — compact subsystem cards
  for owner projects, first docs, entrypoints, and validation lanes
- [`docs/ai/codex/README.md`](../../docs/ai/codex/README.md) — Codex-specific AI docs index,
  skill validation commands, and required/advisory/maintenance tooling split
- [`../AGENTS.md`](../AGENTS.md) — Codex-specific desktop workstation implementation rules
- [`../prompts/`](../prompts) and [`../checklists/`](../checklists) — reusable desktop prompts and
  modularity, MVVM, resource, definition-of-done, and safe-refactor checklists
- [`docs/ai/agent-handoff-checklist.md`](../../docs/ai/agent-handoff-checklist.md) — multi-agent
  handoff format, context minimization, and validation handoff schema for cross-host workflows

## Maintenance Rules

- Keep each skill's `description` aligned with the current `README.md`, roadmap registry
  (`docs/roadmap/data/*.yml`), and generated roadmap views, not with older market-data-only
  phrasing or migrated status stubs.
- Every current Codex skill must reference `_shared/project-context.md` and
  `_shared/codex-execution-contract.md` so execution behavior stays consistent without duplicating
  the full contract in each skill.
- Every current Codex skill must include these decision sections: `Use When`, `Do Not Use When`,
  `Workflow`, `Handoffs`, `Validation`, and `Output Standards`.
- Every current Codex skill must include lightweight trigger and non-trigger examples so routing
  boundaries remain inspectable without running a full eval harness.
- Treat `src/Meridian.Wpf/` as the active desktop app operator UI path. Keep
  `src/Meridian.Ui/dashboard/` and `/workstation/` as retained browser workstation surfaces unless
  the user explicitly asks for browser workstation work.
- Keep skill descriptions and product examples centered on the W1-W5 operational record baseline:
  data confidence, retained source evidence, reconciliation, approvals, accounting records,
  multi-asset operational coverage, and governed reports. Treat Backtesting Studio, live-readiness,
  full payments, forecasting, enterprise risk, client portal, no-code workflow design, mobile, and
  other broad expansion lanes as deferred unless roadmap data moves them into active scope.
- Do not introduce mobile development guidance unless the roadmap or user explicitly reopens that
  lane.
- Keep `agents/openai.yaml` synchronized with the skill text so Codex UI metadata stays current.
- Mirror shared workflow changes into the corresponding Claude and GitHub agent surfaces when a
  specialist workflow is meant to stay host-consistent.
- For Codex-only implementation workflow changes, keep the edit in `.codex/skills/`, preserve
  disjoint-worker ownership, run narrow validation first, skip purposeless cosmetic churn, and keep
  code/doc evidence paired in the final response.
- Treat `make ai-verify`, `make ai-arch-check`, and the CI `Validate AI contract drift` step as
  required gates for AI/tooling changes. Keep `ai-audit*`, `ai-report`, docs-drift/freshness, and
  archive/maintenance targets as advisory or reporting lanes unless a task explicitly promotes them.
- For source/docs alignment, run `python build/scripts/docs/validate-doc-hashes.py --summary`.
  Refresh `docs/source/generated/source-hash-manifest.json` with
  `python build/scripts/docs/validate-doc-hashes.py --write --summary` only after confirming the
  nearest source README and registries still describe the changed code.
- When source READMEs need extra context, prefer conditional sections such as plans, end-user value,
  benchmarks/performance, operational evidence, security/credentials, API/contracts, or
  migration/archive notes. Skip empty optional sections.
- Validate Codex skill drift with `python build/scripts/docs/check-codex-skills.py --summary`
  after changing repo-local Codex skills, their `agents/openai.yaml` metadata, or Codex docs.
- Validate catalog drift with `python build/scripts/docs/check-ai-inventory.py --summary` after
  changing Codex skill metadata or shared context.
- Preserve `.agents/skills/` and `.claude/skills/` as host-neutral mirrors. Update them only when
  the changed workflow is shared across hosts, not when the change is Codex-specific structure or
  execution guidance.

## Recommended Flow

1. `meridian-repo-navigation`
2. the relevant specialist skill
3. `meridian-implementation-assurance` when the change needs explicit validation
