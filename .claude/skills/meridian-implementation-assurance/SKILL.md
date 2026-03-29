---
name: meridian-implementation-assurance
description: >
  Implement Meridian changes with built-in correctness checks, performance guardrails,
  documentation synchronization, and structured self-evaluation. Use when an implementation or
  refactor request must also prove behavior with validation evidence, avoid performance regressions,
  and keep docs in sync.
license: See repository LICENSE
last_updated: 2026-03-29
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files and may
  execute bundled Python 3 helper scripts for deterministic documentation routing and rubric scoring.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---
# Meridian Implementation Assurance

Deliver production-ready code changes and leave documentation in a consistent, current state.

> **GitHub Copilot equivalent:** [`.github/agents/implementation-assurance-agent.md`](../../../.github/agents/implementation-assurance-agent.md)
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)
> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md) — authoritative stats, paths, and architecture anchors.

Read `references/documentation-routing.md` before doc updates. Read `references/evaluation-harness.md` before final output.

## Workflow (Required)

1. Define requested behavior, risks, and acceptance checks.
2. Identify impacted layers and likely performance-sensitive paths before editing.
3. Implement the smallest safe change set that satisfies the request.
4. Run targeted validation (tests/build/lint) and capture exact command evidence.
5. Update related documentation; if missing, add docs in the correct doc area.
6. Run the evaluation harness and report pass/fail with rubric evidence.
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
- For new docs, choose placement with `references/documentation-routing.md` and add cross-links from the nearest index/README.
- Keep documentation concrete: what changed, why, and how to use/operate it.

## Scripts

- `scripts/doc_route.py`: recommends doc location/filename and whether cross-linking is required.
  - Example: `python3 scripts/doc_route.py --kind ai --topic "agent routing update"`
- `scripts/score_eval.py`: scores rubric categories and emits a standardized eval report.
  - Example: `python3 scripts/score_eval.py --scenario A --scores '{"behavior_correctness":2,"validation_evidence":2,"performance_safety":2,"documentation_sync":2,"traceable_summary":2}'`

## Evaluation Requirement

Treat `references/evaluation-harness.md` as mandatory. Always return:

- Evaluated scenario.
- Rubric scores by category.
- Failing checks and corrective follow-ups.
- Exact command evidence for tests/build checks.
