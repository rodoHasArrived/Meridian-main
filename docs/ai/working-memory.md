# AI Working Memory Contract

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-16

Use this contract when an AI task has parallel implementation lanes, concurrent codebase changes,
or enough moving parts that another assistant could lose track of ownership, assumptions, or proof
state. Working memory is task-local coordination state. It is not a permanent knowledge base, a
replacement for source files, or a place to store secrets.

## Purpose

Working memory gives the coordinator and specialist lanes one compact ledger for:

- active objective and acceptance criteria
- file ownership claims and exclusions
- inspected files that should not be rediscovered
- validated facts with source paths
- open assumptions and questions
- codebase drift noticed during the task
- merge queue, integration order, and conflict watch points
- validation evidence, reuse rules, and rerun triggers

Use it before spawning parallel agents, before editing shared files touched by another lane, and
before resuming a long task after unrelated work has changed the tree.

## Minimal Ledger

Keep the ledger short enough to paste into a handoff packet or parallel manifest:

```yaml
working_memory:
  task_id: <short-id>
  coordinator: <owner>
  last_synced: <yyyy-mm-ddThh:mm:ssZ-or-local>
  objective: <single-sentence outcome>
  acceptance:
    - <observable done condition>
  codebase_snapshot:
    branch: <branch>
    git_status_summary: <clean | dirty with scoped summary>
    known_unrelated_changes:
      - <path or owner-owned change to avoid>
  active_claims:
    - lane_id: <lane>
      owner: <agent/person>
      files:
        - <path>
      status: <planned|editing|ready-for-integration|blocked|merged>
  inspected_files:
    - path: <path>
      reason: <why it was read>
      lane_id: <lane>
  validated_facts:
    - fact: <fact>
      source: <path or command>
  open_assumptions:
    - assumption: <what is not proven yet>
      owner: <lane>
      resolve_by: <file, command, or user answer>
  merge_queue:
    - lane_id: <lane>
      depends_on:
        - <lane>
      conflict_watch:
        - <path or API contract>
  validation_snapshot:
    - command: <command>
      outcome: <pass|fail|blocked|not-run>
      reuse_until: <file or condition that invalidates this proof>
```

## Update Rules

- Update working memory before a lane starts editing, when a lane changes its file set, after
  validation, and before final integration.
- Treat `active_claims.files` as write ownership, not just files read. Read-only discovery belongs
  in `inspected_files`.
- Keep validated facts evidence-backed. If the fact cannot point to a source path or command, record
  it as an assumption.
- When `git status --short` changes unexpectedly, refresh `codebase_snapshot` before continuing and
  decide whether validation reuse still applies.
- If two lanes need the same file, the coordinator records merge order and one lane owns the final
  edit. Do not let both lanes independently write the same file.
- Keep secrets, private credentials, raw logs, and large command output out of working memory.

## Codex Usage

For Codex runs, use this contract with:

- [`codex/quickstart.md`](codex/quickstart.md) for startup and proof routing
- [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md) for parallel lane
  ownership
- [`agent-handoff-checklist.md`](agent-handoff-checklist.md) for lane transfer packets
- `.codex/skills/_shared/codex-execution-contract.md` for Codex-specific execution gates

The coordinator owns the ledger. Specialist agents may update their lane status, inspected files,
facts, assumptions, and validation results, but the coordinator owns merge order, conflict
resolution, final validation, and final user-facing claims.

## Closeout

At task end, collapse working memory into the final answer or handoff:

- what changed
- which files were intentionally touched
- validation commands and outcomes
- reused validation and rerun triggers
- unresolved assumptions or residual risks

Do not preserve transient working memory as a long-lived repo artifact unless the task explicitly
asks for a durable run record.
