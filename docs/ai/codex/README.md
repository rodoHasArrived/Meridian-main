# Codex AI Workflow

This page is the Codex-specific AI workflow index for Meridian. Shared, provider-agnostic policy
still lives in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md); this page
tracks repo-local Codex skill behavior, validation, and documentation ownership.

## Active Codex Surfaces

| Surface | Purpose |
| --- | --- |
| [`.codex/config.toml`](../../../.codex/config.toml) | Repository-local Codex sandbox and approval defaults |
| [`.codex/skills/README.md`](../../../.codex/skills/README.md) | Codex skill catalog and maintenance rules |
| [`.codex/skills/_shared/project-context.md`](../../../.codex/skills/_shared/project-context.md) | Meridian project grounding used by Codex skills |
| [`.codex/skills/_shared/codex-execution-contract.md`](../../../.codex/skills/_shared/codex-execution-contract.md) | Codex-only execution gates for concurrency, validation, docs sync, and response shape |
| [`.codex/skills/*/agents/openai.yaml`](../../../.codex/skills) | Codex UI metadata for repo-local skills |

## Current Codex Skills

| Skill | Purpose |
| --- | --- |
| `meridian-archive-organizer` | Archive stale code/docs and keep the repository structure tidy |
| `meridian-blueprint` | Create implementation-ready Meridian technical blueprints |
| `meridian-brainstorm` | Generate Meridian-native product and architecture ideas |
| `meridian-cleanup` | Clean up code and docs without behavior changes |
| `meridian-code-review` | Review changes for bugs, regressions, and architecture drift |
| `meridian-implementation-assurance` | Implement and verify changes with strict Codex gates, explicit evidence, and docs sync |
| `meridian-provider-builder` | Build and extend provider integrations |
| `meridian-repo-navigation` | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | Refresh roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel` | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | Write scenario-first Meridian tests |

## Required Gates For Codex AI/Tooling Changes

Run or account for these gates when Codex skill, catalog, prompt, docs automation, or AI workflow
behavior changes:

```bash
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
make ai-verify
make ai-arch-check
```

If `make` is unavailable in the local Windows shell, run the target's underlying command directly
and report that the wrapper could not be invoked.

## Tooling Split

Required quality gates:

- `make ai-verify`
- `make ai-arch-check`
- CI step `Validate AI contract drift` in `.github/workflows/ci.yml`

Advisory tooling:

- `make ai-audit*`
- `make ai-report`
- `make ai-docs-freshness`
- `make ai-docs-drift`
- `make ai-docs-sync-report`
- `make ai-arch-check-summary`
- `make ai-arch-check-json`

Maintenance/reporting:

- `make ai-maintenance-light`
- `make ai-maintenance-full`
- `make ai-docs-archive`
- `make ai-docs-archive-execute`

## Maintenance Rules

- Keep Codex-only guidance in `.codex/skills/` and this `docs/ai/codex/` index.
- Update shared Claude, GitHub, or portable skill surfaces only when the requested change is
  explicitly cross-provider.
- Keep every current Codex skill linked to `_shared/project-context.md` and
  `_shared/codex-execution-contract.md`.
- Keep `agents/openai.yaml` present for every current Codex skill.
- Skip purposeless cosmetic churn unless it fixes canonical naming, broken docs, accessibility,
  lint/test failures, API contract names, or user-visible correctness.

## Validation

Use the Codex skill checker for fast local drift detection:

```bash
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-codex-skills.py --json-output docs/generated/codex-skills-check.json
```
