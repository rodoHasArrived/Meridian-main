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
- Use `.codex/skills/meridian-implementation-assurance/scripts/run_evals.py` to run deterministic checks against `.codex/skills/meridian-implementation-assurance/evals/evals.json`.

## Prompt-Based Eval Infrastructure

The deterministic evaluation assets currently live in the repo-local Codex skill package at
`.codex/skills/meridian-implementation-assurance/evals/`.

Validate the prompt-set structure without invoking Codex:

```bash
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run
```

Use the Codex eval assets when you need:

- trigger classification coverage
- deterministic regression checks
- artifact capture in JSONL traces
- baseline comparison across revisions

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
