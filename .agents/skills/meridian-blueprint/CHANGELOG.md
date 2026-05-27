# meridian-blueprint — Changelog

## v1.0.0 (2026-03-17)

### Added

- **Initial skill release** — Blueprint Mode for Meridian
- **9-section blueprint format** — Scope, Architectural Overview, Interface & API Contracts,
  Component Design, Data Flow, XAML & UI Design, Test Plan, Implementation Checklist, Open
  Questions & Risks
- **3 depth modes** — `full` (all 9 sections), `spike` (riskiest unknowns + Steps 1–3),
  `interface-only` (Steps 1–3 only for contract alignment)
- **JSON output** — Optional `blueprint.json` summary with interface list, component list,
  config schema, checklist, and test counts
- **4-step integration pattern** — GATHER CONTEXT → ANALYZE & PLAN → EXECUTE → COMPLETE
  (matching other Meridian skills)
- **`references/blueprint-patterns.md`** — Meridian naming conventions, ADR contract reference,
  DI registration patterns, Options pattern, Channel/pipeline pattern, WPF/MVVM patterns,
  F# domain type patterns, error handling and structured logging patterns, storage sink pattern,
  historical provider pattern, and breaking change checklist
- **`references/pipeline-position.md`** — Full pipeline diagram (Brainstorm → Evaluator →
  Roadmap → Blueprint → Implementation → Code Review → Test Writing), stage input/output
  contracts, and handoff contracts between all pipeline stages
- **Breaking change detection** — `⚠️ Breaking Change` block convention; consumer enumeration
  checklist; ADR amendment guidance
- **Sprint constraint support** — `target_sprint` constraint causes checklist to fit sprint;
  deferred tasks documented explicitly
- **Pipeline integration** — Blueprint fits after `meridian-brainstorm`/Roadmap Builder and before
  implementation, `meridian-code-review`, and `meridian-test-writer`
- **Complementary agent** — `.claude/agents/meridian-blueprint.md` for use in subagent contexts
- **GitHub agent** — `.github/agents/meridian-blueprint-agent.md` for GitHub Actions equivalence
- **`skills_provider.py` registration** — `mdc_blueprint_skill` registered in
  `SkillsProvider`, with dynamic `blueprint-git-context` resource and `validate-skill` script
