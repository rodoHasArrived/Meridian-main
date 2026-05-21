# Meridian Codex Skills

This folder contains Meridian's repo-local Codex skills. These are the primary project-scoped
skills for the current AI workflow and should stay aligned with Meridian's browser-first operator
workstation direction, retained WPF support, fund-management/trading-platform scope, and no-mobile
development policy.

Last verified against `README.md` and `docs/status/ROADMAP.md`: 2026-05-13.

## Current Skills

| Skill | Entry Point | Purpose |
|-------|-------------|---------|
| `meridian-archive-organizer` | [`SKILL.md`](meridian-archive-organizer/SKILL.md) | Archive stale code/docs and keep the repo structure tidy |
| `meridian-blueprint` | [`SKILL.md`](meridian-blueprint/SKILL.md) | Create implementation-ready Meridian technical blueprints |
| `meridian-brainstorm` | [`SKILL.md`](meridian-brainstorm/SKILL.md) | Generate Meridian-native product and architecture ideas |
| `meridian-cleanup` | [`SKILL.md`](meridian-cleanup/SKILL.md) | Clean up code and docs without behavior changes |
| `meridian-code-review` | [`SKILL.md`](meridian-code-review/SKILL.md) | Review changes for bugs, regressions, and architecture drift |
| `meridian-implementation-assurance` | [`SKILL.md`](meridian-implementation-assurance/SKILL.md) | Implement and verify changes with strict Codex gates, explicit evidence, and docs sync |
| `meridian-provider-builder` | [`SKILL.md`](meridian-provider-builder/SKILL.md) | Build and extend provider integrations |
| `meridian-repo-navigation` | [`SKILL.md`](meridian-repo-navigation/SKILL.md) | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | [`SKILL.md`](meridian-roadmap-strategist/SKILL.md) | Refresh roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel` | [`SKILL.md`](meridian-simulated-user-panel/SKILL.md) | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | [`SKILL.md`](meridian-test-writer/SKILL.md) | Write scenario-first Meridian tests |

## Shared Resources

- [`_shared/project-context.md`](_shared/project-context.md) — current product framing, solution
  map, key abstractions, and review guardrails
- [`_shared/codex-execution-contract.md`](_shared/codex-execution-contract.md) — Codex-only
  execution gates for safe concurrency, narrow validation, cosmetic-churn avoidance, docs sync,
  AI tooling gates, and final response shape
- [`docs/ai/codex/README.md`](../../docs/ai/codex/README.md) — Codex-specific AI docs index,
  skill validation commands, and required/advisory/maintenance tooling split

## Maintenance Rules

- Keep each skill's `description` aligned with the current `README.md` and
  `docs/status/ROADMAP.md`, not with older market-data-only phrasing.
- Every current Codex skill must reference `_shared/project-context.md` and
  `_shared/codex-execution-contract.md` so execution behavior stays consistent without duplicating
  the full contract in each skill.
- Treat `src/Meridian.Ui/dashboard/` and `/workstation/` as the default operator UI surface for
  new browser-facing work. WPF guidance is retained support unless the user explicitly asks for
  desktop compatibility work.
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
- Validate Codex skill drift with `python build/scripts/docs/check-codex-skills.py --summary`
  after changing repo-local Codex skills, their `agents/openai.yaml` metadata, or Codex docs.
- Validate catalog drift with `python build/scripts/docs/check-ai-inventory.py --summary` after
  changing Codex skill metadata or shared context.

## Recommended Flow

1. `meridian-repo-navigation`
2. the relevant specialist skill
3. `meridian-implementation-assurance` when the change needs explicit validation
