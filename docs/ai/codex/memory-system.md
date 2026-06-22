# Codex Memory System

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-20

This document is the canonical contract for Meridian's repo-local Codex memory system. The system
stores small, source-backed memory entries under `.codex/memory/` so future Codex sessions can
recover durable context without loading all prior work or treating guesses as instructions.

Memory supplements canonical repository guidance. It never overrides direct user instructions,
system/developer instructions, applicable `AGENTS.md` files, source code, tests, scripts, docs, or
selected skill `SKILL.md` files.

## Purpose And Non-Goals

The memory system should:

- Preserve high-value context that is expensive to rediscover, such as validated repository
  conventions, recurring validation lanes, branch-specific migration notes, and accepted decisions.
- Load memory selectively by task descriptor, user intent, selected skill, changed path, branch, or
  explicit tag.
- Bind task-specific memory to an explicit task descriptor so one Codex task does not inherit
  another task's assumptions.
- Track progress for very long Codex goals in a compact inventory that survives compaction,
  interruption, and thread continuation.
- Keep each durable entry auditable through source references, confidence, freshness, review dates,
  and invalidation triggers.
- Make promotion deliberate so short-lived observations do not become repo guidance without review.
- Detect broken links, stale entries, unindexed files, invalid metadata, and overbroad routing before
  memory silently steers implementation.

The memory system must not:

- Store secrets, credentials, tokens, personal data, customer data, raw logs, or proprietary
  external content.
- Override canonical repository sources or scoped `AGENTS.md` instructions.
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
| `branch` | Current Git branch | Until branch merges, is abandoned, or review expires | Loaded only when branch scope matches | Must include branch invalidation triggers. |
| `task` | Named work item, issue, plan, or prompt family | Until task closes or review expires | Loaded only when task descriptor matches | Must stay narrower than repo memory. |
| `repo` | Meridian repository | Durable, reviewed periodically | Loaded by task, skill, path, intent, branch, or tag | Requires current source references and stable repository relevance. |
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
    example.yml
  goals/
    README.md
    example.yml
  branches/
    README.md
  sessions/
    README.md
  archive/
    README.md
```

Storage rules:

- `index.yml` is the machine-readable lookup surface.
- Markdown files are human-readable memory entries. Indexed entries must include YAML front matter
  with the same required metadata as the index entry.
- Task descriptor files under `tasks/*.yml` are YAML routing inputs, not indexed memory entries.
  They are exempt from entry indexing and must not contain durable guidance by themselves.
- Goal inventory files under `goals/*.yml` are YAML progress records, not indexed memory entries.
  They are exempt from entry indexing and should point to the active task descriptor.
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
| `load_when` | object | Yes | Positive selectors for `skills`, `paths`, `intents`, `branches`, explicit `tags`, and `task`. |
| `exclude_when` | object | No | Negative selectors for `skills`, `paths`, `intents`, `branches`, `tags`, and `task_ids`; any match prevents loading. |
| `confidence` | string | Yes | `low`, `medium`, or `high`. |
| `freshness` | string | Yes | `fresh`, `review-soon`, `stale`, or `unknown`. |
| `source_refs` | non-empty string array | Yes | Repo files, URLs, or other explicit evidence supporting the memory. Missing repo-local paths are warnings that require review. |
| `review_after` | ISO date | Yes | Date after which the memory is treated as stale until reviewed against its source references. |
| `invalidates_when` | non-empty string array | Yes | Concrete conditions that make the memory unsafe until reviewed. |
| `retired_because` | string | Archive only | Required for `archive` entries; explains why the memory was retired. |
| `replaced_by` | string array | Archive only | Required for `archive` entries; lists replacement entry IDs, docs, or an explicit no-replacement marker. |

`load_when.task` is required on every indexed entry and supports:

- `ids`: explicit task IDs such as `codex-memory-routing-example`.
- `work_modes`: task modes such as `planning`, `implementation`, or `validation`.
- `intents`: task descriptor intent labels.
- `paths`: planned-path globs from the active task descriptor.

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
    task:
      ids: []
      work_modes:
        - implementation
      intents:
        - ai-tooling
      paths:
        - .codex/memory/**
  confidence: high
  freshness: fresh
  source_refs:
    - docs/ai/tooling/README.md
  review_after: 2026-09-19
  invalidates_when:
    - AI tooling validation commands change.
```

Use `exclude_when` to keep shared repo memories from loading in unrelated lanes. For example,
architecture memory can exclude `ai-tooling` so a shared implementation skill does not pull
architecture guidance into a docs-tooling task.

## Task Descriptors

Task descriptors are YAML files under `.codex/memory/tasks/<task-id>.yml`. They describe the active
Codex task for routing only:

```yaml
version: 1
task_id: codex-memory-routing-example
intent: ai-tooling
selected_skill: meridian-implementation-assurance
work_mode: implementation
branch: main
planned_paths:
  - docs/ai/codex/**
  - .codex/memory/**
  - build/scripts/docs/**
memory_tags:
  - ai-guidance
  - validation
success_criteria:
  - Explain selected and skipped memory entries before loading them.
promotion_candidates: []
```

Required fields are `version`, `task_id`, `intent`, `selected_skill`, `work_mode`, `branch`,
`planned_paths`, `memory_tags`, and `success_criteria`. `promotion_candidates` is optional and is a
review queue only; it must not promote memory automatically.

## Goal Inventories

Goal inventories are YAML files under `.codex/memory/goals/<goal-id>.yml`. Use them when Codex is
running a very long goal that may span compaction, thread continuation, or multiple implementation
passes.

```yaml
version: 1
goal_id: codex-memory-long-goal-example
objective: Keep the Codex memory system usable during very long implementation goals.
status: active
started_at: 2026-06-20T00:00:00Z
updated_at: 2026-06-20T00:00:00Z
active_task_descriptor: .codex/memory/tasks/example.yml
progress_inventory:
  - id: task-specific-routing
    status: completed
    summary: Added task descriptor routing and explainable memory selection.
    evidence_refs:
      - build/scripts/docs/check-codex-memory.py
    updated_at: 2026-06-20T00:00:00Z
next_actions:
  - Keep progress inventory items evidence-backed and compact.
open_questions: []
promotion_candidates: []
```

Required fields are `version`, `goal_id`, `objective`, `status`, `started_at`, `updated_at`,
`active_task_descriptor`, `progress_inventory`, and `next_actions`. Allowed goal statuses are
`active`, `blocked`, `complete`, and `abandoned`. Progress items require `id`, `status`, `summary`,
`evidence_refs`, and `updated_at`; allowed progress statuses are `pending`, `in_progress`,
`completed`, `blocked`, and `deferred`.

Goal inventories should stay compact. They track progress and evidence, not durable guidance. Put
reusable facts into task, branch, or repo memory through the normal promotion workflow.

Update goal progress through the checker instead of hand-editing inventory items during a long run:

```bash
python build/scripts/docs/check-codex-memory.py \
  --goal .codex/memory/goals/<goal-id>.yml \
  --record-goal-progress <progress-id> \
  --progress-status completed \
  --progress-summary "Recorded the validated memory checkpoint." \
  --progress-evidence-ref build/scripts/docs/check-codex-memory.py
```

The command creates or updates one `progress_inventory` item, refreshes `updated_at`, validates the
result before writing, and prints a compact `Memory update` notice. Add `--next-action` or
`--open-question` only when the checkpoint changes those lists.

## Loading Rules

Memory loading must be selective and explainable.

By goal inventory:

- Use `--goal .codex/memory/goals/<goal-id>.yml --receipt` for very long Codex goals.
- When `--goal` is provided and `--task` is not, route through the goal's
  `active_task_descriptor`.
- Include goal status, completed/total progress count, active task descriptor, next actions, and
  blockers in the memory receipt or handoff summary.
- Update the goal inventory with `--record-goal-progress` at natural checkpoints: after a
  validation pass, before compaction, after resuming a thread, and when the active task descriptor
  changes.

By task descriptor:

- Prefer `--task .codex/memory/tasks/<task-id>.yml` for recurring or multi-step Codex work.
- Load task-tier entries only when their `scope` or `load_when.task.ids` matches the descriptor
  `task_id`.
- Load branch-tier entries only when their `scope` or `load_when.branches` matches the active branch
  selector.
- Apply task descriptor `intent`, `selected_skill`, `work_mode`, `branch`, `planned_paths`, and
  `memory_tags` as routing inputs.
- Treat task or branch scope mismatches as skipped routing decisions, not as permission to load
  nearby task or branch memory.

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

- Load branch-tier entries only when the current Git branch matches `load_when.branches` or the
  branch scope.
- Treat branch memory as temporary. It should expire when the branch merges, is abandoned, or its
  scope materially changes.
- If branch memory conflicts with repo memory, prefer the narrower branch memory only when it is
  fresh, evidence-backed, and still within branch scope.

By explicit tag:

- If the user or a tool request names tags, load matching entries even when intent or path matching
  would not have selected them.
- Explicit tags broaden discovery, not authority. Freshness, source references, and conflict checks
  still apply.
- Descriptor `memory_tags` match `load_when.tags`; they do not automatically load entries only
  because a generic entry tag is present. Use CLI `--tags` for an explicit user/tool tag request.

Negative guards:

- Evaluate `exclude_when` after collecting possible positive matches and before loading.
- Any matching skill, intent, branch, path, tag, or task ID skips the entry.
- Prefer narrowing an overbroad entry with `exclude_when` before deleting useful memory that still
  applies elsewhere.

Memory receipt:

- At startup or route changes, report selected memory IDs, the reason they matched, stale warnings,
  and task/branch entries skipped because their scope did not match.
- Use `--receipt` to print the same reference/dereference summary the JSON payload exposes as
  `memory_receipt`.
- Treat `referenced` entries as eligible context to read; treat `dereferenced` entries as explicitly
  skipped for the current task, branch, goal, path, or tag route.
- Keep the receipt compact and inside the existing Codex workflow disclosure shape; it is context
  provenance, not a separate audit log.

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
- Include exact non-empty `source_refs`, a future `review_after`, and concrete non-empty `invalidates_when` triggers.
- Prefer concise entries and link to canonical docs instead of restating long instructions.
- Record `promotion_candidates` in session or task notes only as reviewed candidates with target
  tier, source evidence, and reason; keep promotion explicit through `--promote-session`.
- In a goal inventory, use `promotion_candidates` only as a queue of candidate observations to
  review later; do not treat it as a write instruction.
- Repo-level promotion requires source references. User/global promotion requires explicit user
  approval and a future opt-in mechanism.

## Staleness, Invalidation, And Conflict Rules

Before using a memory entry as guidance, check:

- `freshness`: `fresh` is normal; `review-soon` is cautionary; `stale` and `unknown` require
  verification.
- `review_after`: if the date has passed, the checker marks the entry stale; verify against source references before relying on it.
- `source_refs`: every indexed entry must name at least one source; if referenced repo-local paths no longer exist or materially changed, review the memory.
- `invalidates_when`: every indexed entry must name at least one invalidation trigger; if any condition is true, do not rely on the entry until updated.
- `archive` retirement metadata: archived entries must explain why they were retired and list replacement guidance or an explicit no-replacement marker.

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
  and shared UI-surface work, but excludes AI-tooling tasks.
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
| Codex runs a very long goal | Matching goal inventory plus its active task descriptor |

## Validation

Run the memory checker after memory contract, index, descriptor, or entry changes:

```bash
python build/scripts/docs/check-codex-memory.py --summary
```

The checker validates that:

- `index.yml` exists and is pure YAML data.
- Active linked memory files exist under `.codex/memory/`; active entries that point outside that tree are rejected.
- Required metadata fields exist in the index and Markdown front matter.
- `load_when.task` selector metadata is present and well-formed.
- Optional `exclude_when` selectors are well-formed.
- Task descriptors used with `--task` live under `.codex/memory/tasks/` and contain the required
  routing fields.
- Goal inventories used with `--goal` live under `.codex/memory/goals/`, contain progress inventory
  metadata, and point to an existing active task descriptor.
- IDs are unique.
- `source_refs` is non-empty, and missing repo-local source paths are emitted as warnings.
- `review_after` values are valid ISO dates.
- Entries whose `review_after` date has passed are visible as stale warnings and route through `--stale-only`.
- `invalidates_when` is non-empty for every indexed entry.
- Archive entries include `retired_because` and `replaced_by` retirement metadata.
- Unknown active tiers, disabled tiers, and invalid scopes are rejected.
- Non-README memory files are indexed.
- Task descriptor YAML files under `.codex/memory/tasks/` are exempt from entry indexing.
- Goal inventory YAML files under `.codex/memory/goals/` are exempt from entry indexing.
- `--explain` reports selected and skipped routing decisions, including task-scope conflicts.

Optional helper modes:

```bash
python build/scripts/docs/check-codex-memory.py --summary --stale-only
python build/scripts/docs/check-codex-memory.py --paths docs/ai/codex/quickstart.md
python build/scripts/docs/check-codex-memory.py --tags ai-guidance validation
python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --explain --summary
python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --json-output artifacts/codex/memory-routing.json
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --record-goal-progress validated-routing --progress-status completed --progress-summary "Validated memory routing." --progress-evidence-ref build/scripts/docs/check-codex-memory.py
```

Use `--write-stub`, `--promote-session`, and `--record-goal-progress` only for reviewed memory
maintenance. They are explicit write paths and must keep repo promotion source-backed.
