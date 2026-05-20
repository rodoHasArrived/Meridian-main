# Agent Skills

This directory indexes Meridian's AI skill surfaces. The current project-scoped workflow is
centered on repo-local Codex skills under `.codex/skills/`, while `.agents/skills/` and
`.claude/skills/` hold portable mirrored skill packages for Agent Skills-compatible hosts.
Shared skill policy, cross-provider safety rules, and alignment checks live in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

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
| `meridian-cleanup` | Clean code and docs without changing observable behavior |
| `meridian-code-review` | Review changes for bugs, regressions, and architecture drift |
| `meridian-implementation-assurance` | Implement and verify work with explicit evidence |
| `meridian-provider-builder` | Build and extend providers with the right contracts |
| `meridian-repo-navigation` | Route large-repo tasks before deeper work |
| `meridian-roadmap-strategist` | Refresh roadmap and target-state documents |
| `meridian-simulated-user-panel` | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | Produce scenario-first Meridian tests |

Shared grounding files:

- [`.codex/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/_shared/project-context.md)
- [`.agents/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills/_shared/project-context.md)
- [`.claude/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills/_shared/project-context.md)

---

## Available Portable Skills

Portable packages are mirrored under [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills)
for Agent Skills-compatible hosts and [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills)
for Claude-compatible hosts. Keep both mirrors aligned when shared skill behavior changes.

| Skill | SKILL.md | Purpose |
| ------ | --------- | --------- |
| `meridian-archive-organizer` | [`SKILL.md`](../../../.claude/skills/meridian-archive-organizer/SKILL.md) | Archive stale files and keep the repository structure tidy |
| `meridian-blueprint` | [`SKILL.md`](../../../.claude/skills/meridian-blueprint/SKILL.md) | Turn one idea into an implementation-ready technical blueprint |
| `meridian-brainstorm` | [`SKILL.md`](../../../.claude/skills/meridian-brainstorm/SKILL.md) | Generate high-value product and architecture ideas |
| `meridian-code-review` | [`SKILL.md`](../../../.claude/skills/meridian-code-review/SKILL.md) | Apply Meridian’s 7-lens review framework |
| `meridian-implementation-assurance` | [`SKILL.md`](../../../.claude/skills/meridian-implementation-assurance/SKILL.md) | Validate completed work against requirements and evidence |
| `meridian-provider-builder` | [`SKILL.md`](../../../.claude/skills/meridian-provider-builder/SKILL.md) | Scaffold and extend providers with the right contracts and resilience patterns |
| `meridian-repo-navigation` | [`SKILL.md`](../../../.claude/skills/meridian-repo-navigation/SKILL.md) | Route large-repo tasks before deeper work |
| `meridian-roadmap-strategist` | [`SKILL.md`](../../../.claude/skills/meridian-roadmap-strategist/SKILL.md) | Refresh roadmap and target-state documents |
| `meridian-simulated-user-panel` | [`SKILL.md`](../../../.claude/skills/meridian-simulated-user-panel/SKILL.md) | Simulate realistic user panels and owner-minded product critique |
| `meridian-test-writer` | [`SKILL.md`](../../../.claude/skills/meridian-test-writer/SKILL.md) | Produce Meridian-style xUnit and FluentAssertions tests |

Code-defined provider skills may also exist, such as AI documentation maintenance helpers exposed by the local skills provider.

---

## Related Resources

| Resource | Purpose |
| ---------- | --------- |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Provider-agnostic workflow and skill/agent alignment checklist |
| [`../navigation/README.md`](../navigation/README.md) | Repo navigation workflow |
| [`../agents/README.md`](../agents/README.md) | Agent catalog |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) | Codex repo-local skills and their maintenance rules |
| [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills) | Host-neutral portable Agent Skills packages |

---

## Validation

Validate skill packaging with:

```bash
python3 build/scripts/docs/validate-skill-packages.py
python3 build/scripts/docs/check-ai-inventory.py --summary
```

---

_Last Updated: 2026-05-19_
