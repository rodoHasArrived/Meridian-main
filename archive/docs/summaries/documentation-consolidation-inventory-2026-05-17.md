# Documentation Consolidation Inventory - 2026-05-17

**Status:** archived
**Scope:** documentation, prompts, automation notes, design notes, architecture notes, and developer guidance
**Current path:** `C:\Dev\Meridian-main`

This inventory records the consolidation pass that created the maintained
front-door documentation structure. It is historical evidence, not active
developer guidance.

## Inventory Summary

| Category | Findings |
| --- | --- |
| Documentation files | Active hand-authored docs were spread across `docs/architecture`, `docs/development`, `docs/operations`, `docs/plans`, `docs/status`, `docs/ai`, `docs/evaluations`, and `docs/audits` |
| Prompt files | Prompt and agent guidance was split across `.github/prompts`, `.github/agents`, `.github/instructions`, `.github/copilot-instructions.md`, `.agents/skills`, `.codex/skills`, and `docs/ai` |
| Automation files | CI and local automation lived in `.github/workflows`, `build/scripts`, `scripts/dev`, `scripts/ai`, `make`, and `deploy` |
| Design notes | Design-system guidance existed in `Meridian Design System/`, `docs/reference/design-review-memo.md`, `docs/ui/components.md`, and scattered WPF/browser notes |
| Architecture notes | Architecture guidance was split across `docs/architecture`, `docs/adr`, `docs/development/repository-organization-guide.md`, and several plan/evaluation files |
| Setup/build/publish instructions | Command guidance was duplicated across `README.md`, `AGENTS.md`, `CLAUDE.md`, `docs/HELP.md`, `docs/development`, installer docs, publish scripts, and generated status docs |

## Consolidated Entry Points Created

- `docs/architecture/project-structure.md`
- `docs/architecture/module-map.md`
- `docs/architecture/mvvm-guidelines.md`
- `docs/developer/setup.md`
- `docs/developer/build-test-run.md`
- `docs/developer/publish-standalone-exe.md`
- `docs/operations/cleanup-and-maintenance.md`
- `docs/operations/disk-space-hygiene.md`
- `docs/design/design-system-usage.md`
- `docs/prompts/automation-prompts.md`
- `docs/prompts/repo-maintenance-prompts.md`

## Duplicates And Contradictions Resolved

- Root `README.md` no longer carries a volatile generated repository tree.
- Active local-path guidance now points at `C:\Dev\Meridian-main`.
- Developer commands are routed to `docs/developer/` instead of being repeated
  in every planning or AI-guidance file.
- Prompt and agent guidance now has a docs-level catalog under `docs/prompts/`.
- Docs cleanup guidance now recognizes `docs/archive/` as the docs-tree archive
  for this consolidation pass.

## Remaining Gaps

- Legacy root archive references under `archive/docs/` still exist and should be
  migrated only with a focused link/index pass.
- Some generated status and audit reports still represent point-in-time output.
  Regenerate them from `C:\Dev\Meridian-main` before treating them as current
  evidence.
- Several long-form evaluation and plan files still contain useful rationale.
  They should be archived only when replacement links and status summaries are
  updated in the same change.
