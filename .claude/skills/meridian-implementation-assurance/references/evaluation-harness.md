# Evaluation Harness

Use this harness to evaluate whether outputs from `meridian-implementation-assurance` are complete, correct, and operationally safe.

## How to Run the Eval

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

The `evals/` directory contains a prompt set and structured rubric schema for systematic regression testing using `codex exec`.

### Trigger Classification (`evals/meridian-implementation-assurance.prompts.csv`)

A small CSV of prompts labelled `should_trigger=true/false`. Use this to verify that changes to the skill name or description don't break invocation:

| Column | Description |
|---|---|
| `id` | Unique case identifier |
| `should_trigger` | `true` if the prompt should invoke this skill; `false` if it should not |
| `prompt` | The prompt text to send to the agent |

Use `python3 scripts/run_evals.py --all --dry-run` to validate CSV structure without invoking `codex exec`.

**Negative controls** (`should_trigger=false`) catch false positives — prompts that match adjacent skills (code-review, blueprint, test-writer) but should not invoke implementation assurance.

### Structured Rubric Output (`evals/style-rubric.schema.json`)

Pass this schema to `codex exec --output-schema` for a second qualitative grading pass after the skill completes:

```shell
codex exec \
  "Evaluate this implementation-assurance output against the rubric:
   - behavior_correctness: change satisfies request, contracts preserved
   - validation_evidence: exact commands and pass/fail included
   - performance_safety: hot-path risks identified and handled
   - documentation_sync: docs updated or new doc in correct subtree
   - traceable_summary: requirement linked to implementation and evidence
   Return structured rubric result." \
  --output-schema ./evals/style-rubric.schema.json \
  -o ./evals/artifacts/rubric-result.json
```

The schema enforces `overall_pass`, `score` (0-10), `scenario`, and one `checks` entry per category.

### Deterministic Runner (`scripts/run_evals.py`)

Runs each case in `evals/evals.json` through `codex exec --json --full-auto`, saves JSONL traces to `evals/artifacts/`, and applies deterministic checks:

| Check | Description |
|---|---|
| `ran build/test command` | At least one `dotnet build`, `dotnet test`, `make test`, or script validation command |
| `produced rubric output` | Rubric score block detected in the trace |
| `command count within budget` | ≤ 30 command executions (guards against thrashing) |
| `doc_route.py invoked` | Required for Scenario B (new doc needed) |
| `score_eval.py invoked` | Recommended for all scenarios |

```shell
# Validate infrastructure without running codex
python3 scripts/run_evals.py --all --dry-run

# Run all cases and check regressions vs baseline
python3 scripts/run_evals.py --all --summary

# Run a single case
python3 scripts/run_evals.py --eval-id 3

# Machine-readable output for CI
python3 scripts/run_evals.py --all --summary --json
```

Traces are saved to `evals/artifacts/eval-<id>.jsonl` for inspection. Open them to see every `command_execution` event in order — regressions become immediately explainable.

### Baseline Management (`evals/benchmark_baseline.json`)

Each eval case has an `accepted_pass_rate`. If a run drops more than `regression_threshold_pp` (default 10) percentage points below baseline, the runner emits a regression warning.

After intentionally improving the skill, update the baseline:
1. Run `python3 scripts/run_evals.py --all --summary --json` and inspect output quality.
2. Update `accepted_pass_rate` values in `benchmark_baseline.json` to match the verified run.
- Use `.codex/skills/meridian-implementation-assurance/scripts/run_evals.py` to run deterministic checks against eval cases and compare against the baseline.

## Prompt-Based Eval Infrastructure

The `.codex/skills/meridian-implementation-assurance/evals/` directory contains a prompt set and
structured rubric schema for systematic regression testing.

### Trigger Classification

A CSV of prompts labelled `should_trigger=true/false` lives at
`.codex/skills/meridian-implementation-assurance/evals/meridian-implementation-assurance.prompts.csv`.
Use it to verify that changes to the skill name or description don't break invocation.

**Negative controls** (`should_trigger=false`) catch false positives — prompts that match adjacent
skills (code-review, blueprint, test-writer) but should not invoke implementation assurance.

Validate CSV structure without invoking `codex exec`:

```bash
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run
```

### Structured Rubric Output

A JSON Schema at `.codex/skills/meridian-implementation-assurance/evals/style-rubric.schema.json`
enforces `overall_pass`, `score` (0-10), `scenario`, and one `checks` entry per rubric category.
Pass it to `codex exec --output-schema` for a second qualitative grading pass.

### Deterministic Runner

Runs each case in `evals/evals.json` through `codex exec --json --full-auto`, saves JSONL traces
to `evals/artifacts/`, and applies deterministic checks:

| Check | Description |
|---|---|
| `ran build/test command` | At least one `dotnet build`, `dotnet test`, `make test`, or script validation |
| `produced rubric output` | Rubric score block detected in the trace |
| `command count within budget` | ≤ 30 command executions |
| `doc_route.py invoked` | Required for Scenario B (new doc needed) |
| `score_eval.py invoked` | Recommended for all scenarios |

```bash
# Validate infrastructure without running codex
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run

# Run all cases and check regressions vs baseline
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --summary

# Run a single case
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --eval-id 3

# Machine-readable output for CI
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --summary --json
```

### Baseline Management

Each eval case has an `accepted_pass_rate` in
`.codex/skills/meridian-implementation-assurance/evals/benchmark_baseline.json`.
If a run drops more than `regression_threshold_pp` (default 10 pp) below baseline, the runner
emits a regression warning.

After intentionally improving the skill, update the baseline:
1. Run `python3 scripts/run_evals.py --all --summary --json` and inspect output quality.
2. Update `accepted_pass_rate` values to match the verified run.
3. Update `_last_updated`.

### Growing Coverage

Add new rows to `evals/evals.json` and corresponding baselines to `benchmark_baseline.json` when:
- A prompt that should trigger the skill was observed **not** triggering it.
- A prompt that should **not** trigger the skill was incorrectly activating it.
- A real fix was made to address a skill regression.

Every manual correction to the skill is a candidate for a new eval case so the behavior is locked in.

## Scenario Set

### Scenario A — Code Change + Existing Docs

Prompt pattern:

- "Refactor `<component>` to support `<new behavior>`, keep performance stable, and update docs."

Expected:

- Code change implemented with targeted tests.
- Existing docs updated in-place.
- No contract regressions.

### Scenario B — Code Change + Missing Docs

Prompt pattern:

- "Add `<feature>` and document it; there is no current doc for this area."

Expected:

- Code implemented and validated.
- New doc created in correct docs subtree.
- Nearest README/index receives cross-link.

### Scenario C — Performance-Sensitive Path

Prompt pattern:

- "Optimize `<hot path>` without changing behavior and update related docs."

Expected:

- Hot-path analysis noted.
- Blocking/buffering/allocation risks explicitly addressed.
- Verification commands included.

## Rubric (0-2 per category)

Score each category:

- **0 = Missing/incorrect**
- **1 = Partial/unclear**
- **2 = Complete and concrete**

Categories:

1. **Behavior Correctness**
   - Change satisfies requested behavior.
   - Contracts/cancellation/nullability preserved.
2. **Validation Evidence**
   - Includes exact commands and results.
   - Tests/build checks map to touched areas.
3. **Performance Safety**
   - Hot-path risks are identified.
   - Async blocking/unbounded buffering risks are handled.
4. **Documentation Sync**
   - Existing docs updated when applicable.
   - New docs created only when needed and routed correctly.
5. **Traceable Summary**
   - Final summary links code, docs, validation, and residual risks.

### Passing Threshold

- **Pass:** total score >= 8/10 and no category scored 0.
- **Fail:** total score < 8 or any category scored 0.

## Evaluation Report Template

Use this exact structure in the final response when running an eval:

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
- Docs are claimed updated but no file/path is cited.
- New docs are added with no README/index cross-link.
- Performance-sensitive request has no performance discussion.

