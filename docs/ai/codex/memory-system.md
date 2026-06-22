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
| `session` | Current Codex session | Until session end, compaction, or promotion | Loaded only by the current session or explicit session ID | May store temporary observations, inspected files, assumptions, and validation notes. |
| `branch` | Current Git branch | Until branch merges, is abandoned, or review expires | Loaded only when branch scope matches | Must include branch invalidation triggers. |
| `task` | Named work item, issue, plan, or prompt family | Until task closes or review expires | Loaded only when task descriptor matches | Must stay narrower than repo memory. |
| `repo` | Meridian repository | Durable, reviewed periodically | Loaded by task, skill, path, intent, branch, or tag | Requires current source references and stable repository relevance. |
| `archive` | Retired memory | As long as audit value remains | Not loaded for active guidance | Must name replacement guidance or archival reason. |
| `user` | Explicit operator profile outside repo | Durable outside repo | Disabled | Not read or written by repo-local tooling without explicit future opt-in. |
| `global` | Cross-user or organization baseline outside repo | Durable outside repo | Disabled | Not read or written by repo-local tooling without explicit future opt-in. |

The active repo-local tiers are only `session`, `branch`, `task`, `repo`, and `archive`.
Per-turn reasoning notes are ephemeral context, not memory-system tiers, and must not be written under
`.codex/memory/` unless they pass the promotion workflow. `user` and `global` are disabled tiers:
they are schema-known only so validators can reject accidental writes unless a later explicit opt-in
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
Indexed files must live in the folder that matches their tier: `repo/`, `tasks/`, `branches/`,
`sessions/`, or `archive/`.

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

Tier-specific validation rules:

- `repo` entries must include at least one stable, existing repo-local `source_refs` path. External
  URLs or fragment notes can supplement evidence, but they cannot be the only support for durable
  repo guidance.
- `task` entries must be bound to a task descriptor by `scope: task:<id>` or by
  `load_when.task.ids`. Task memory must stay narrower than repo memory.
- `branch` entries must include branch invalidation rules, such as merge, abandonment, deletion,
  review expiry, or branch-scope change.
- `session` entries must not be indexed as durable guidance unless they are promoted. Validators
  allow only explicit task/session binding and require invalidation on session end, compaction, or
  promotion; broad skill, path, intent, branch, tag, work-mode, task-intent, or task-path selectors
  fail validation.
- `archive` entries must not load as active guidance. Their `load_when` selectors must remain empty
  so archived entries are retained only for auditability.
- `user` and `global` entries fail validation. They remain disabled until a future explicit opt-in
  design defines storage, consent, privacy, and loading behavior.

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

## Routing Algorithm

Memory loading must be selective and explainable. Use this routing algorithm for every memory-aware
Codex task:

1. Establish higher-precedence instructions and evidence first: direct user/system/developer
   instructions, applicable `AGENTS.md`, source, tests, scripts, docs, and selected skill guidance.
2. Identify the active branch and classify the task intent, selected skill, work mode, planned paths,
   and explicit memory tags. Prefer a `.codex/memory/tasks/<task-id>.yml` descriptor for recurring
   or multi-step work.
3. If a goal inventory is active, resolve its `active_task_descriptor` before evaluating entries.
4. Collect candidate entries whose `load_when` selectors match at least one relevant task, intent,
   skill, path, branch, or explicit tag.
5. Reject entries with disabled tiers, scope mismatches, missing files, invalid metadata, expired
   trust without verification, or any matching `exclude_when` selector.
6. Order remaining entries from narrowest to broadest scope: matching `session`, then `branch`,
   then `task`, then `repo`. `archive` entries are not active guidance and load only for audit or
   explicit maintenance.
7. Read only the selected memory entries needed for the task and produce a receipt listing
   referenced entries, dereferenced entries, match reasons, stale warnings, and skipped task or
   branch scopes.
8. If memory conflicts with higher-precedence sources, ignore the memory for guidance and mark it
   for invalidation or review.

### Routing Inputs

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
- Do not load `archive` entries as active instructions; keep them dereferenced unless a human is
  auditing historical memory.

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
  skipped memory IDs, skip reasons, task descriptor path, goal inventory path, and the branch or
  path selectors used for routing.
- Use `--receipt` to print the same reference/dereference summary the JSON payload exposes as
  `memory_receipt`.
- Treat `selected_memory` entries as eligible context to read; treat `skipped_memory` entries as
  explicitly skipped for the current task, branch, goal, path, or tag route. The JSON payload keeps
  `referenced` and `dereferenced` aliases for older consumers.
- Keep the receipt compact and inside the existing Codex workflow disclosure shape; it is context
  provenance, not a separate audit log.

Example task-scoped receipt:

```text
$ python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
Codex memory status: pass; 5 entrie(s), 2 selected, 0 error(s), 0 warning(s).
selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md
selected: repo:validation -> .codex/memory/repo/validation.md

Memory receipt:
task_descriptor_path: .codex/memory/tasks/example.yml
task: codex-memory-routing-example
selectors: branches=['main']; paths=['docs/ai/codex/**', '.codex/memory/**', 'build/scripts/docs/**']
selected: repo:validation -> .codex/memory/repo/validation.md (task work mode matches implementation; task intent matches ai-tooling; ...)
selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md (task work mode matches implementation; task intent matches ai-tooling; ...)
skipped: repo:architecture -> .codex/memory/repo/architecture.md (excluded by intent ai-tooling)
skipped: repo:accounting-workflows -> .codex/memory/repo/accounting-workflows.md (excluded by intent ai-tooling)
skipped: repo:financial-record-explorers -> .codex/memory/repo/financial-record-explorers.md (excluded by intent ai-tooling)
```

Example goal-scoped receipt:

```text
$ python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
Codex memory status: pass; 5 entrie(s), 2 selected, 0 error(s), 0 warning(s).
goal: codex-memory-long-goal-example (active); 2/3 progress item(s) completed; active task .codex/memory/tasks/example.yml
selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md
selected: repo:validation -> .codex/memory/repo/validation.md

Memory receipt:
task_descriptor_path: .codex/memory/tasks/example.yml
goal_inventory_path: .codex/memory/goals/example.yml
goal: codex-memory-long-goal-example
task: codex-memory-routing-example
selectors: branches=['main']; paths=['docs/ai/codex/**', '.codex/memory/**', 'build/scripts/docs/**']
selected: repo:validation -> .codex/memory/repo/validation.md (task work mode matches implementation; task intent matches ai-tooling; ...)
selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md (task work mode matches implementation; task intent matches ai-tooling; ...)
skipped: repo:architecture -> .codex/memory/repo/architecture.md (excluded by intent ai-tooling)
skipped: repo:accounting-workflows -> .codex/memory/repo/accounting-workflows.md (excluded by intent ai-tooling)
skipped: repo:financial-record-explorers -> .codex/memory/repo/financial-record-explorers.md (excluded by intent ai-tooling)
```

## Promotion And Compaction

Promotion is a review step, not an automatic dump. Compaction may create short-lived session notes,
but it must not promote them automatically.

During work, Codex may keep temporary session notes. At task end, reusable observations should be
classified as `discard`, `session only`, `branch`, `task`, `repo`, or `archive`. Promotion is only
allowed through the paths below:

| Promotion | Allowed When | Notes |
| --- | --- | --- |
| `session` to `task` | The observation belongs to a named task, issue, roadmap item, or accepted plan and will likely help a later session on the same task. | Keep `scope` and `load_when.task.ids` tied to that task. |
| `session` to `branch` | The observation is tied to the current Git branch, such as temporary migration state, integration order, branch-only failures, or compatibility decisions. | Include branch selectors and branch invalidation triggers. |
| `task` to `repo` | A task-scoped observation has become a stable repository convention, durable environment limit, repeatable validation rule, or accepted workflow. | Remove task-only selectors and prove current repo relevance with canonical source references. |
| `branch` to `repo` | A branch-scoped observation remains true after branch integration or review and now describes durable repository behavior. | Remove branch-only selectors unless they are routing hints for active branches. |
| active entry to `archive` | The entry is superseded, expired, contradicted by current sources, or no longer useful as active guidance but retains audit value. | The archive entry must name the archival reason or replacement guidance. |

Do not promote directly from `session` to `repo`. First stabilize the observation in a task or
branch entry, then promote that reviewed task or branch entry to `repo` if it proves durable. Do not
promote `archive` entries back to active tiers without creating a new reviewed active entry.

Every promotion review must record this evidence before the index and Markdown entry are updated:

- Source references: exact repo paths, canonical docs, source files, scripts, tests, or validation
  evidence that currently support the claim. Repo-tier entries require at least one current
  repo-local source reference outside `.codex/memory/tasks/`, `.codex/memory/branches/`, and
  `.codex/memory/sessions/`.
- Confidence value: `low`, `medium`, or `high`, matching the indexed `confidence` field.
- Freshness value: `fresh`, `review-soon`, `stale`, or `unknown`, matching the indexed `freshness`
  field. New active promotions should normally be `fresh`; use `review-soon` when evidence is
  current but expected to age quickly.
- Review date: the `review_after` date that tells future agents when to re-check the memory.
- Invalidation triggers: concrete `invalidates_when` conditions that make the memory unsafe until
  reviewed.
- Reason for broader persistence: why the memory belongs in the broader target tier instead of
  staying in the source tier or being discarded.
- Conflicting memory entries reviewed: IDs of related or potentially conflicting memory entries, or
  an explicit `none found` note after checking the index.

Promotion target rules:

- Use `task` for claims whose truth depends on one task descriptor, plan, issue, or prompt family.
- Use `branch` for claims whose truth depends on one Git branch lifecycle.
- Use `repo` only when the claim is stable beyond one task or branch and can be supported by current
  source references. A repo entry must not encode task-only claims with `load_when.task.ids`,
  task-specific scopes, or source evidence that only lives under task/session/branch memory.
- Use `archive` when an active entry should no longer guide routing or implementation.

Promotion hygiene:

- Do not promote secrets, personal data, copied proprietary text, raw logs, or private preferences.
- Deduplicate before promotion. Update an existing entry when the new observation refines the same
  guidance.
- Include exact non-empty `source_refs`, a future `review_after`, and concrete non-empty `invalidates_when` triggers.
- Prefer concise entries and link to canonical docs instead of restating long instructions.
- Record `promotion_candidates` in session, task, or goal notes only as reviewed candidates with
  target tier, source evidence, reason for broader persistence, and conflicting entries reviewed;
  keep promotion explicit through `--promote-session` or a direct reviewed index/entry update.
- In a goal inventory, use `promotion_candidates` only as a queue of candidate observations to
  review later; do not treat it as a write instruction.
- Repo-level promotion requires stable repo-local source references. User/global promotion is
  disabled and must fail validation until explicit user approval and a future opt-in mechanism are
  designed and implemented.

## Invalidation

Invalidation is the workflow for retiring, narrowing, or refreshing memory that is no longer safe
to use. Before using a memory entry as guidance, check:

- `freshness`: `fresh` is normal; `review-soon` is cautionary; `stale` and `unknown` require
  verification.
- `review_after`: if the date has passed, the checker marks the entry stale; verify against source references before relying on it.
- `source_refs`: every indexed entry must name at least one source; if referenced repo-local paths no longer exist or materially changed, review the memory.
- `invalidates_when`: every indexed entry must name at least one invalidation trigger; if any condition is true, do not rely on the entry until updated.
- `archive` retirement metadata: archived entries must explain why they were retired and list replacement guidance or an explicit no-replacement marker.

Authoritative source precedence:

1. Direct user, system, and developer instructions for the current turn.
2. Applicable `AGENTS.md` instructions by directory scope.
3. Source files.
4. Tests.
5. Scripts and maintained command implementations.
6. Canonical docs and generated documentation sources.
7. Selected skill `SKILL.md` files and their required shared context.
8. Fresh, high-confidence memory at the narrowest applicable scope.
9. Broader, lower-confidence, stale, or archived memory, which is advisory only.

If memory conflicts with a higher-precedence source, use the higher-precedence source and mark the
memory for review. The invalidation workflow is:

1. Identify the invalidating source, missing path, expired review date, scope mismatch, or true
   `invalidates_when` condition.
2. Stop using the entry as guidance for the current task; stale or conflicting entries may be read
   only as warnings or audit history.
3. Choose the smallest corrective action: refresh metadata and source refs, narrow `load_when`, add
   `exclude_when`, demote to task or branch scope, move to `archive`, or delete only if there is no
   audit value.
4. Preserve an audit trail by naming the replacement guidance or archival reason when moving an
   entry to `archive`.
5. Re-run the memory checker and include the stale/conflict outcome in the task receipt or final
   validation summary.

Narrow an overbroad memory entry instead of deleting useful historical context when the entry still
has audit value.


## Security And Privacy Restrictions

Memory is repo-local guidance, not a secret store or personal profile. Apply these restrictions to
every tier, descriptor, goal inventory, receipt, and promotion candidate:

- Do not store credentials, API keys, tokens, passwords, private keys, cookies, session IDs,
  credential-store paths that expose a person, or recovery material.
- Do not store personal data, customer data, investor data, account numbers, trade records tied to
  real people, raw telemetry containing identifiers, or unredacted logs.
- Do not store proprietary external content, licensed documentation, copied vendor text, or private
  repository material from outside Meridian unless the user explicitly provides it for this repo and
  it is safe to commit.
- Do not store user preferences, user-profile facts, or cross-repository habits in repo-local memory.
  The `user` and `global` tiers are disabled until an explicit opt-in design exists.
- Keep receipts compact: report IDs, match reasons, stale warnings, and skipped scopes; do not paste
  raw logs, secrets, or long source excerpts into receipts.
- Redact sensitive values before adding validation evidence, source refs, progress summaries, or
  promotion candidates. Prefer repo-relative paths and command names over machine-specific paths.
- If sensitive material is discovered in memory, stop using the entry, remove or redact it in the
  same change when safe, validate the index, and report the cleanup without repeating the secret.

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
- `source_refs` for repo-local source paths exist.
- Repo-tier entries have at least one current repo-local source reference outside scoped memory.
- Repo-tier entries do not carry task-only routing such as `load_when.task.ids` or task descriptor
  path selectors.
- `promotion_candidates` include source references, confidence, freshness, review date, invalidation
  triggers, reason for broader persistence, and conflicting entries reviewed.
- `review_after` values are valid ISO dates.
- Expired entries are visible as stale warnings.
- Unknown tiers, disabled `user`/`global` tiers, invalid scopes, and tier/file folder mismatches are
  rejected.
- `repo` entries have stable existing repo-local source references.
- `task` entries have task descriptor or task-scope binding.
- `branch` entries have branch invalidation rules.
- `session` entries do not carry durable broad routing selectors unless promoted.
- `archive` entries have no active `load_when` selectors and are dereferenced for active guidance.
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
