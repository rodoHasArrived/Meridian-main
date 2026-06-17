---
name: meridian-code-architecture
description: Review Meridian architecture conformance, module boundaries, dependency direction, ADR/source-doc alignment, and code-architecture decisions. Use when Codex needs to assess or design architecture-sensitive changes, dependency maps, module ownership, public seams, layer violations, or boundary drift before implementation, review, or refactoring.
---

# Meridian Code Architecture

Evaluate architecture decisions against Meridian's current module map, source documentation, ADR
rules, UI-surface split, and operational-record product boundary.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before naming
projects, dependencies, or validation commands. Read `references/architecture-checklist.md` before
producing a formal architecture assessment or dependency-map recommendation.

## Use When

Use this skill for architecture conformance, module-boundary decisions, dependency direction,
cross-project seams, ADR/source-doc alignment, and architecture-sensitive implementation planning.

Trigger examples:

- "Use $meridian-code-architecture to review this module architecture for boundary drift."
- "Map the dependencies before we move this workflow into a shared service."
- "Check whether this new project fits the Meridian module map and ADR rules."

## Do Not Use When

Use `meridian-blueprint` for a code-ready feature design, `meridian-code-review` for findings-first
bug review, and `safe-refactoring` for a behavior-preserving edit sequence after the architecture
decision is already made.

Non-trigger examples:

- "Write the blueprint for this selected feature."
- "Review this diff for null-reference bugs."
- "Refactor these files now without changing behavior."

## Workflow

1. Restate the architecture question and identify the affected projects or source paths.
2. Load the current module map, project structure, nearest source README, and any relevant ADR.
3. Run `scripts/architecture_surface.py --path <path> --summary` when a project or path is known.
4. Classify dependencies as allowed, needs-review, or violation using current layer boundaries.
5. Prefer existing shared service/read-model seams before proposing new projects or UI-specific forks.
6. State the implementation handoff: owning layer, public seam, docs to update, tests to run, and
   residual architecture risks.

## Handoffs

- Hand off to `meridian-blueprint` when the user wants the chosen architecture turned into a build spec.
- Hand off to `meridian-contract-governance` when the decision changes DTOs, routes, provider contracts, or shared read models.
- Hand off to `safe-refactoring` when the work is behavior-preserving extraction after boundaries are locked.
- Hand off to `meridian-implementation-assurance` when the architecture change is being implemented or certified.

## Validation

- Run `python .codex/skills/meridian-code-architecture/scripts/architecture_surface.py --path <path> --summary`.
- Run `python .codex/skills/meridian-code-architecture/scripts/run_evals.py --all --dry-run --summary` after editing this skill.
- For Codex catalog changes, run `python build/scripts/docs/check-codex-skills.py --summary` and
  `python build/scripts/docs/check-ai-inventory.py --summary`.
- For source dependency changes, add the narrow project build/test command that covers the touched module.

## Automation Scripts

- `scripts/architecture_surface.py` emits project, dependency, source-doc, module-map, and boundary evidence.
- `scripts/run_evals.py` runs deterministic architecture fixtures by default and only runs live
  Codex traces with `--live-run`.
- `scripts/score_eval.py` scores architecture assessments against boundary, evidence, validation,
  docs, and traceability criteria.

## Output Standards

- Name the affected projects, layers, public seams, source docs, and ADRs inspected.
- Separate verified repository facts from architecture recommendations.
- List boundary risks before implementation suggestions.
- Include validation commands and docs/source registry updates needed for the downstream lane.
