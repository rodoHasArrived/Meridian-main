# Agent Skills

This directory indexes Meridian's AI skill surfaces. The current project-scoped workflow is
centered on repo-local Codex skills under `.codex/skills/`, while `.agents/skills/` and
`.claude/skills/` hold portable mirrored skill packages for Agent Skills-compatible hosts.
Shared skill policy, cross-provider safety rules, and alignment checks live in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).
Codex-specific execution gates, current skill validation, and repo-local skill maintenance live in
[`../codex/README.md`](../codex/README.md).
Treat `.codex/skills/` as the execution source for repo-local Codex package structure, evals, scripts, agent profiles, and route coverage. Mirror only host-neutral workflow behavior into `.agents/skills/` and `.claude/skills/`; do not copy Codex-only eval/script package surfaces into portable mirrors unless a host-neutral package contract is explicitly added.

When a skill lane needs validator, route, handoff-packet, or maintenance-script selection, load
[`../tooling/README.md`](../tooling/README.md) before widening repository context or repeating
discovery.

---

## Package Contract

Each portable skill package follows this shape:

```text
<skill-name>/
├── SKILL.md
├── scripts/      # optional deterministic helpers
├── references/   # optional supporting docs
├── assets/       # optional templates or static resources
└── ...
```

---

## Current Codex Skills

These repo-local skills are the primary Meridian skill set for current AI work:

| Skill | Purpose |
| ------ | --------- |
| `meridian-archive-organizer` | Archive stale files and keep the repository structure tidy |
| `meridian-blueprint` | Turn one idea into an implementation-ready technical blueprint |
| `meridian-brainstorm` | Generate Meridian-native product and architecture ideas |
| `meridian-browser-workstation` | Implement and review browser workstation TypeScript/React changes |
| `meridian-cleanup` | Clean code and docs without changing observable behavior |
| `meridian-code-architecture` | Review architecture conformance, module boundaries, dependencies, and ADR/source-doc alignment |
| `meridian-code-review` | Review changes for bugs, regressions, and architecture drift |
| `meridian-contract-governance` | Trace shared contract impact across services, UI surfaces, tests, and docs |
| `meridian-accounting-posting-controls` | Review accounting posting gates, approval, period locks, idempotency, and reversal/rebook safeguards |
| `meridian-event-accounting-architecture` | Design event-based accounting architecture, immutable journals, ledger projections, and evidence-backed controls |
| `meridian-ledger-projection-replay-review` | Review ledger projection, replay ordering, rebuild, versioning, and report handoff risk |
| `meridian-codex-skill-builder` | Package Codex skills with scripts, evals, profiles, catalogs, and route coverage |
| `meridian-docs` | Maintain Meridian documentation with repo-grounded evidence |
| `meridian-implementation-assurance` | Implement, certify, and improve work with scope control, requirement-to-evidence traceability, explicit validation, and docs sync |
| `meridian-provider-builder` | Build and extend providers with the right contracts |
| `meridian-repo-navigation` | Route large-repo tasks before deeper work |
| `meridian-roadmap-strategist` | Refresh roadmap and target-state documents |
| `meridian-simulated-user-panel` | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | Produce scenario-first Meridian tests |
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

Shared grounding files:

- [`.codex/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/_shared/project-context.md)
- [`.codex/skills/_shared/codex-execution-contract.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/_shared/codex-execution-contract.md)
- [`.agents/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills/_shared/project-context.md)
- [`.claude/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills/_shared/project-context.md)
- [docs/ai/agent-handoff-checklist.md](../agent-handoff-checklist.md)

## AI Contract Coverage

- Repo navigation: begin with `../navigation/README.md`, then `../generated/repo-navigation.md`
  before selecting skills for a specific subsystem.
- Agent edit rules: prefer repository policy in `../assistant-workflow-contract.md`; keep this index
  as discoverability, not duplicated policy text.
- Generated-file handling: when updating generation scripts or indexes that produce generated AI docs,
  change the generator/input and run regeneration commands in `../assistant-workflow-contract.md`.
- Agent orchestration: use `../parallel-task-manifest-template.md` and `../agent-handoff-checklist.md`
  for coordinated skill usage across >1 lane.
- Tooling and validators: use [`../tooling/README.md`](../tooling/README.md) to choose the narrowest
  script, validator, route artifact, or maintenance lane for the skill batch.
- Parallel workflow: disambiguate ownership by lane and keep touched skill surfaces non-overlapping.
- Token/context management: use `../work-modes.md`, keep required vs optional context explicit in
  handoff packets, and record validation reuse or rerun triggers before switching lanes.
- Validation: `check-ai-inventory`, `check-codex-skills`, Codex
  `skill_package_audit.py --skill <skill>`, portable/Claude `validate-skill-packages`,
  `check-ai-handoff --strict`, and `git diff --check` for skills/docs-only batches.
- Documentation ownership: [`../../documentation-ownership.md`](../../documentation-ownership.md)

---

## Available Portable Skills

Portable packages are mirrored under [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills)
for Agent Skills-compatible hosts and [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills)
for Claude-compatible hosts. Keep both mirrors aligned when shared skill behavior changes, but keep Codex-only package evals, scripts, profile wiring, and route rules in `.codex/skills/`.

| Skill | SKILL.md | Purpose |
| ------ | --------- | --------- |
| `meridian-archive-organizer` | [`SKILL.md`](../../../.claude/skills/meridian-archive-organizer/SKILL.md) | Archive stale files and keep the repository structure tidy |
| `meridian-blueprint` | [`SKILL.md`](../../../.claude/skills/meridian-blueprint/SKILL.md) | Turn one idea into an implementation-ready technical blueprint |
| `meridian-brainstorm` | [`SKILL.md`](../../../.claude/skills/meridian-brainstorm/SKILL.md) | Generate high-value product and architecture ideas |
| `meridian-browser-workstation` | [`SKILL.md`](../../../.claude/skills/meridian-browser-workstation/SKILL.md) | Route and implement browser workstation TypeScript/React work |
| `meridian-cleanup` | [`SKILL.md`](../../../.claude/skills/meridian-cleanup/SKILL.md) | Clean up code and docs without changing observable behavior |
| `meridian-code-review` | [`SKILL.md`](../../../.claude/skills/meridian-code-review/SKILL.md) | Apply Meridian’s 7-lens review framework |
| `meridian-docs` | [`SKILL.md`](../../../.claude/skills/meridian-docs/SKILL.md) | Maintain documentation accurately and conservatively |
| `meridian-implementation-assurance` | [`SKILL.md`](../../../.claude/skills/meridian-implementation-assurance/SKILL.md) | Validate completed work against requirements and evidence |
| `meridian-provider-builder` | [`SKILL.md`](../../../.claude/skills/meridian-provider-builder/SKILL.md) | Scaffold and extend providers with the right contracts and resilience patterns |
| `meridian-repo-navigation` | [`SKILL.md`](../../../.claude/skills/meridian-repo-navigation/SKILL.md) | Route large-repo tasks before deeper work |
| `meridian-roadmap-strategist` | [`SKILL.md`](../../../.claude/skills/meridian-roadmap-strategist/SKILL.md) | Refresh roadmap and target-state documents |
| `meridian-simulated-user-panel` | [`SKILL.md`](../../../.claude/skills/meridian-simulated-user-panel/SKILL.md) | Simulate realistic user panels and owner-minded product critique |
| `meridian-test-writer` | [`SKILL.md`](../../../.claude/skills/meridian-test-writer/SKILL.md) | Produce Meridian-style xUnit and FluentAssertions tests |

Code-defined provider skills may also exist, such as AI documentation maintenance helpers exposed by the local skills provider.

---

## Claude Plugin Skill Packages

Checked-in Claude plugins live under [`.claude/plugins/`](../../../.claude/plugins). Treat them as
provider-specific helper packages, not replacements for the shared Meridian workflow contract.

| Plugin | Manifest | Skill folders | Role |
| ------ | -------- | ------------- | ---- |
| `csharp-dotnet-development` | [`.claude/plugins/csharp-dotnet-development/.github/plugin/plugin.json`](../../../.claude/plugins/csharp-dotnet-development/.github/plugin/plugin.json) | `aspnet-minimal-api-openapi`, `csharp-async`, `csharp-mstest`, `csharp-nunit`, `csharp-tunit`, `csharp-xunit`, `dotnet-best-practices`, `dotnet-upgrade` | General C#/.NET helper skills for async, tests, OpenAPI, best practices, and upgrades |
| `frontend-web-dev` | [`.claude/plugins/frontend-web-dev/.github/plugin/plugin.json`](../../../.claude/plugins/frontend-web-dev/.github/plugin/plugin.json) | `playwright-explore-website`, `playwright-generate-test` | General frontend and Playwright helper skills |

When a plugin package is added, renamed, or removed, update this table, the plugin-agent section in
[`../agents/README.md`](../agents/README.md), and the shared inventory contract in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

---

## Related Resources

| Resource | Purpose |
| ---------- | --------- |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Provider-agnostic workflow and skill/agent alignment checklist |
| [`../codex/README.md`](../codex/README.md) | Codex repo-local skill workflow and validation gates |
| [`../navigation/README.md`](../navigation/README.md) | Repo navigation workflow |
| [`../agents/README.md`](../agents/README.md) | Agent catalog |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) | Codex repo-local skills and their maintenance rules |
| [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills) | Host-neutral portable Agent Skills packages |

---

## Validation

Validate skill packaging with:

```bash
python3 build/scripts/docs/check-codex-skills.py --summary
python3 .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill <skill> --summary
python3 build/scripts/docs/validate-skill-packages.py
python3 build/scripts/docs/check-ai-inventory.py --summary
```

Use `skill_package_audit.py --skill <skill>` for repo-local Codex package completeness.
Use `validate-skill-packages.py` for portable Agent Skills and Claude mirror packages unless that
script is explicitly changed to cover `.codex/skills/`.

---

_Last Updated: 2026-06-19_
