# Meridian Archive Placement Guide

Use this guide when the task is about deciding whether Meridian content stays active, moves into `archive/`, or should be deleted.

## Decision Matrix

| Classification | Use when | Default destination |
| --- | --- | --- |
| `active` | The file is current, referenced, or part of active workflows | Keep in canonical active folder |
| `archive-doc` | The document is historical, superseded, or reference-only | `archive/docs/<bucket>/` |
| `archive-code` | The code or tests are retired but still worth preserving | `archive/code/<original-relative-path>` |
| `delete` | The file is transient clutter or a no-value duplicate | Remove instead of archive |

## Canonical Active Locations

| Content type | Preferred location |
| --- | --- |
| Product and engineering docs | `docs/` |
| Application and library source | `src/` |
| Automated tests | `tests/` |
| Support scripts and repo tooling | `scripts/` |
| Deployment assets | `deploy/` |
| Build and generated outputs | `artifacts/`, `bin/`, `obj/`, other ignored output folders |
| Benchmarks and experiments still in active use | `benchmarks/` |
| Project-specific planning or scoped work folders | `PROJECTS/`, `issues/`, or an existing active docs section |

## Archive Buckets

| Bucket | Use for |
| --- | --- |
| `archive/docs/assessments/` | Audits, investigations, evaluations, cleanup findings |
| `archive/docs/plans/` | Completed or superseded plans, reorganization proposals, old roadmaps |
| `archive/docs/summaries/` | Historical summaries, snapshots, one-off reports, retrospectives |
| `archive/docs/migrations/` | Legacy migration notes, platform transition material |
| `archive/docs/assets/` | Images, diagrams, or attachments used by archived docs |
| `archive/code/` | Retired code and tests, preserving prior layout where practical |

## Placement Heuristics

- Mirror the old relative path under `archive/code/` when moving retired source or tests.
- Prefer archiving over deletion when the item explains a past decision, migration, or removed subsystem.
- Prefer deletion over archiving for generated outputs, caches, duplicate artifacts, and scratch files with no lasting reference value.
- Before moving generated docs, confirm whether repo tooling intentionally publishes them into `docs/status/` or `docs/generated/`.
- Keep active docs concise by moving historical material out of `docs/` once it stops guiding current work.
- Update archive indexes or nearby README files when a move changes how someone will discover the material later.

## Quick Checks Before Moving

1. Search for references in project files, solution files, docs, and tests.
2. Confirm the item is not part of the active build or runtime path.
3. Distinguish strong references from weak ones:
   - strong: active README files, current roadmap/plan docs, hand-maintained guidance
   - weak: generated file trees, validation reports, automation snapshots, stale inventories
4. Decide whether the value is historical reference or just clutter.
5. Choose the most specific archive bucket available.
6. Update any links or indexes that pointed at the old location.

## Local Scratch Examples

- Delete or ignore local tool output such as `.playwright-cli/`, temporary screenshot captures, and ad hoc session logs when they are not part of the documented repo workflow.
- Archive only when the output itself has lasting historical value; otherwise keep the repo clean and rely on `.gitignore`.

## Script-Assisted Trace

Use the bundled trace script when a decision is not obvious from one README/index read:

```bash
python scripts/trace_archive_candidates.py --path docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md
python scripts/trace_archive_candidates.py --path docs/status/docs-automation-summary.json --path .playwright-cli --json
```

The trace output is most useful for:

- separating strong references from weak inventory/report references
- proving that a dated doc is still active
- proving that a generated-looking file is automation-owned
- identifying delete-or-ignore candidates at the repo root

## Example Triggers

- "Move this superseded migration plan into the archive."
- "Archive the retired prototype code but keep it for reference."
- "Where should this outdated audit report live?"
- "Clean up the repo root and move stale folders into the right place."
