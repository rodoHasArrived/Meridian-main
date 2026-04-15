# Evaluation Harness

Use this harness to evaluate whether outputs from `meridian-implementation-assurance` are
complete, correct, and operationally safe.

## How To Run The Eval

1. Pick one or more scenarios from **Scenario Set**.
2. Execute the skill workflow end-to-end.
3. Score results with the **Rubric**.
4. Record evidence in the **Evaluation Report Template**.
5. If any critical check fails, revise output and re-score.

## Script-Assisted Execution

- Use `scripts/score_eval.py` to enforce rubric key coverage, compute totals, and emit a report block.
- Use `scripts/doc_route.py` before documentation edits when placement is unclear.
- Use `scripts/run_evals.py` to run deterministic checks against `evals/evals.json` cases and compare against `evals/benchmark_baseline.json`.

## Prompt-Based Eval Infrastructure

The `evals/` directory contains a prompt set and structured rubric schema for systematic
regression testing.

### Trigger Classification (`evals/meridian-implementation-assurance.prompts.csv`)

A small CSV of prompts labelled `should_trigger=true/false`. Use it to verify that changes to the
skill name or description do not break invocation.

Validate CSV structure without invoking Codex:

```bash
python3 scripts/run_evals.py --all --dry-run
```

Negative controls (`should_trigger=false`) catch false positives such as prompts that should route
to adjacent skills like code review, blueprint, or test writer.

### Structured Rubric Output (`evals/style-rubric.schema.json`)

Pass this schema to `codex exec --output-schema` for a second qualitative grading pass after the
skill completes.

### Deterministic Runner (`scripts/run_evals.py`)

Runs each case in `evals/evals.json` through `codex exec --json --full-auto`, saves JSONL traces
to `evals/artifacts/`, and applies deterministic checks:

| Check | Description |
|---|---|
| `ran build/test command` | At least one `dotnet build`, `dotnet test`, `make test`, or script validation command |
| `produced rubric output` | Rubric score block detected in the trace |
| `command count within budget` | <= 30 command executions |
| `doc_route.py invoked` | Required for Scenario B (new doc needed) |
| `score_eval.py invoked` | Recommended for all scenarios |

```bash
# Validate infrastructure without running Codex
python3 scripts/run_evals.py --all --dry-run

# Run all cases and check regressions vs baseline
python3 scripts/run_evals.py --all --summary

# Run a single case
python3 scripts/run_evals.py --eval-id 3

# Machine-readable output for CI
python3 scripts/run_evals.py --all --summary --json
```

### Baseline Management (`evals/benchmark_baseline.json`)

Each eval case has an `accepted_pass_rate`. If a run drops more than
`regression_threshold_pp` (default 10) percentage points below baseline, the runner emits a
regression warning.

After intentionally improving the skill, update the baseline:

1. Run `python3 scripts/run_evals.py --all --summary --json` and inspect output quality.
2. Update `accepted_pass_rate` values in `benchmark_baseline.json` to match the verified run.
3. Update `_last_updated`.

### Growing Coverage

Add new rows to `evals/evals.json` and corresponding baselines to
`benchmark_baseline.json` when:

- a prompt that should trigger the skill was observed not triggering it
- a prompt that should not trigger the skill was incorrectly activating it
- a real fix was made to address a skill regression

## Scenario Set

### Scenario A — Code Change + Existing Docs

- "Refactor `<component>` to support `<new behavior>`, keep performance stable, and update docs."

Expected:

- Code change implemented with targeted tests.
- Existing docs updated in-place.
- No contract regressions.

### Scenario B — Code Change + Missing Docs

- "Add `<feature>` and document it; there is no current doc for this area."

Expected:

- Code implemented and validated.
- New doc created in the correct docs subtree.
- Nearest README or index receives a cross-link.

### Scenario C — Performance-Sensitive Path

- "Optimize `<hot path>` without changing behavior and update related docs."

Expected:

- Hot-path analysis noted.
- Blocking, buffering, and allocation risks explicitly addressed.
- Verification commands included.

## Rubric (0-2 Per Category)

Score each category:

- `0 = Missing or incorrect`
- `1 = Partial or unclear`
- `2 = Complete and concrete`

Categories:

1. **Behavior Correctness**
2. **Validation Evidence**
3. **Performance Safety**
4. **Documentation Sync**
5. **Traceable Summary**

### Passing Threshold

- **Pass:** total score >= 8/10 and no category scored 0.
- **Fail:** total score < 8 or any category scored 0.

## Evaluation Report Template

```markdown
### Skill Eval Report

- Scenario: <A|B|C>
- Total Score: <n>/10
- Outcome: <Pass|Fail>

| Category | Score (0-2) | Evidence |
|---|---:|---|
| Behavior Correctness |  |  |
| Validation Evidence |  |  |
| Performance Safety |  |  |
| Documentation Sync |  |  |
| Traceable Summary |  |  |

- Failed checks:
  - <none or list>
- Corrective follow-ups:
  - <none or list>
```

## Quick Failure Heuristics

Automatically fail if any of these occur:

- No validation commands are shown.
- Docs are claimed updated but no file or path is cited.
- New docs are added with no README or index cross-link.
- A performance-sensitive request has no performance discussion.
