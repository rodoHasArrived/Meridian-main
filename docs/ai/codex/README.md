# Codex AI Workflow

This page is the Codex-specific AI workflow index for Meridian. Shared, provider-agnostic policy
still lives in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md); this page
tracks repo-local Codex skill behavior, validation, and documentation ownership.

## Active Codex Surfaces

| Surface | Purpose |
| --- | --- |
| [`.codex/config.toml`](../../../.codex/config.toml) | Repository-local Codex sandbox and approval defaults |
| [`.codex/agents/`](../../../.codex/agents) | Codex specialist agent-profile TOML files that route recurring documentation, cleanup, roadmap, navigation, and user-panel work |
| [`.codex/skills/README.md`](../../../.codex/skills/README.md) | Codex skill catalog and maintenance rules |
| [`.codex/skills/_shared/project-context.md`](../../../.codex/skills/_shared/project-context.md) | Meridian project grounding used by Codex skills |
| [`.codex/skills/_shared/codex-execution-contract.md`](../../../.codex/skills/_shared/codex-execution-contract.md) | Codex-only execution gates for concurrency, validation, docs sync, and response shape |
| [`.codex/skills/*/agents/openai.yaml`](../../../.codex/skills) | Codex UI metadata for repo-local skills |
| [`.codex/AGENTS.md`](../../../.codex/AGENTS.md) | Codex-specific desktop workstation implementation rules |
| [`.codex/prompts/`](../../../.codex/prompts) | Reusable desktop implementation, refactor, provider, diagnostics, resource, and test prompts |
| [`.codex/checklists/`](../../../.codex/checklists) | Modularity, MVVM, resource, definition-of-done, and safe-refactor checklists |
| [`tools/codex/`](../../../tools/codex) | Codex-focused PowerShell quality scans, desktop workspace generators, resource reviews, and refactor-plan helpers |

## Current Codex Skills

| Skill | Purpose |
| --- | --- |
| `meridian-archive-organizer` | Archive stale code/docs and keep the repository structure tidy |
| `meridian-blueprint` | Create implementation-ready Meridian technical blueprints |
| `meridian-brainstorm` | Generate Meridian-native product and architecture ideas |
| `meridian-browser-workstation` | Route and implement browser workstation TypeScript/React tasks |
| `meridian-cleanup` | Clean up code and docs without behavior changes |
| `meridian-code-review` | Review changes for bugs, regressions, and architecture drift |
| `meridian-docs` | Maintain Meridian documentation with repo-grounded evidence |
| `meridian-implementation-assurance` | Implement and verify changes with strict Codex gates, explicit evidence, and docs sync |
| `meridian-provider-builder` | Build and extend provider integrations |
| `meridian-repo-navigation` | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | Refresh roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel` | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | Write scenario-first Meridian tests |
| `modular-desktop-mvvm` | Implement modular WPF MVVM workstation changes |
| `workstation-screen-composition` | Compose desktop screens from shared workstation primitives |
| `shared-component-extraction` | Extract repeated desktop patterns into reusable components |
| `provider-management-workflow` | Build secure provider setup, health, credential, and recovery workflows |
| `research-data-acquisition` | Build research acquisition, preview, validation, and lineage workflows |
| `dense-data-grid-inspector-panel` | Build scalable dense grids and inspector panels |
| `diagnostics-audit-timeline` | Build diagnostics panels, audit timelines, and evidence trails |
| `performance-resource-review` | Review memory, CPU, I/O, rendering, concurrency, and lifecycle risks |
| `safe-refactoring` | Refactor desktop code incrementally without behavior drift |
| `desktop-test-generation` | Generate focused WPF view-model, command, service, and binding tests |

## Routing Model

Use `.codex/skills/` as the canonical repo-local Codex skill set. Keep public skill names stable and
route by task phase:

| Lane | Skill | Boundary |
| --- | --- | --- |
| Orient | `meridian-repo-navigation` | Route first, name owner files/docs, then exit. |
| Ideate | `meridian-brainstorm` | Generate product or architecture options, not a build spec. |
| Plan | `meridian-blueprint` | Turn one selected idea into an implementation-ready design. |
| Implement or verify | `meridian-implementation-assurance` | Build or certify work with evidence, docs sync, and gates. |
| Review | `meridian-code-review` | Findings first; no patching unless explicitly requested. |
| Docs | `meridian-docs` | Update docs, guidance, and indexes with current repo evidence. |
| Test | `meridian-test-writer` | Add scenario-first tests in the correct test lane. |
| Provider | `meridian-provider-builder` | Build providers and hand off to assurance for rollout proof. |
| Archive | `meridian-archive-organizer` | Classify stale material and update archive/navigation evidence. |
| Roadmap | `meridian-roadmap-strategist` | Reconcile status, waves, opportunities, and target state. |
| Cleanup | `meridian-cleanup` | Preserve behavior while removing dead code or duplication. |
| Simulated user review | `meridian-simulated-user-panel` | Critique concrete artifacts with personas and explicit evidence. |
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

## Required Gates For Codex AI/Tooling Changes

Run or account for these gates when Codex skill, catalog, prompt, docs automation, or AI workflow
behavior changes:

```bash
make ai-codex-skills-check
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --json
git diff --check
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

Local helper surfaces:

- `scripts/ai/` backs the make-based AI setup, cleanup, routing, and maintenance lanes.
- `tools/codex/` holds Codex-focused PowerShell scanners and generators used for desktop quality
  reports and reviewable implementation planning.

## Maintenance Rules

- Keep Codex-only guidance in `.codex/skills/` and this `docs/ai/codex/` index.
- Before editing `src/**`, read the nearest registered source README and identify the module in
  `docs/source/data/source-modules.yml`.
- For meaningful source behavior, workflow, validation, diagram, ownership, or TODO changes, update
  the nearest source README plus `docs/source/data/*.yml` records in the same change.
- Use `python3 build/scripts/docs/mark-stale-docs.py --write --summary` to create the source-doc
  update queue, then use stale-only sync/render commands when only outdated docs should change.
- Use `python3 build/scripts/docs/validate-doc-hashes.py --summary` to detect source/docs drift.
  Use `--write` only after reviewing or updating the nearest README, registry, generated blocks,
  and hash baseline.
- Add optional source README sections only when relevant: plans, end-user value, benchmarks and
  performance, operational evidence, security/credentials, API/contracts, or migration/archive
  notes.
- Never hand-edit generated docs under `docs/roadmap/generated/` or `docs/source/generated/`;
  update registry data or renderers and regenerate.
- Update shared Claude, GitHub, or portable skill surfaces only when the requested change is
  explicitly cross-provider.
- Keep every current Codex skill linked to `_shared/project-context.md` and
  `_shared/codex-execution-contract.md`.
- Keep every current Codex skill structured around `Use When`, `Do Not Use When`, `Workflow`,
  `Handoffs`, `Validation`, and `Output Standards`, with trigger and non-trigger examples.
- Keep `agents/openai.yaml` present for every current Codex skill.
- Keep `.agents/skills/` and `.claude/skills/` as host-neutral mirrors; do not widen Codex-only
  structure changes into portable packages unless the workflow itself is shared.
- Skip purposeless cosmetic churn unless it fixes canonical naming, broken docs, accessibility,
  lint/test failures, API contract names, or user-visible correctness.

## Validation

Use the Codex skill checker for fast local drift detection:

```bash
make ai-codex-skills-check
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-codex-skills.py --json-output docs/generated/codex-skills-check.json
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/validate-source-readmes.py --summary
python3 build/scripts/docs/scan-source-todos.py --summary
python3 build/scripts/docs/mark-stale-docs.py --write --summary
python3 build/scripts/docs/validate-doc-hashes.py --summary
```

Use `python3 build/scripts/docs/check-ai-inventory.py --summary` after adding, renaming, or
removing Codex agent profiles, prompts, validation checklists, `.codex/AGENTS.md`, environment
configs, skills, OpenAI metadata, or `tools/codex/` quality tools so the shared AI contract and
this index stay aligned.
