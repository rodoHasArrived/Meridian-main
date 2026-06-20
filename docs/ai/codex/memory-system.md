# Codex Memory System

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-19

This document is the canonical contract for Meridian's repo-local Codex memory system. The system
stores small, source-backed memory entries under `.codex/memory/` so future Codex sessions can
recover durable context without loading all prior work or treating guesses as instructions.

Memory supplements canonical repository guidance. It does not replace direct user instructions,
system/developer instructions, applicable `AGENTS.md` files, source code, tests, scripts, docs, or
skill `SKILL.md` files.

## Purpose And Non-Goals

The memory system should:

- Preserve high-value context that is expensive to rediscover, such as validated repository
  conventions, recurring validation lanes, branch-specific migration notes, and accepted decisions.
- Load memory selectively by user intent, selected skill, changed path, branch, or explicit tag.
- Keep each durable entry auditable through source references, confidence, freshness, review dates,
  and invalidation triggers.
- Make promotion deliberate so short-lived observations do not become repo guidance without review.
- Detect broken links, stale entries, unindexed files, and invalid metadata before memory silently
  steers implementation.

The memory system must not:

- Store secrets, credentials, tokens, personal data, customer data, raw logs, or proprietary
  external content.
- Override direct user instructions, canonical docs, source files, tests, scripts, selected skills,
  or scoped `AGENTS.md` instructions.
- Load every memory entry at startup.
- Turn speculative notes, unverified assumptions, or transient command output into durable repo
  guidance.
- Enable user or global memory by default.
- Substitute for validation. Memory can suggest risks and commands, but agents must still run the
  narrowest proof for the current change.

## Memory Tiers

| Tier | Scope | Lifetime | Default Loading | Write Rule |
| --- | --- | --- | --- | --- |
| `ephemeral` | Current reasoning turn only | Minutes | Active context only | Never written under `.codex/memory/`. |
| `session` | Current Codex session | Until session end, compaction, or promotion | Loaded only by the current session or explicit session ID | May store temporary observations, inspected files, assumptions, and validation notes. |
| `branch` | Current Git branch | Until branch merges, is abandoned, or review expires | Loaded when branch selector matches | Must include branch invalidation triggers. |
| `task` | Named work item, issue, plan, or prompt family | Until task closes or review expires | Loaded by task ID, intent, path, or tag | Must stay narrower than repo memory. |
| `repo` | Meridian repository | Durable, reviewed periodically | Loaded by skill, path, intent, or tag | Requires current source references and stable repository relevance. |
| `archive` | Retired memory | As long as audit value remains | Not loaded for active guidance | Must name replacement guidance or archival reason. |
| `user` | Explicit operator profile outside repo | Durable outside repo | Disabled | Not read or written by repo-local tooling without explicit future opt-in. |
| `global` | Cross-user or organization baseline outside repo | Durable outside repo | Disabled | Not read or written by repo-local tooling without explicit future opt-in. |

The active repo-local tiers are `session`, `branch`, `task`, `repo`, and `archive`. `user` and
`global` are schema-known so validators can reject accidental writes unless a later explicit opt-in
design enables them.

## Storage Layout

The Meridian repo-local store uses YAML index data plus reviewable Markdown entries:

```text
.codex/memory/
  README.md
  index.yml
  repo/
    validation.md
    architecture.md
    ai-guidance.md
  tasks/
    README.md
  branches/
    README.md
  sessions/
    README.md
  archive/
    README.md
```

Storage rules:

- `index.yml` is the machine-readable lookup surface.
- Markdown files are human-readable entries. Indexed entries must include YAML front matter with
  the same required metadata as the index entry.
- Folder `README.md` files provide guidance and are exempt from entry indexing.
- File names should be stable lowercase slugs and must stay under `.codex/memory/`.
- Use `repo/` only for stable, sourced facts. Put uncertain or temporary findings in `sessions/`,
  `tasks/`, or `branches/`.
- Use `archive/` for retired entries that should remain auditable but should not guide active work.

## Index Schema

Each `entries` item in `.codex/memory/index.yml` must contain:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string | Yes | Stable unique ID, usually `<tier>:<slug>`. |
| `tier` | string | Yes | One of `session`, `branch`, `task`, `repo`, or `archive`; `user` and `global` are disabled. |
| `scope` | string | Yes | Boundary such as `repo`, `branch:<name>`, `task:<id>`, `session:<id>`, or `archive`. |
| `file` | string | Yes | Repo-relative path to the Markdown entry under `.codex/memory/`. |
| `tags` | string array | Yes | Stable labels such as `validation`, `architecture`, `ai-guidance`, `wpf`, or `browser-workstation`. |
| `load_when` | object | Yes | Selectors for `skills`, `paths`, `intents`, `branches`, and explicit `tags`. |
| `confidence` | string | Yes | `low`, `medium`, or `high`. |
| `freshness` | string | Yes | `fresh`, `review-soon`, `stale`, or `unknown`. |
| `source_refs` | string array | Yes | Repo files or other explicit evidence supporting the memory. Repo-tier entries require existing repo paths. |
| `review_after` | ISO date | Yes | Date after which the memory must be reviewed before being trusted as current guidance. |
| `invalidates_when` | string array | Yes | Conditions that make the memory unsafe until reviewed. |

Example:

```yaml
- id: repo:validation
  tier: repo
  scope: repo
  file: .codex/memory/repo/validation.md
  tags:
    - validation
    - ai-tooling
  load_when:
    skills:
      - meridian-docs
    paths:
      - docs/**
      - build/scripts/docs/**
    intents:
      - validation
    branches: []
    tags:
      - validation
  confidence: high
  freshness: fresh
  source_refs:
    - docs/ai/tooling/README.md
  review_after: 2026-09-19
  invalidates_when:
    - AI tooling validation commands change.
```

## Loading Rules

Memory loading must be selective and explainable.

By intent:

- Map the prompt to a small set of intent labels such as `documentation`, `implementation`,
  `validation`, `architecture`, `provider-work`, `desktop-ui`, `browser-workstation`,
  `ai-guidance`, or `ai-tooling`.
- Load entries whose `load_when.intents` match the detected intent.
- Load stale entries only as warnings or verification prompts, not as current instructions.

By skill:

- When a repo-local skill is selected, load entries whose `load_when.skills` includes that skill.
- Skill instructions remain authoritative. If memory conflicts with a skill, follow the skill and
  mark the memory for review.

By path:

- Match staged, unstaged, and planned paths against `load_when.paths` using repo-relative glob
  patterns.
- Path-triggered memory should stay narrow. For example, `src/Meridian.Wpf/**` should not load
  browser workstation memory unless the entry also matches intent or tags.
- When a path has a nearer `AGENTS.md`, README, or source module doc, load that source before
  relying on memory.

By branch:

- Load branch-tier entries only when the current Git branch matches `load_when.branches`.
- Treat branch memory as temporary. It should expire when the branch merges, is abandoned, or its
  scope materially changes.
- If branch memory conflicts with repo memory, prefer the narrower branch memory only when it is
  fresh, evidence-backed, and still within branch scope.

By explicit tag:

- If the user or a tool request names tags, load matching entries even when intent or path matching
  would not have selected them.
- Explicit tags broaden discovery, not authority. Freshness, source references, and conflict checks
  still apply.

## Promotion And Compaction

Promotion is a review step, not an automatic dump.

During work, Codex may keep temporary session notes. At task end, reusable observations should be
classified as `discard`, `session only`, `branch`, `task`, or `repo`.

Promote session memory to task memory when:

- It belongs to a named task, issue, roadmap item, or accepted plan.
- It will likely be useful in a later session for the same task.
- It is supported by source references or validation output.
- It is too specific for branch or repo memory.

Promote session memory to branch memory when:

- It is tied to the current Git branch rather than long-term repository behavior.
- It describes temporary migration state, integration order, branch-only failures, or compatibility
  decisions.
- It should expire on branch merge, deletion, abandonment, or scope change.

Promote session memory to repo memory only when:

- It describes a stable convention, durable environment limit, repeatable validation rule, or
  accepted repository workflow.
- It is supported by canonical docs, source files, scripts, or repeated validation evidence.
- It has no narrower task or branch owner.
- It does not duplicate or contradict existing canonical guidance.

Promotion hygiene:

- Do not promote secrets, personal data, copied proprietary text, raw logs, or private preferences.
- Deduplicate before promotion. Update an existing entry when the new observation refines the same
  guidance.
- Include exact `source_refs`, `review_after`, and concrete `invalidates_when` triggers.
- Prefer concise entries and link to canonical docs instead of restating long instructions.
- Repo-level promotion requires source references. User/global promotion requires explicit user
  approval and a future opt-in mechanism.

## Staleness, Invalidation, And Conflict Rules

Before using a memory entry as guidance, check:

- `freshness`: `fresh` is normal; `review-soon` is cautionary; `stale` and `unknown` require
  verification.
- `review_after`: if the date has passed, verify against source references before relying on it.
- `source_refs`: if referenced paths no longer exist or materially changed, review the memory.
- `invalidates_when`: if any condition is true, do not rely on the entry until updated.

Source precedence:

1. Direct user instruction for the current turn.
2. System and developer instructions.
3. Applicable `AGENTS.md` instructions by directory scope.
4. Canonical repository docs, source files, tests, scripts, and selected skill instructions.
5. Fresh, high-confidence memory at the narrowest applicable scope.
6. Broader or lower-confidence memory.

If memory conflicts with a higher-precedence source, use the higher-precedence source and mark the
memory for review. Narrow an overbroad memory entry instead of deleting useful historical context
when the entry still has audit value.

## Meridian Seed Examples

The initial repo-level bundle is intentionally small:

- `.codex/memory/repo/validation.md` loads for AI tooling, docs, validation, `.codex/**`, and
  `build/scripts/docs/**` work.
- `.codex/memory/repo/architecture.md` loads for architecture, desktop, browser workstation, MDIF,
  and shared UI-surface work.
- `.codex/memory/repo/ai-guidance.md` loads for Codex guidance, skill routing, AI docs, and memory
  maintenance work.

Future examples should stay narrow:

| Situation | Memory To Add Or Load |
| --- | --- |
| User asks to test AI tooling | `repo:validation`, `repo:ai-guidance` |
| User changes WPF files | Future `repo:wpf-desktop` only if it captures stable desktop-specific facts |
| User changes browser dashboard | Future `repo:browser-workstation` only if it captures stable browser-specific facts |
| User asks for blueprint | Relevant task memory plus `repo:architecture` |
| User resumes a branch | Matching branch memory plus repo entries selected by changed paths |

## Validation

Run the memory checker after memory contract, index, or entry changes:

```bash
python build/scripts/docs/check-codex-memory.py --summary
```

The checker validates that:

- `index.yml` exists and is pure YAML data.
- Linked memory files exist under `.codex/memory/`.
- Required metadata fields exist in the index and Markdown front matter.
- IDs are unique.
- `source_refs` for repo-local source paths exist.
- `review_after` values are valid ISO dates.
- Expired entries are visible as stale warnings.
- Unknown active tiers, disabled tiers, and invalid scopes are rejected.
- Non-README memory files are indexed.

Optional helper modes:

```bash
python build/scripts/docs/check-codex-memory.py --summary --stale-only
python build/scripts/docs/check-codex-memory.py --paths docs/ai/codex/quickstart.md
python build/scripts/docs/check-codex-memory.py --tags ai-guidance validation
```

Use `--write-stub` and `--promote-session` only for reviewed memory maintenance. They are explicit
write paths and must keep repo promotion source-backed.
