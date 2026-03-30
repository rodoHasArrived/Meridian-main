---
name: meridian-implementation-assurance
description: >
  Implementation assurance and evidence collection skill for Meridian. Use when a change needs
  to prove it matches the approved blueprint/requirements, with explicit test evidence, doc
  routing, and a traceable summary. Triggers on requests to certify completeness, confirm scope
  alignment, gather rollout evidence, or update AI/agent catalogs after new capabilities land.
license: See repository LICENSE
last_updated: 2026-03-30
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files plus the
  bundled scripts for doc routing and lightweight evaluation scoring. No external network access
  required.
metadata:
  owner: meridian-ai
  version: "1.1"
  spec: open-agent-skills-v1
---
# Meridian — Implementation Assurance Skill

Assure that a change is correctly implemented, documented, and verifiable. This skill drives
end-to-end evidence collection: requirement mapping, workflow adherence, test/documentation
proof, and routing updates to the right AI catalogs.

> **GitHub Copilot equivalent:** [`.github/agents/implementation-assurance-agent.md`](../../../.github/agents/implementation-assurance-agent.md)
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)
> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)

---

## Definition of Done

A task delivered by this skill is complete when **all** of the following are true:

- **Build passes:** at least one of `dotnet build` or `dotnet test` targeting the touched project runs without errors.
- **Tests cover the change:** happy path, failure path, and cancellation/disposal exist or are cited as a gap.
- **Validation evidence is explicit:** the final response includes exact commands and their pass/fail results.
- **Documentation is in sync:** existing docs covering the changed behavior are updated in-place, or a new doc is created in the correct subtree with a cross-link from the nearest index.
- **Rubric score ≥ 8/10, no category at 0:** `scripts/score_eval.py` is run and the report is included in the response.
- **Performance-sensitive paths are annotated:** any hot-path touched by the change includes an explicit note on allocation, async, or buffering risk.
- **Summary is traceable:** the closing summary links requirement → files changed → validation artifact → doc update.

---

## Integration Pattern

Follow this 4-step loop for every implementation-assurance task:

### 1 — GATHER CONTEXT
- Identify the source of truth: blueprint / roadmap item / issue requirements.
- Capture acceptance criteria, success metrics, and any mandated evidence.
- Determine the scenario: **A** (code + existing docs), **B** (code + missing docs), or **C** (performance-sensitive).
- Run `doc_route.py` to confirm which AI/agent catalog pages must be updated for discoverability.

### 2 — PLAN & TRACE
- Map each requirement to its implementing file(s) and validation artifact(s) (tests, CLI runs).
- Decide the validation lane (fast sanity vs. full regression) and required doc touchpoints.
- If scoring is needed, choose a scenario (`A`, `B`, or `C`) and scoring weights for evaluation.

### 3 — EXECUTE & VERIFY
- Perform the minimal changes to satisfy the mapped criteria.
- Run the relevant tests/builds; collect logs, command lines, and outcomes.
- For **Scenario C**: explicitly discuss allocation before/after, confirm no `.Result`/`.Wait()` introduced.
- Use `score_eval.py` to summarize evaluation scores with JSON-ready output for audit trails.

### 4 — REPORT & ROUTE
- Produce a traceable summary: requirement ↔ implementation ↔ evidence.
- Include validation commands, outcomes, and links to updated docs/agents.
- Update AI/agent catalogs and skills indexes per `doc_route.py` guidance.

---

## Guardrails & Required Evidence

- **Traceability:** Every requirement must reference the files changed and the validation artifact.
- **Validation:** Prefer existing tests; if none exist, state the gap and suggest coverage.
- **Documentation:** Update the appropriate AI/agent catalogs when capabilities change.
- **Safety:** No scope creep; defer unrelated refactors. Avoid destructive commands.
- **Output:** Provide a concise checklist with pass/fail status and command transcripts for validation.

---

## Correctness Guardrails

- Preserve existing contracts, nullability expectations, and cancellation flow.
- Keep layer boundaries explicit (UI/service/storage/provider/execution).
- Add or extend tests for happy path, failure path, and cancellation/disposal where relevant.
- Prefer deterministic behavior over timing-sensitive heuristics.

---

## Performance Guardrails

- Inspect hot paths for avoidable allocations, synchronous blocking, and unbounded buffering.
- Avoid `.Result`/`.Wait()` on async flows.
- Keep logging and serialization costs proportional to execution frequency.
- When introducing loops or streams, define cancellation and backpressure behavior.

---

## Documentation Synchronization Rules

- Update docs in the same PR as code changes when behavior, interfaces, architecture, or operations change.
- Prefer editing an existing doc when one already covers the topic.
- Create new docs only when no suitable home exists.
- For new docs, choose placement using `references/documentation-routing.md` and add cross-links from the nearest index/README.
- Keep documentation concrete: what changed, why, and how to use/operate it.

---

## On-Demand References

Load these only when the task requires the deeper context they provide:

- `references/documentation-routing.md` — routing matrix for placing doc updates in the correct
  `docs/` subtree and quality bar for doc changes.
- `references/evaluation-harness.md` — rubric definitions (Scenarios A/B/C), per-category scoring
  guide, passing threshold, eval infrastructure, and the canonical eval-report template.

---

## Bundled Scripts

### `scripts/doc_route.py`

Routes AI/agent-related updates to the correct catalog pages.

Usage:
```bash
python3 .claude/skills/meridian-implementation-assurance/scripts/doc_route.py \
  --kind ai --topic "agent routing update"
```

Outputs a destination hint (e.g., docs/ai/agents catalog vs. skills catalog) plus a rationale.
Choices: `ai`, `skill`, `agent`, `workflow`.

### `scripts/score_eval.py`

Lightweight evaluator for summarizing assurance scores.

Usage:
```bash
python3 .claude/skills/meridian-implementation-assurance/scripts/score_eval.py \
  --scenario A \
  --scores '{"behavior_correctness":2,"validation_evidence":2,"performance_safety":2,"documentation_sync":2,"traceable_summary":2}' \
  --json
```

Produces totals, averages, and a verdict string. Use the `--json` flag for machine-readable output.

---

## Evaluation Requirement

Treat `references/evaluation-harness.md` as mandatory for this skill. Always return:

- Which scenario was evaluated (A/B/C).
- Rubric scores by category.
- Failing checks and corrective follow-ups.
- Exact command evidence for tests/build checks.

---

## Evidence Template (recommended)

```
Requirement ↔ Implementation ↔ Evidence
- R1: <statement> → <files touched> → <test/log/doc link>
- R2: ...

Validation Commands
- dotnet build ... (pass/fail, duration)
- dotnet test ... (pass/fail, duration)
- python scripts/... (pass/fail, key output)

Documentation Updates
- Updated: docs/ai/skills/README.md (added skill entry)
- Updated: docs/ai/agents/README.md (symmetry map)
```

Keep the summary under 15 lines; link to detailed artifacts only as needed.

---

## Output Checklist

Before finishing, confirm:

- [ ] Scope/requirements restated and scenario (A/B/C) identified
- [ ] Requirement → implementation → evidence matrix produced
- [ ] Validation commands + results (build/tests/scripts) with pass/fail
- [ ] Performance-sensitive changes reviewed with explicit notes
- [ ] Docs updated or newly added in the correct location
- [ ] Evaluation harness completed with a rubric score summary (≥ 8/10, no category at 0)
- [ ] Final traceable summary (≤15 lines) with validation commands and any residual risk

