# Evaluation Harness

Use this harness when you are changing the archive skill, running a broad archive sweep, or claiming the skill is now more robust.

## When To Use It

Run the harness when any of the following are true:

- you changed archive heuristics, placement rules, or evidence weighting
- you are evaluating more than one archive candidate in a pass
- a dated document still has mixed or ambiguous references
- you need to prove the skill is safe before recommending deletes or moves

## Script-Assisted Execution

Prefer the bundled scripts over ad hoc reasoning when the task touches multiple files.

1. Trace each candidate with `scripts/trace_archive_candidates.py`.
2. Replay the deterministic scenarios in `evals/evals.json` with `scripts/run_evals.py`.
3. If the pass was manual or exploratory, summarize it with `scripts/score_eval.py`.

The trace should ignore AI metadata and local mirror surfaces such as `.codex/` and `.claude/` so they do not inflate evidence counts.

Recommended command sequence:

```powershell
python .codex/skills/meridian-archive-organizer/scripts/trace_archive_candidates.py --path docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md
python .codex/skills/meridian-archive-organizer/scripts/run_evals.py --all
python .codex/skills/meridian-archive-organizer/scripts/score_eval.py --scenario B --scores "{\"classification_accuracy\":2,\"reference_trace_quality\":2,\"placement_correctness\":2,\"safety_guardrails\":2,\"cleanup_follow_through\":1}"
```

## Scenario Set

### Scenario A

A dated status or backlog document still has strong references from active planning or documentation entrypoints, so it must stay active.

### Scenario B

A dated snapshot has no strong references and only weak/generated references, so it should move into the correct archive docs bucket.

References that say "snapshot", "historical", or "prior review context" count as weak evidence for this scenario.

### Scenario C

A generated or machine-readable documentation artifact is still written or consumed by automation, so it stays active even if it looks stale.

### Scenario D

A hidden local tool folder, scratch file, or log capture is local clutter and should be deleted or ignored rather than archived.

## Rubric

Score each category from `0` to `2`.

- `classification_accuracy`
  - `0`: wrong disposition
  - `1`: partially right but hedged or inconsistent
  - `2`: correct final classification
- `reference_trace_quality`
  - `0`: no evidence trail
  - `1`: references gathered but not separated well
  - `2`: strong and weak references clearly separated
- `placement_correctness`
  - `0`: wrong destination or no placement logic
  - `1`: mostly right but bucket or path is unclear
  - `2`: destination cleanly matches the placement guide
- `safety_guardrails`
  - `0`: risky move or delete with missing checks
  - `1`: partial safety checks
  - `2`: solution respects build, docs, and automation guardrails
- `cleanup_follow_through`
  - `0`: links or indexes left stale
  - `1`: partial follow-through
  - `2`: surrounding docs or structure were updated appropriately

Passing threshold:

- total score must be at least `8/10`
- no category may be `0`

## Evaluation Report Template

Use this structure in the final response for a non-trivial archive pass:

```text
Scenario: B
Targets: docs/status/EXAMPLE_2026_03_01.md
Evidence:
- trace command and result
- strong references
- weak references
Outcome:
- classification
- old path -> new path
Score:
- classification_accuracy: 2
- reference_trace_quality: 2
- placement_correctness: 2
- safety_guardrails: 1
- cleanup_follow_through: 2
Result: pass (9/10)
```
