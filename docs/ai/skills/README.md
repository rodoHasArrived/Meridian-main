# Agent Skills

This directory indexes Meridian's portable Agent Skills packages. These packages live under `.claude/skills/` and are designed for progressive disclosure: advertise the skill, load `SKILL.md` only when needed, then read references or run scripts on demand.

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

## Available Portable Skills

| Skill | Purpose |
|------|---------|
| `meridian-blueprint` | Turn one idea into an implementation-ready technical blueprint |
| `meridian-brainstorm` | Generate high-value product and architecture ideas |
| `meridian-code-review` | Apply Meridian’s 7-lens review framework |
| `meridian-implementation-assurance` | Validate completed work against requirements and evidence |
| `meridian-provider-builder` | Scaffold and extend providers with the right contracts and resilience patterns |
| `meridian-test-writer` | Produce Meridian-style xUnit and FluentAssertions tests |

Code-defined provider skills may also exist, such as AI documentation maintenance helpers exposed by the local skills provider.

---

## Related Resources

| Resource | Purpose |
|----------|---------|
| [`../README.md`](../README.md) | Master AI resource index |
| [`../navigation/README.md`](../navigation/README.md) | Repo navigation workflow |
| [`../agents/README.md`](../agents/README.md) | Agent catalog |
| [`../../../.codex/skills/README.md`](../../../.codex/skills/README.md) | Codex repo-local skills, including `meridian-repo-navigation` |

---

## Validation

Validate skill packaging with:

```bash
python3 build/scripts/docs/validate-skill-packages.py
```

---

*Last Updated: 2026-03-31*
