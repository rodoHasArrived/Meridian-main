---
name: meridian-implementation-assurance
description: >
  Implementation assurance skill for Meridian. Use when asked to build or refactor code and also
  prove correctness, performance safety, and documentation synchronization. Triggers on requests
  to "implement with validation", "prevent regressions", "update docs with code", "productionize
  this change", or "ship with tests/performance checks".
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Requires markdown frontmatter
  support and optional Python 3 execution for bundled helper scripts.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Implementation Assurance

Deliver production-ready code changes and leave documentation in a consistent, current state.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)
> **Documentation routing:** [`references/documentation-routing.md`](references/documentation-routing.md)
> **Evaluation harness:** [`references/evaluation-harness.md`](references/evaluation-harness.md)

## Workflow

1. Define requested behavior, risks, and acceptance checks.
2. Identify impacted layers and likely performance-sensitive paths before editing.
3. Implement the smallest safe change set that satisfies the request.
4. Run targeted validation (tests/build/lint) and capture concrete command results.
5. Update related documentation; if missing, add docs in the correct doc area.
6. Run the evaluation harness and report pass/fail with evidence.
7. Summarize code + docs updates and call out residual risk.

## Guardrails

### Correctness
- Preserve existing contracts, nullability expectations, and cancellation flow.
- Keep layer boundaries explicit (UI/service/storage/provider/execution).
- Add or extend tests for happy path, failure path, and cancellation/disposal where relevant.
- Prefer deterministic behavior over timing-sensitive heuristics.

### Performance
- Inspect hot paths for avoidable allocations, synchronous blocking, and unbounded buffering.
- Avoid `.Result`/`.Wait()` on async flows.
- Keep logging and serialization costs proportional to execution frequency.
- When introducing loops or streams, define cancellation and backpressure behavior.

### Documentation
- Update docs in the same PR as code changes when behavior, interfaces, architecture, or operations change.
- Prefer editing an existing doc when one already covers the topic.
- Create new docs only when no suitable home exists.
- For new docs, choose placement via `references/documentation-routing.md` and add index/README cross-links.

## Automation Scripts

- `scripts/doc_route.py` — suggest doc location and filename.
- `scripts/score_eval.py` — score rubric and emit standardized evaluation output.

Examples:

```bash
python3 scripts/doc_route.py --kind architecture --topic "provider orchestration retries"
python3 scripts/score_eval.py --scenario C --scores '{"behavior_correctness":2,"validation_evidence":2,"performance_safety":2,"documentation_sync":2,"traceable_summary":2}'
```

## Output Requirements

Always include:

- Validation command evidence.
- Documentation paths changed or added.
- Performance risk notes for touched hot paths.
- Evaluation report (scenario, category scores, total, pass/fail, follow-ups).
