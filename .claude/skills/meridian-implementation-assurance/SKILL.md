---
name: meridian-implementation-assurance
description: >
  Implementation assurance and evidence collection skill for Meridian. Use when a change needs
  to prove it matches the approved blueprint or requirements, with explicit test evidence, doc
  routing, and a traceable summary. Trigger on requests to certify completeness, confirm scope
  alignment, gather rollout evidence, or update AI and agent catalogs after new capabilities land.
license: See repository LICENSE
last_updated: 2026-04-13
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files plus the
  bundled scripts for doc routing and lightweight evaluation scoring. No external network access
  required.
metadata:
  owner: meridian-ai
  version: "1.3"
  spec: open-agent-skills-v1
---

# Meridian — Implementation Assurance Skill

Assure that a change is correctly implemented, documented, and verifiable. This skill drives
end-to-end evidence collection: requirement mapping, workflow adherence, test and documentation
proof, and routing updates to the right AI catalogs.

> **GitHub Copilot equivalent:** [`.github/agents/implementation-assurance-agent.md`](../../../.github/agents/implementation-assurance-agent.md)
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)
> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)

## Definition Of Done

A task delivered by this skill is complete when **all** of the following are true:

- **Build passes:** at least one of `dotnet build` or `dotnet test` targeting the touched project runs without errors.
- **Tests cover the change:** happy path, failure path, and cancellation or disposal are covered or explicitly cited as gaps.
- **Validation evidence is explicit:** the final response includes exact commands and their pass or fail results.
- **Documentation is in sync:** existing docs covering the changed behavior are updated in-place, or a new doc is created in the correct subtree with a cross-link from the nearest index.
- **Rubric score >= 8/10, no category at 0:** `scripts/score_eval.py` is run and the report is included in the response.
- **Performance-sensitive paths are annotated:** any hot path touched by the change includes an explicit note on allocation, async, or buffering risk.
- **Summary is traceable:** the closing summary links requirement -> files changed -> validation artifact -> doc update.

## Integration Pattern

Follow this 4-step loop for every implementation-assurance task:

### 1 — Gather Context

- Identify the source of truth: blueprint, roadmap item, or issue requirements.
- Capture acceptance criteria, success metrics, and any mandated evidence.
- Determine the scenario: **A** (code + existing docs), **B** (code + missing docs), or **C** (performance-sensitive).
- Run `doc_route.py` to confirm which AI and agent catalog pages must be updated for discoverability.

### 2 — Plan And Trace

- Map each requirement to its implementing file(s) and validation artifact(s).
- Decide the validation lane and required doc touchpoints.
- If scoring is needed, choose a scenario (`A`, `B`, or `C`) and scoring weights.

### 3 — Execute And Verify

- Perform the minimal changes to satisfy the mapped criteria.
- Run the relevant tests and builds; collect commands, logs, and outcomes.
- For **Scenario C**, explicitly discuss allocation before and after, and confirm no `.Result` or `.Wait()` was introduced.
- Use `score_eval.py` to summarize evaluation scores with JSON-ready output for audit trails.

### 4 — Report And Route

- Produce a traceable summary: requirement <-> implementation <-> evidence.
- Include validation commands, outcomes, and links to updated docs or agents.
- Update AI and agent catalogs per `doc_route.py` guidance.

## Requirement Type Detection

Use this decision tree before starting any task to pick the right validation lane:

```text
What are you assuring?
|-- Feature completeness vs. blueprint/acceptance criteria
|   -> Lane: requirement matrix + targeted unit/integration tests
|-- Scope alignment to an issue or roadmap item
|   -> Lane: requirement matrix + file mapping + acceptance criteria check
|-- Documentation sync after a code change
|   -> Lane: doc routing matrix + cross-reference validation
|-- Capability discovery / AI catalog update
|   -> Lane: agent/skill symmetry check (docs/ai/agents/ + docs/ai/skills/)
`-- Rollout readiness
    -> Lane: build gate + test gate + deployment gates (all CRITICAL)
```

## Skill-Authoring Lane

Use this lane whenever the task creates or updates a skill or agent package.

- Inspect only the relevant Meridian instinct files when local learned behavior would help, and verify each instinct against the repository before turning it into instructions.
- Keep the main skill file concise and imperative. Put detailed material in `references/`, deterministic helpers in `scripts/`, and output resources in `assets/`.
- Preserve host-specific metadata rules. For Codex repo-local skills, keep frontmatter to `name` and `description`. For portable Claude packages, preserve the metadata required by that host.
- Keep mirrored Codex, Claude, and GitHub agent guidance aligned when a shared workflow or policy changes.
- Avoid auxiliary docs in skill folders unless they directly support execution or the host format requires them.
- When `agents/openai.yaml` exists, regenerate or update it so the UI metadata still matches the skill text.
- Validate package shape after editing and run representative checks for any added or changed scripts.

## Guardrails And Required Evidence

- **Traceability:** every requirement must reference the files changed and the validation artifact.
- **Validation:** prefer existing tests; if none exist, state the gap and suggest coverage.
- **Documentation:** update the appropriate AI and agent catalogs when capabilities change.
- **Host-runtime semantics:** when configuration, persistence, or upgrade-path behavior changes, update the affected `docs/ai/*` pages, shared project context, and mirrored host packages in the same change so agents do not retain stale path guidance.
- **Skill packaging:** record validator output and metadata synchronization when skill files change.
- **Safety:** no scope creep; defer unrelated refactors. Avoid destructive commands.

## On-Demand References

Load these only when the task requires the deeper context they provide:

- `references/documentation-routing.md`
- `references/evaluation-harness.md`

## Bundled Scripts

- `scripts/doc_route.py`
- `scripts/score_eval.py`

## Output Checklist

- [ ] scope or requirements restated and scenario (A/B/C) identified
- [ ] requirement -> implementation -> evidence matrix produced
- [ ] validation commands and results included
- [ ] performance-sensitive changes reviewed with explicit notes
- [ ] docs updated or newly added in the correct location
- [ ] evaluation harness completed with a rubric score summary (>= 8/10, no category at 0)
- [ ] final traceable summary kept to 15 lines or fewer
