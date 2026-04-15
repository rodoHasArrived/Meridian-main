---
name: meridian-simulated-user-panel
description: >
  Simulate structured Meridian product feedback from multiple realistic user personas such as
  quantitative analysts, fund managers, fund accountants, fund operators, hobbyists, individual
  traders, academics, compliance leads, data engineers, support and onboarding leads,
  implementation consultants, and owner-operators. Use when the user asks for simulated user
  testing, persona-based critique, multi-role reactions, likes and dislikes, release-gate review,
  usability-lab benchmarking, adoption-risk analysis, workflow-fit analysis, or improvement ideas
  for Meridian screens, workflows, documents, roadmap directions, or overall product strategy.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Provides manifest-driven review
  instructions, bundle templates, eval fixtures, and a deterministic local scoring harness.
metadata:
  owner: meridian-ai
  version: "2.0"
  spec: open-agent-skills-v1
---
# Meridian Simulated User Panel

Run artifact-driven Meridian product reviews that can behave as a `design_partner`,
`release_gate`, or `usability_lab` depending on the request.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md) —
> read before making claims about current Meridian capabilities or product direction.
> **GitHub agent equivalent:** [`.github/agents/simulated-user-panel-agent.md`](../../../.github/agents/simulated-user-panel-agent.md)
> **Claude agent equivalent:** [`.claude/agents/meridian-user-panel.md`](../../../.claude/agents/meridian-user-panel.md)

## Reference Files

Read these `references/` files on demand:

- `references/review-contract.md` — stable manifest fields, output contract, rubric, and verdict
  rules
- `references/personas.md` — tagged persona panels, role pressures, and pairing guidance
- `references/review-modes.md` — invocation modes, focus areas, and artifact-type defaults
- `references/artifact-bundles.md` — how to use screenshots, workflow manifests, and smoke output
- `references/sample-prompts.md` — ready-to-use prompt starters for each mode

## Assets

Use these machine-readable assets when building or validating a review:

- `assets/review-manifest.schema.json`
- `assets/eval-result.schema.json`
- `assets/bundles/screen-review.manifest.json`
- `assets/bundles/workflow-walkthrough.manifest.json`
- `assets/bundles/roadmap-review.manifest.json`
- `assets/bundles/ship-readiness.manifest.json`

## Eval Resources

Use these when testing or improving the skill:

- `evals/evals.json` — mode-by-artifact eval matrix
- `evals/benchmark_baseline.json` — accepted pass-rate floor per eval
- `agents/grader.md` — grading instructions for judging whether a run produced grounded,
  owner-minded output
- `scripts/run_eval.py` — manifest-aware helper for materializing, scoring, aggregating, and
  comparing eval runs

## Core Rules

- Inspect the most concrete artifact available before simulating reactions.
- Keep every persona owner-minded: each role should care about Meridian's future quality, trust,
  support burden, and strategic coherence.
- Separate verified repo evidence from inferred user reaction.
- Prefer concrete, buildable recommendations over generic product advice.
- Use at least 4 personas when the request asks for a panel.

## Workflow

1. Validate or construct the review manifest.
2. Inspect the artifact bundle: code, XAML, screenshots, workflow manifests, smoke-test notes,
   docs, or roadmap text.
3. Choose the right invocation mode and focus areas.
4. Build the panel from `references/personas.md`, honoring user-specified roles exactly.
5. For each persona, evaluate first reaction, core task, trust check, adoption decision, and
   owner-minded improvement push.
6. Score the six rubric dimensions with 1-5 evidence-backed ratings:
   - Workflow Fit
   - Trust / Controls
   - Time-to-Value
   - Data Confidence
   - Extensibility
   - Learning Curve
7. End with the shared output contract from `references/review-contract.md`.

## Output Contract

Use these headings unless the host or user explicitly asks for another format:

- `Executive Summary`
- `Panel`
- `Persona Findings`
- `Cross-Persona Tensions`
- `Owner Actions`
- `Release Recommendation`
- `Confidence Notes`

Within `Persona Findings`, every persona must include:

- `Liked`
- `Didn't like`
- `Missing or risky`
- `Owner-minded improvement ideas`
- `Adoption verdict`
- `Rubric (1-5 with evidence)` across the six required dimensions

Within `Owner Actions`, group recommendations as:

- `Now`
- `Next`
- `Later`

Within `Confidence Notes`, always separate:

- `Verified`
- `Inferred`
- `Missing evidence`

## Scripts

Typical commands:

```bash
python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py list
python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py materialize --eval-id 2
python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py score --workspace .claude/skills/meridian-simulated-user-panel/tmp-evals/eval-02-provider-onboarding-release-gate
python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py aggregate --input .claude/skills/meridian-simulated-user-panel/tmp-evals
python .claude/skills/meridian-simulated-user-panel/scripts/run_eval.py compare-baseline --aggregate .claude/skills/meridian-simulated-user-panel/tmp-evals/aggregate.json
```

## Quality Bar

- In `release_gate`, separate blockers from polish and end with `ship`, `ship_with_caveats`, or
  `hold`.
- In `usability_lab`, surface repeated complaint clusters and note whether the run is strong
  enough to compare against prior results.
- If the artifact bundle is weak or incomplete, say so plainly and treat that as product evidence.
