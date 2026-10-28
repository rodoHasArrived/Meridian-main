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

- Use `scripts/score_eval.py` to validate integer scores and per-category evidence, compute totals,
  and emit a report. It returns 0 for pass, 1 for a failed rubric, and 2 for invalid input.
  Supply `--evidence CATEGORY=TEXT` for all five categories, including a concrete applicability
  reason when a category does not apply. Any `--failed-check` prevents a passing verdict.
- Use `scripts/doc_route.py` before documentation edits when placement is unclear.
- Use `scripts/run_evals.py` to run deterministic checks against `evals/evals.json` cases and compare against `evals/benchmark_baseline.json`.
- Use `scripts/skill_script_advisor.py audit --skill <skill> --summary` before and after adding or optimizing bundled skill scripts.

## Prompt-Based Eval Infrastructure

The `evals/` directory contains a prompt set and structured rubric schema for systematic
regression testing.

### Trigger Classification (`evals/meridian-implementation-assurance.prompts.csv`)

A small CSV of prompts labelled `should_trigger=true/false`. Dry-run checks its structure and label
coverage; it does not invoke a router or measure skill selection accuracy. Measure invocation with
a separate routing or live evaluation when name, description, or routing changes require it.

Validate CSV structure without invoking Codex:

```bash
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run
```

Negative controls (`should_trigger=false`) catch false positives such as prompts that should route
to adjacent skills like code review, blueprint, or test writer.

### Structured Rubric Output (`evals/style-rubric.schema.json`)

Use this schema for a separate qualitative grading pass after the skill completes. Schema validity
alone does not establish correctness: review the evidence behind every score and scenario assertion.

### Deterministic Runner (`scripts/run_evals.py`)

Dry-run is the default setup check. It validates fixture shape and CSV labels without invoking
Codex, scoring behavior, evaluating assertions, or changing the accepted baseline. A successful
exit means the fixtures are usable; it is not a skill-quality score.

Live runs require `--live-run` because prompts can mutate files. Use an isolated worktree or scratch
clone. Treat scenario premises as test inputs to verify against source: stale paths, assertions that
code is already complete, or claims that docs are absent are not authority to invent evidence,
edit archived docs, or create duplicate documentation.

The runner captures `codex exec --json --full-auto` traces in `evals/artifacts/`. Automated checks
look for successful completed validation commands, rubric output from completed agent messages or
successful scorer output, command budget, and execution failure. Helper invocation is advisory.
These are trace heuristics; they do not evaluate the prose assertions in `evals/evals.json`, prove
that a validation command covers the change, or establish that reported evidence is true.
Inspect command results, changed artifacts, and each assertion before making a behavioral verdict.

Run the following from the repository root:

```bash
# Fixture setup only; no agent behavior score
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --summary --json

# Trace checks, followed by manual assertion/evidence review, in an isolated checkout
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --summary --json --live-run

# Regression tests for the local assurance helpers
python -m unittest discover -s tests/scripts -p 'test_implementation_assurance_*.py'
```

### Baseline Management (`evals/benchmark_baseline.json`)

Historical `accepted_pass_rate` values describe automatic trace-check results, not the five-category
rubric or scenario assertion coverage. Keep those measures separate. Changes to the runner's check
set make old and new rates non-comparable. The runner skips regression comparison unless the
baseline declares the current `trace_check_version`; keep the version unchanged only when check
semantics are unchanged. Re-score equivalent traces and review assertions before accepting a new
versioned baseline.

Do not update accepted rates from fixture dry-runs, unit tests of the harness, or unsupported
self-scores. After a comparable live run, review each scenario assertion and evidence artifact,
record the rubric score and trace-check rate separately, and update the baseline only when the
behavioral evidence supports promotion. When no live run is made, report behavior as unmeasured
and preserve the historical baseline.

### Growing Coverage

Add cases to `evals/evals.json` and trigger controls to the CSV when needed. Add or change accepted
baselines only after a reviewed live run. Useful regression sources include:

- a prompt that should trigger the skill was observed not triggering it
- a prompt that should not trigger the skill was incorrectly activating it
- a real fix was made to address a skill regression
- failed validation, nonzero agent exit, or malformed fixtures were previously treated as success
- blocked integrations or mock-only workflows were incorrectly reported as verified

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

### Evidence Challenge Scenarios

Use these during qualitative review; they are not automatically scored by the trace runner.

- A save control updates local state but never persists: require evidence across the affected
  UI/API/storage boundary before marking the criterion verified.
- A financial change passes ordinary cases but breaks rounding, duplicate-event handling, or audit
  history: require tests for the affected invariant and failure path.
- Browser, external service, or Windows validation is unavailable: finish independent work and
  identify unverified criteria, blockers, and the next proof step.
- An unrelated test fails without baseline evidence: report origin as unresolved rather than
  declaring it pre-existing.
- A documentation-only change: use relevant docs/package gates without inventing runtime proof.

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

- **Pass:** total score >= 8/10, no category scored 0, concrete evidence for all categories,
  and no failed checks.
- **Fail:** any of those conditions is unmet. A numeric score cannot override a critical failure.
- This is a reviewed output-quality rubric, not proof of implementation completeness or an automatic
  promotion decision. Record unverified behavior even when the rubric passes.

## Evaluation Report Template

```markdown
### Skill Eval Report

- Scenario: <A|B|C>
- Evidence mode: <reviewed live output | fixture-only; behavior unmeasured>
- Total Score: <n>/10, or not evaluated for fixture-only checks
- Outcome: <Pass|Fail|Not evaluated>

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

- Validation is claimed successful without observed command results.
- Required criteria remain unverified but the output claims full certification.
- Dry-run fixture checks are represented as behavioral evaluation or baseline promotion.
- Docs are claimed updated but no file or path is cited.
- New docs are added with no README or index cross-link.
- A performance-sensitive request has no performance discussion.


## Local Improvement Record — 2026-10-28

- Baseline: commit `838a74d5a7`; this skill, OpenAI metadata, Codex profile, rubric schema,
  evaluation reference, and bundled scorer/runner. Package and script audits passed; dry-run loaded
  five scenarios and 8 positive / 4 negative trigger controls. Stored accepted rates are 0.80;
  their behavioral evidence was not re-established in this task.
- Feedback: source review demonstrated that failed validation commands and nonzero Codex exits
  could pass trace checks; rubric scoring could pass explicit failures or missing evidence and
  coerce non-integer scores. Dry-run output did not distinguish fixture checks from behavior proof.
- Candidate, iteration 1: acceptance-to-evidence guidance, financial and integration checks,
  explicit incomplete-proof states, fail-closed scorer and runner, versioned trace comparisons,
  synchronized Codex metadata/catalog rows, and focused helper regression tests.
- Deterministic evidence: 28 tests pass with
  `python -m unittest discover -s tests/scripts -p 'test_implementation_assurance_*.py'`.
  Fixture dry-run, package audit, script audit, Codex skill checker, AI inventory, package validation,
  prompt-route linter, AI contract drift, and TOML parsing pass.
- Behavioral evaluation: not run; aggregate behavioral score is **not evaluated**. The reviewed
  rubric target remains >= 8/10, no zero category, complete evidence, and no failed checks.
  No live baseline is promoted. Trace assertions still require review in an isolated live run.
- Broader gates: `python build/scripts/ai-architecture-check.py --src src/ check` reports 117
  critical findings and 930 warnings on unchanged source inputs (verified against `HEAD`). Full
  `bash scripts/ci.sh` is tracked separately; do not infer repository-wide readiness from the
  package tests. The separate `ai-verify` wrapper was not repeated because it duplicates the
  build/test work of that CI entry point.
- Rollback: revert only this revision's skill, helper, test, profile, metadata, and catalog changes.
  The historical benchmark file and other hosts' skill packages remain unchanged.
