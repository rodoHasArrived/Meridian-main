# Codex Memory System

This document defines the intended Meridian-local Codex memory model. It describes how future
memory tooling should classify, store, load, promote, refresh, and invalidate reusable agent context
without making every task depend on all previously observed information.

The system is designed for repository-local memory under `.codex/memory/`. User-level and global
memory tiers are included in the model for forward compatibility, but they are not enabled by
default and must not be read or written unless an operator explicitly opts in.

## 1. Purpose and non-goals

### Purpose

The Codex memory system should:

- Preserve high-value task context that is expensive to rediscover, such as validated repository
  conventions, recurring command failures, branch-specific migration notes, and accepted decisions.
- Load only the memory that is relevant to the current user intent, selected skill, changed paths,
  branch, or explicit tags.
- Keep memory auditable by requiring source references, confidence, freshness, review dates, and
  invalidation triggers.
- Make promotion deliberate so short-lived observations do not become permanent repo guidance
  without review.
- Detect stale, conflicting, or superseded memories before they silently steer implementation.
- Keep Meridian source-of-truth documents authoritative; memory supplements docs, code, and tests,
  but does not replace them.

### Non-goals

The memory system should not:

- Store secrets, credentials, tokens, personal data, customer data, or proprietary external content.
- Override direct user instructions, repository `AGENTS.md` instructions, canonical docs, source
  files, tests, or skill instructions.
- Load all available memory at startup.
- Turn speculative notes, unverified assumptions, or transient command output into durable guidance.
- Enable user or global memory by default.
- Use memory as a substitute for validation. Memory can suggest commands or risks, but the agent
  must still run the narrowest applicable checks for the current change.

## 2. Memory tiers

| Tier | Scope | Intended lifetime | Default loading | Typical content |
| --- | --- | --- | --- | --- |
| Ephemeral | Current reasoning turn only | Minutes | In active context only | Scratch observations, candidate commands, rejected hypotheses. |
| Session | Current Codex session | Until session ends or is summarized | Loaded only by the running agent/session | Inspected files, active assumptions, validation results, task-local decisions. |
| Branch | Current Git branch | Until branch merges, is abandoned, or review expires | Loaded when branch name matches | Branch migration notes, temporary compatibility decisions, known branch-specific failures. |
| Task | Named work item, issue, plan, or prompt family | Until task closes or review expires | Loaded when user intent, tags, or task id match | Accepted implementation plan, open follow-ups, evidence already gathered for a task. |
| Repo | Meridian repository | Durable, reviewed periodically | Loaded by path, skill, intent, or explicit tags | Stable repo conventions, recurring environment limits, validated workflow notes. |
| User | Explicit operator profile | Durable outside repo | Disabled by default | Operator preferences that are safe and intentionally shared across repos. |
| Global | Cross-user or organization-wide baseline | Durable outside repo | Disabled by default | Approved organization policies or reusable public conventions. |

Ephemeral memory is not written under `.codex/memory/`. All other enabled tiers should use the
storage layout below. User and global memory are part of the schema so future tooling can enforce
consistent validation and opt-in behavior, but Meridian-local agents must treat them as disabled
unless the operator provides an explicit enablement mechanism.

## 3. Storage layout under `.codex/memory/`

Use a small index plus reviewable Markdown entries. The proposed layout is:

```text
.codex/memory/
  README.md
  index.jsonl
  sessions/
    <session-id>/
      summary.md
      observations.jsonl
  tasks/
    <task-id>.md
  branches/
    <branch-slug>.md
  repo/
    <topic-slug>.md
  disabled/
    user/
      README.md
    global/
      README.md
```

Storage rules:

- `index.jsonl` is the lookup surface. Each line is one JSON object using the index schema in this
  document.
- Markdown files are the human-readable memory entries. They should include a short summary,
  supporting evidence, usage guidance, and explicit invalidation notes.
- `sessions/` stores session memory that has not yet been promoted. A session directory may be
  removed after useful entries are promoted or explicitly discarded.
- `tasks/`, `branches/`, and `repo/` hold promoted memory for their respective tiers.
- `disabled/user/` and `disabled/global/` document the reserved design for opt-in higher-scope
  memory. They are intentionally inactive by default.
- File names should be stable, lowercase slugs. Avoid embedding secrets, user names, or branch names
  that disclose sensitive context.

## 4. Index schema fields

Each `index.jsonl` entry should contain these fields:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string | Yes | Stable unique identifier, preferably `<tier>:<slug>` or `<tier>:<scope>:<slug>`. |
| `scope` | string | Yes | Matching boundary such as `repo`, `branch:<name>`, `task:<id>`, `session:<id>`, `user`, or `global`. |
| `tier` | string | Yes | One of `session`, `branch`, `task`, `repo`, `user`, or `global`. Ephemeral memory is not indexed. |
| `file` | string | Yes | Repo-relative path to the Markdown entry under `.codex/memory/`. |
| `tags` | string[] | Yes | Searchable labels such as `docs`, `wpf`, `dashboard`, `validation`, `provider`, or `ai-tooling`. |
| `load_when` | object | Yes | Declarative selectors for loading, for example user intents, skills, paths, branches, or explicit tags. |
| `confidence` | string | Yes | `low`, `medium`, or `high`, based on evidence quality and whether the memory has been reviewed. |
| `freshness` | string | Yes | Freshness state such as `fresh`, `review-soon`, `stale`, or `unknown`. |
| `source_refs` | string[] | Yes | Repo files, commands, issue IDs, PRs, or user prompts that support the memory. |
| `review_after` | string | Yes | ISO 8601 date after which the memory must be reviewed before use as guidance. |
| `invalidates_when` | string[] | Yes | Conditions that make the memory unsafe, such as file changes, merged branch, changed command output, or superseding doc updates. |

Example:

```json
{"id":"repo:docs-validation","scope":"repo","tier":"repo","file":".codex/memory/repo/docs-validation.md","tags":["docs","validation"],"load_when":{"skills":["meridian-docs"],"paths":["docs/**"],"intents":["documentation"]},"confidence":"high","freshness":"fresh","source_refs":["docs/engineering/README.md",".codex/skills/_shared/project-context.md"],"review_after":"2026-09-01","invalidates_when":["docs validation commands change","AI tooling validation scripts are renamed"]}
```

## 5. Rules for loading memory

Memory loading must be selective and explainable.

### By user intent

- Map the prompt to a small set of intent labels, such as `documentation`, `implementation`,
  `testing`, `architecture-review`, `provider-work`, `desktop-ui`, `browser-workstation`, or
  `ai-tooling`.
- Load only entries whose `load_when.intents` match the detected intent or whose tags were
  explicitly requested by the user.
- Prefer high-confidence and fresh memories. Stale memories may be loaded only to warn about a
  possible conflict or to guide verification.

### By skill

- When a repo-local skill is selected, load entries whose `load_when.skills` includes that skill.
- Skill instructions remain authoritative. Memory may add reminders, known pitfalls, or prior
  evidence, but must not contradict the skill's `SKILL.md`.
- If memory conflicts with a skill, follow the skill and mark the memory for review.

### By changed path

- Match staged, unstaged, and planned file paths against `load_when.paths` using repo-relative glob
  patterns.
- Path-triggered memory should be narrow. For example, `src/Meridian.Wpf/**` should not load all
  browser workstation memory unless the entry also matches user intent or explicit tags.
- When a path has a nearer `AGENTS.md`, README, or canonical module doc, load and follow that source
  before relying on memory.

### By branch

- Load branch-tier entries only when the current Git branch matches the indexed branch selector.
- Treat branch memory as temporary. It should not guide work after the branch is merged, rebased
  into a materially different scope, or abandoned.
- If branch memory conflicts with repo memory, prefer the narrower branch memory only when it is
  fresh, evidence-backed, and still within the branch's intended work.

### By explicit tags

- If the user or tool request names tags, load matching entries even when intent or path matching
  would not have selected them.
- Explicit tags should broaden discovery, not authority. Loaded memory still needs freshness,
  source, and conflict checks before use.
- Tag names should be stable and descriptive; avoid one-off tags that cannot be reused.

## 6. Rules for promoting session memory

Promotion is a review step, not an automatic dump.

### Session to task

Promote session memory to task memory when:

- The observation is relevant to a named task, issue, roadmap item, or implementation plan.
- It will likely be useful in a later session for the same task.
- It is supported by source references or validation output.
- It is too specific for branch or repo memory.

Examples include accepted design constraints, unresolved task follow-ups, task-specific validation
findings, or reviewed assumptions that must survive session boundaries.

### Session to branch

Promote session memory to branch memory when:

- The observation is tied to the current Git branch rather than a long-term repository rule.
- It describes a temporary migration state, integration order, branch-only failing test, or local
  compatibility decision.
- It should expire when the branch merges or is abandoned.

Branch memory must include an `invalidates_when` entry for branch merge, branch deletion, or scope
change.

### Session to repo

Promote session memory to repo memory only when:

- It describes a stable convention, durable environment limitation, repeatable validation rule, or
  accepted repository workflow.
- It is supported by canonical docs, source files, scripts, or repeated validation evidence.
- It has no narrower owner in task or branch memory.
- It will not duplicate or contradict existing canonical documentation.

Repo promotion should use `confidence: high` only after the source evidence is current and the entry
has a clear review date. If the observation is useful but not fully verified, either keep it in task
memory or promote with `confidence: medium` and a near `review_after` date.

### Promotion hygiene

- Do not promote secrets, personal data, raw logs, copied proprietary text, or private user
  preferences.
- Deduplicate before promotion. Update an existing entry when the new observation refines the same
  guidance.
- Include exact `source_refs` and concrete `invalidates_when` triggers.
- Prefer concise memory entries. Link to canonical docs instead of restating long guidance.

## 7. Stale memory handling and conflict detection

### Stale memory handling

Before using a memory entry as guidance, check:

- `freshness`: use `fresh` entries normally, use `review-soon` entries cautiously, and treat `stale`
  or `unknown` entries as prompts for verification.
- `review_after`: if the date has passed, verify against source references before relying on the
  memory.
- `source_refs`: if referenced files, commands, docs, or scripts no longer exist or have materially
  changed, mark the entry stale.
- `invalidates_when`: if any invalidation condition is true, do not rely on the entry until it is
  reviewed and updated.

Stale entries may remain in the index if they help explain historical decisions, but they must not
be loaded silently as current instructions.

### Conflict detection

When multiple sources disagree, apply this precedence:

1. Direct user instruction for the current turn.
2. System and developer instructions.
3. Applicable `AGENTS.md` instructions by directory scope.
4. Canonical repository docs, source files, tests, scripts, and selected skill instructions.
5. Fresh, high-confidence memory at the narrowest applicable scope.
6. Broader or lower-confidence memory.

Detect conflicts by comparing:

- Memory entries that load for the same intent, skill, path, branch, or tag.
- Memory guidance against current canonical docs and `SKILL.md` files.
- Branch or task entries against repo entries.
- Indexed `invalidates_when` triggers against changed paths and recent validation output.

If a conflict is found:

- Use the higher-precedence source for the current task.
- Mention the conflict only when it affects the task outcome, validation, or follow-up work.
- Mark the lower-precedence or stale memory for review.
- Prefer narrowing an overbroad memory entry over deleting useful historical context.
