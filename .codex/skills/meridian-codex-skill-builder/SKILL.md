---
name: meridian-codex-skill-builder
description: Create, audit, package, and validate Meridian repo-local Codex skills with SKILL.md guidance, OpenAI metadata, deterministic scripts, eval fixtures, Codex agent profiles, catalog entries, and prompt-route coverage. Use when Codex is adding or improving Meridian Codex skills, eval rubrics, skill-owned scripts, or skill discovery.
---

# Meridian Codex Skill Builder

Build Codex-only Meridian skills as validated packages, not loose prompt files.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before changing
skills, agent profiles, route rules, or Codex catalogs. Read `references/skill-package-checklist.md`
before creating a new skill or changing eval/script packaging.

## Use When

Use this skill when adding, auditing, or improving repo-local Codex skills under `.codex/skills/`,
their `agents/openai.yaml` metadata, deterministic scripts, eval fixtures, Codex agent profiles,
catalog entries, or prompt-route rules.

Trigger examples:

- "Use $meridian-codex-skill-builder to add a full Codex skill with scripts and evals."
- "Audit this skill package for missing metadata, route coverage, and dry-run evals."
- "Add a skill-owned helper script and update the eval baseline."

## Do Not Use When

Use the system `$skill-creator` instructions for generic skill-format rules. Use
`meridian-implementation-assurance` when the work is broader implementation certification, and
`meridian-docs` when the only deliverable is documentation wording.

Non-trigger examples:

- "Explain how Agent Skills work in general."
- "Certify this provider implementation."
- "Fix a typo in one Codex README."

## Workflow

1. Confirm the skill is Codex-only or shared across hosts. For Codex-only work, do not update
   `.agents/skills/` or `.claude/skills/`.
2. Run the system `skill-creator` initializer for new skills, then replace placeholders.
3. Add `SKILL.md`, `agents/openai.yaml`, skill-owned `references/`, deterministic `scripts/`, and `evals/`.
4. Add or update `.codex/agents/<skill>.toml`, `.codex/config.toml`, `.codex/skills/README.md`,
   `docs/ai/codex/README.md`, `docs/ai/skills/README.md`, and prompt-route rules when discovery changes.
5. Run `scripts/skill_package_audit.py --skill <skill> --summary` and the owning skill evals.
6. Promote only after package, catalog, route, and dry-run eval checks pass or residual gaps are explicit.

## Handoffs

- Hand off to `meridian-implementation-assurance` for final rollout evidence.
- Hand off to `meridian-docs` when catalog/index wording becomes the main deliverable.
- Hand off to the owning specialist skill when the package audit finds domain-specific content gaps.

## Validation

- Run `python .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill <skill> --summary`.
- Run `python .codex/skills/meridian-codex-skill-builder/scripts/run_evals.py --all --dry-run --summary`.
- Run `python build/scripts/docs/check-codex-skills.py --summary`.
- Run `python build/scripts/docs/check-ai-inventory.py --summary`.
- Run `python build/scripts/docs/prompt-route-linter.py --summary` when route rules change.

## Automation Scripts

- `scripts/skill_package_audit.py` validates Codex skill package, eval, script, profile, catalog, and route coverage.
- `scripts/run_evals.py` runs deterministic package-audit fixtures by default and only runs live
  Codex traces with `--live-run`.
- `scripts/score_eval.py` scores skill-builder work for package structure, eval quality, script
  quality, catalog/route coverage, and traceability.

## Output Standards

- Report package surfaces changed: skill, scripts, evals, profile, config, catalogs, and routes.
- Distinguish Codex-only changes from host-neutral mirrored changes.
- Include the package audit, dry-run evals, Codex skill checker, AI inventory checker, route linter,
  and residual package risks.
