# Agent Skills

This directory is the navigation index for **Meridian's portable Agent Skills packages**. Each
skill is a self-contained package of instructions, optional scripts, and optional reference
resources rooted under `.claude/skills/`, with a required `SKILL.md` entry point that follows the
open Agent Skills-style frontmatter + markdown pattern.

Meridian keeps the packages portable on purpose: the same skill directory can be discovered by
Claude-oriented tooling today and reused by other Agent Skills-compatible hosts that support the
same progressive disclosure workflow.

---

## Portable Package Contract

Every Meridian skill package is expected to follow this structure:

```text
<skill-name>/
├── SKILL.md              # Required: YAML frontmatter + concise workflow instructions
├── scripts/              # Optional: deterministic helpers agents can execute
├── references/           # Optional: docs/resources loaded only when needed
├── assets/               # Optional: static templates or output resources
└── ...                   # Optional: evals, graders, host-specific metadata, etc.
```

### SKILL.md Frontmatter

Meridian skill packages now use portable metadata fields that map cleanly onto the open skill
specification:

| Field | Purpose |
| ----- | ------- |
| `name` | Stable package name; must match the skill directory |
| `description` | Trigger guidance for hosts that advertise skills up front |
| `license` | Repository license reference for downstream packaging |
| `compatibility` | Host/runtime expectations |
| `metadata` | Package owner, version, and spec tagging |

---

## Progressive Disclosure Model

Meridian skills are authored to support the standard three-stage loading pattern:

1. **Advertise** — The host injects each skill's `name` and `description` so agents know the
   package exists.
2. **Load** — When the task matches, the host loads the full `SKILL.md` instructions.
3. **Read resources / run scripts as needed** — The host reads `references/` files or executes
   `scripts/` only when the task actually needs that deeper context or determinism.

This keeps the default context lean while preserving access to Meridian-specific architecture,
provider patterns, grading assets, and helper automation.

### Host Tools Meridian Skills Expect

Meridian's skill packages are written to work best in hosts that expose the following primitives:

- `load_skill` — load the full `SKILL.md`
- `read_skill_resource` — fetch a referenced file only when needed
- `run_skill_script` — execute deterministic helpers when the package bundles scripts

The repository's code-defined provider at
[`.claude/skills/skills_provider.py`](../../../.claude/skills/skills_provider.py) implements this
model for local use and keeps dynamic resources such as git context or project stats separate from
static package content.

---

## Available Skill Packages

### `meridian-blueprint`

**Location:** [`.claude/skills/meridian-blueprint/`](../../../.claude/skills/meridian-blueprint/)
**Purpose:** Turn one prioritized idea into a code-ready technical blueprint for Meridian.
**When it triggers:** design-doc requests, architecture spikes, interface planning, or roadmap-to-implementation handoffs.
**On-demand resources:**

- `references/blueprint-patterns.md` — reusable section patterns and implementation shapes
- `references/pipeline-position.md` — pipeline-stage and handoff guidance
- `../_shared/project-context.md` — canonical project statistics, paths, and ADR anchors

### `meridian-code-review`

**Location:** [`.claude/skills/meridian-code-review/`](../../../.claude/skills/meridian-code-review/)
**Purpose:** Apply Meridian's 7-lens architecture and code quality review framework.
**When it triggers:** code review, refactoring, architecture audit, MVVM cleanup, provider compliance, or performance review tasks.
**On-demand resources and scripts:**

- `references/architecture.md` — deep architecture context
- `references/schemas.md` — eval/grading schemas
- `agents/grader.md` — grading instructions for eval runs
- `scripts/run_eval.py`, `scripts/aggregate_benchmark.py`, `scripts/package_skill.py` — deterministic review helpers
- Dynamic resources via the provider: `project-stats`, `git-context`

### `meridian-brainstorm`

**Location:** [`.claude/skills/meridian-brainstorm/`](../../../.claude/skills/meridian-brainstorm/)
**Purpose:** Generate high-value, implementable ideas that extend Meridian coherently.
**When it triggers:** feature ideation, user-pain brainstorming, architecture brainstorms, or technical-debt ideation.
**On-demand resources:**

- `references/idea-dimensions.md` — evaluation rubric
- `references/competitive-landscape.md` — external framing and differentiation context
- `brainstorm-history.jsonl` — optional local continuity ledger when the host permits writes

### `meridian-provider-builder`

**Location:** [`.claude/skills/meridian-provider-builder/`](../../../.claude/skills/meridian-provider-builder/)
**Purpose:** Scaffold Meridian data providers with the right ADR, DI, and resilience patterns.
**When it triggers:** new exchange/provider work, historical providers, streaming adapters, or symbol search implementations.
**On-demand resources:**

- `references/provider-patterns.md` — provider skeletons, options, DI wiring, and test scaffolds
- Companion skills referenced as needed: `meridian-code-review`, `meridian-test-writer`

### `meridian-test-writer`

**Location:** [`.claude/skills/meridian-test-writer/`](../../../.claude/skills/meridian-test-writer/)
**Purpose:** Produce idiomatic Meridian xUnit tests with the right async, mocking, and cleanup patterns.
**When it triggers:** new tests, test-gap remediation, or code-review follow-up for missing coverage.
**On-demand resources:**

- `references/test-patterns.md` — component-specific test scaffolds and decision trees
- Companion skill referenced as needed: `meridian-code-review`

### `ai-docs-maintain` (code-defined)

**Registered in:** [`.claude/skills/skills_provider.py`](../../../.claude/skills/skills_provider.py)

`ai-docs-maintain` is intentionally code-defined rather than file-backed. It still behaves like a
skill package from the host's perspective, but its resources and scripts are surfaced directly from
Python so documentation freshness and drift checks can stay live.

Available scripts:

| Script | Purpose |
| ------ | ------- |
| `run-freshness` | Check AI doc staleness |
| `run-drift` | Detect divergence between docs and code reality |
| `run-full` | Run freshness, drift, reference, and archive checks |
| `run-archive` | Preview or execute stale-doc archiving |

Available resource:

- `doc-health-summary` — live doc-health snapshot

---

## Validation

Use the repository validator to confirm each file-backed skill still looks like a portable package:

```bash
python3 build/scripts/docs/validate-skill-packages.py
```

The validator checks for:

- required `SKILL.md` frontmatter fences
- `name`/directory alignment and portable naming rules
- required `description`, `license`, `compatibility`, and `metadata` fields
- `SKILL.md` body line-count guardrail (500 lines)
- progressive-disclosure references when `references/`, `scripts/`, or `assets/` directories exist

---

## Related Resources

| Resource | Purpose |
| -------- | ------- |
| [`docs/ai/README.md`](../../archive/docs/README.md) | Master AI resource index |
| [`docs/ai/agents/README.md`](../agents/README.md) | GitHub/Copilot agent equivalents |
| [`CLAUDE.md`](../../../CLAUDE.md) | Root project context and repo conventions |
| [`.claude/skills/skills_provider.py`](../../../.claude/skills/skills_provider.py) | Local skills provider implementation |

---

_Last Updated: 2026-03-20_
