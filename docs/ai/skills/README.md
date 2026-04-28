# Agent Skills

This directory indexes Meridian's AI skill surfaces. The current project-scoped workflow is
centered on repo-local Codex skills under `.codex/skills/`, while `.claude/skills/` holds the
portable mirrored skill packages for hosts that consume Claude-style Agent Skills.
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

The shared Codex grounding file is [`.codex/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/_shared/project-context.md).

---

## Available Portable Skills

| Skill | Purpose |
| ------ | --------- |
| `meridian-archive-organizer` | Archive stale files and keep the repository structure tidy |
| `meridian-blueprint` | Turn one idea into an implementation-ready technical blueprint |
| `meridian-brainstorm` | Generate high-value product and architecture ideas |
| `meridian-code-review` | Apply Meridian’s 7-lens review framework |
| `meridian-implementation-assurance` | Validate completed work against requirements and evidence |
| `meridian-provider-builder` | Scaffold and extend providers with the right contracts and resilience patterns |
| `meridian-repo-navigation` | Route large-repo tasks before deeper work |
| `meridian-roadmap-strategist` | Refresh roadmap and target-state documents |
| `meridian-simulated-user-panel` | Simulate realistic user panels and owner-minded product critique |
| `meridian-test-writer` | Produce Meridian-style xUnit and FluentAssertions tests |

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

---

## Validation

Validate skill packaging with:

```bash
python3 build/scripts/docs/validate-skill-packages.py
python3 build/scripts/docs/check-ai-inventory.py --summary
```

---

_Last Updated: 2026-04-28_
