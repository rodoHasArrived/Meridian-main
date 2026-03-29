---
name: meridian-implementation-assurance
description: Deliver production-ready Meridian changes with correctness checks, performance guardrails, doc synchronization, and rubric-based self-evaluation. Use when a change must be verified, certified, or rolled out with traceable evidence.
triggers:
  - "build or refactor code and verify behavior"
  - "certify completeness or confirm scope alignment"
  - "gather rollout evidence before merging"
  - "update AI/agent catalogs after new capabilities land"
  - "prove a change matches approved blueprint or requirements"
---

# Meridian Implementation Assurance

Deliver production-ready code changes and leave documentation in a consistent, current state.

Read `../_shared/project-context.md` before coding. Read `references/documentation-routing.md` before writing docs. Read `references/evaluation-harness.md` before finalizing output.

## Definition of Done

A task delivered by this skill is complete when **all** of the following are true:

- **Build passes**: at least one of `dotnet build` or `dotnet test` targeting the touched project runs without errors.
- **Tests cover the change**: tests for happy path, failure path, and cancellation/disposal exist or are cited as a gap.
- **Validation evidence is explicit**: the final response includes exact commands and their pass/fail results.
- **Documentation is in sync**: existing docs covering the changed behavior are updated in-place, or a new doc is created in the correct subtree with a cross-link from the nearest index.
- **Rubric score >= 8/10, no category at 0**: `scripts/score_eval.py` is run and the report is included in the response.
- **Performance-sensitive paths are annotated**: any hot-path touched by the change includes an explicit note on allocation, async, or buffering risk.
- **Summary is traceable**: the closing summary links requirement → files changed → validation artifact → doc update.

## Workflow

1. Define requested behavior, risks, and acceptance checks.
2. Identify impacted layers and likely performance-sensitive paths before editing.
3. Implement the smallest safe change set that satisfies the request.
4. Run targeted validation (tests/build/lint) and capture concrete command results.
5. Update related documentation; if missing, add docs in the correct doc area.
6. Run the evaluation harness and report pass/fail with evidence.
7. Summarize code + docs updates and call out residual risk.

## Correctness Guardrails

- Preserve existing contracts, nullability expectations, and cancellation flow.
- Keep layer boundaries explicit (UI/service/storage/provider/execution).
- Add or extend tests for happy path, failure path, and cancellation/disposal where relevant.
- Prefer deterministic behavior over timing-sensitive heuristics.

## Performance Guardrails

- Inspect hot paths for avoidable allocations, synchronous blocking, and unbounded buffering.
- Avoid `.Result`/`.Wait()` on async flows.
- Keep logging and serialization costs proportional to execution frequency.
- When introducing loops or streams, define cancellation and backpressure behavior.

## Documentation Synchronization Rules

- Update docs in the same PR as code changes when behavior, interfaces, architecture, or operations change.
- Prefer editing an existing doc when one already covers the topic.
- Create new docs only when no suitable home exists.
- For new docs, choose placement using `references/documentation-routing.md` and add cross-links from the nearest index/README.
- Keep documentation concrete: what changed, why, and how to use/operate it.


## Automation Scripts

Use bundled scripts to keep execution fast and consistent:

- `scripts/doc_route.py`: recommend documentation location + filename and whether cross-linking is required.
  - Example: `python3 scripts/doc_route.py --kind architecture --topic "provider orchestration retries"`
- `scripts/score_eval.py`: compute rubric totals and generate a standardized eval report.
  - Example: `python3 scripts/score_eval.py --scenario C --scores '{"behavior_correctness":2,"validation_evidence":2,"performance_safety":2,"documentation_sync":1,"traceable_summary":2}'`
- `scripts/run_evals.py`: run the deterministic eval harness against `evals/evals.json` cases.
  - Dry-run (validate setup): `python3 scripts/run_evals.py --all --dry-run`
  - Single case: `python3 scripts/run_evals.py --eval-id 1`
  - All cases with regression check: `python3 scripts/run_evals.py --all --summary`

Run these scripts from the skill directory or with full paths.

## Evaluation Requirement

Treat `references/evaluation-harness.md` as mandatory for this skill. Always return:

- Which scenario was evaluated.
- Rubric scores by category.
- Failing checks and corrective follow-ups.
- Exact command evidence for tests/build checks.

## Output Checklist

Before finishing, confirm:

- Code compiles or tests pass for the touched surface.
- Performance-sensitive changes were reviewed with explicit notes.
- Docs were updated (or newly added in the correct location).
- Evaluation harness was completed with a rubric score summary.
- Summary includes validation commands and any residual risk.
