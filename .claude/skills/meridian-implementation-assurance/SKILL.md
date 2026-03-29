---
name: meridian-implementation-assurance
description: >
  Implementation assurance and evidence collection skill for Meridian. Use when a change needs
  to prove it matches the approved blueprint/requirements, with explicit test evidence, doc
  routing, and a traceable summary. Triggers on requests to certify completeness, confirm scope
  alignment, gather rollout evidence, or update AI/agent catalogs after new capabilities land.
license: See repository LICENSE
last_updated: 2026-03-29
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files plus the
  bundled scripts for doc routing and lightweight evaluation scoring. No external network access
  required.
metadata:
  owner: meridian-ai
  version: "1.0"
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

## Integration Pattern

Follow this 4-step loop for every implementation-assurance task:

### 1 — GATHER CONTEXT
- Identify the source of truth: blueprint / roadmap item / issue requirements.
- Capture acceptance criteria, success metrics, and any mandated evidence.
- Run `doc_route.py` to confirm which AI/agent catalog pages must be updated for discoverability.

### 2 — PLAN & TRACE
- Map each requirement to its implementing file(s) and validation artifact(s) (tests, CLI runs).
- Decide the validation lane (fast sanity vs. full regression) and required doc touchpoints.
- If scoring is needed, choose a scenario (`A`, `B`, or `C`) and scoring weights for evaluation.

### 3 — EXECUTE & VERIFY
- Perform the minimal changes to satisfy the mapped criteria.
- Run the relevant tests/builds; collect logs, command lines, and outcomes.
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

## On-Demand References

Load these only when the task requires the deeper context they provide:

- `references/documentation-routing.md` — routing matrix for placing doc updates in the correct
  `docs/` subtree and quality bar for doc changes.
- `references/evaluation-harness.md` — rubric definitions (Scenarios A/B/C), per-category scoring
  guide, passing threshold, and the canonical eval-report template.

---

## Bundled Scripts

### `scripts/doc_route.py`

Routes AI/agent-related updates to the correct catalog pages.

Usage:
```bash
python3 .claude/skills/meridian-implementation-assurance/scripts/doc_route.py \
  --kind ai --topic "agent routing update"
```

Outputs a short destination hint (e.g., docs/ai/agents catalog vs. skills catalog) plus a rationale.

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
