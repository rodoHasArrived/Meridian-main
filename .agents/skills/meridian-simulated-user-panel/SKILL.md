---
name: meridian-simulated-user-panel
description: >
  Critique concrete Meridian screens, workflows, documents, roadmaps, and release candidates with
  artifact-grounded multi-persona panels. Use for simulated user testing, persona-based product
  critique, multi-role reactions, workflow-fit and adoption-risk analysis, usability-lab
  comparison, or advisory release-gate feedback. Do not use as a substitute for real user research,
  for code correctness review, for unconstrained brainstorming without an artifact, or for final
  roadmap sequencing.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Provides manifest-driven review
  instructions, bundle templates, eval fixtures, and a deterministic local scoring harness.
metadata:
  owner: meridian-ai
  version: "3.0"
  spec: open-agent-skills-v1
---

# Meridian Simulated User Panel

Run evidence-led product reviews in `design_partner`, `release_gate`, or `usability_lab` mode.
Treat every reaction as a simulation, never as observed user research.

Read `../_shared/project-context.md` before making current-capability claims. Load these references
as needed:

- `references/review-contract.md` for manifest, output, evidence, and verdict rules.
- `references/personas.md` before selecting or normalizing a panel.
- `references/rubric.md` before scoring personas.
- `references/review-modes.md` when mode, focus, or evidence requirements are unclear.
- `references/artifact-bundles.md` for screenshots, workflows, browser/WPF comparisons, and
  freshness evidence.
- `references/sample-prompts.md` for ready invocation examples.

Use the templates under `assets/bundles/` when a review needs a repeatable manifest.

## Use When

Use this skill for structured persona feedback on a concrete Meridian artifact, including release
advice, usability comparison, workflow fit, adoption risk, and owner-priority synthesis.

Trigger examples:

- "Run a simulated user panel on this workstation screen."
- "Give release-gate feedback from accounting and compliance users."
- "Compare the browser and WPF workflows with realistic personas."

## Do Not Use When

- Use code-review guidance for code correctness.
- Use brainstorming guidance for idea generation without a concrete artifact.
- Use roadmap strategy guidance for final sequencing.
- Do not present simulated reactions as interviews, telemetry, or validated customer demand.

## Invocation Modes

- `design_partner`: end with `steer`, `prototype`, or `defer`.
- `release_gate`: advisory only; fail closed when critical evidence is missing and end with `ship`,
  `ship_with_caveats`, or `hold`.
- `usability_lab`: compare repeatable runs and end with `advance_to_release_gate`,
  `rerun_after_changes`, or `defer`.

## Workflow

1. Validate or construct a manifest from `assets/review-manifest.schema.json`.
2. Inspect every accessible artifact and classify evidence as `sufficient`, `partial`, or
   `insufficient`.
3. Select at least four roles from the canonical Persona Matrix. Label advisory lenses and honor
   user-specified custom roles without silently relabeling them.
4. Evaluate each persona's first reaction, core-task attempt, trust check, adoption decision, and
   one owner-minded improvement push.
5. Score all six rubric dimensions with bounded 1-5 ratings and artifact evidence. Do not average
   conflicting roles into one panel score.
6. Synthesize repeated strengths, complaint clusters, disagreements, blockers, and owner actions.
7. Apply mode-specific verdict rules. `ship` requires current sufficient evidence and every
   success criterion verified.
8. Return the ordered output contract and simulation disclaimer.

## Handoffs

- Hand opportunity generation to the brainstorm lane.
- Hand one selected action to a blueprint or technical-design lane.
- Hand release blockers to implementation assurance.

## Validation

- Validate the manifest and result against the schemas under `assets/`.
- Use `scripts/run_eval.py` to materialize, score, aggregate, and compare scenario runs.
- Use `agents/grader.md` for qualitative failures that deterministic checks cannot capture.

## Output Standards

Use these exact H2 headings in order unless the user explicitly requests a shorter artifact; do
not promote or demote them. Start every persona with an H3 heading under `Persona Findings`.

1. `## Executive Summary`
2. `## Panel`
3. `## Persona Findings`
4. `## Cross-Persona Tensions`
5. `## Owner Actions`
6. `## Release Recommendation`
7. `## Confidence Notes`

Follow `references/review-contract.md` for required persona fields, action buckets, evidence
boundaries, verdict rules, and `assets/review-result.schema.json`.

## Quality Bar

- Use at least four distinct personas for a panel.
- Name tradeoffs when one role's request harms another role's workflow.
- Keep browser and WPF evidence co-equal when both surfaces are in scope.
- Treat missing or stale evidence as a finding; in `release_gate`, critical gaps force `hold`.
- Treat mobile-only requests as out of scope unless Meridian's product direction explicitly changes.
