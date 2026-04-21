---
name: meridian-simulated-user-panel
description: >
  Simulate structured Meridian product feedback from multiple realistic user personas such as
  quantitative analysts, fund managers, accountants, fund operators, individual traders,
  hobbyists, academics, compliance leads, data engineers, support and onboarding leads,
  implementation consultants, and owner-operators. Use when the user asks for simulated user
  testing, persona-based critique, multi-role reactions, release-gate feedback, usability-lab
  analysis, workflow-fit analysis, adoption-risk analysis, or improvement ideas for Meridian
  screens, workflows, documents, roadmap directions, or overall product strategy.
---

# Meridian Simulated User Panel

Run artifact-driven Meridian product reviews that can behave as a `design_partner`,
`release_gate`, or `usability_lab` depending on the request.

Read `../_shared/project-context.md` before making claims about current Meridian capabilities.
Read `references/review-contract.md` before choosing the output shape. Read
`references/personas.md` before choosing a panel. Read `references/review-modes.md` when the best
mode or focus area is unclear. Read `references/artifact-bundles.md` when the review depends on
screenshots, workflow manifests, smoke-test notes, or repeatable WPF evidence.

Use these bundle templates when you need a ready manifest:

- `assets/bundles/screen-review.manifest.json`
- `assets/bundles/workflow-walkthrough.manifest.json`
- `assets/bundles/roadmap-review.manifest.json`
- `assets/bundles/ship-readiness.manifest.json`

## Mission

- Inspect the most concrete Meridian artifact available before simulating reactions.
- Keep every persona owner-minded: each role should care about product quality, trust, support
  burden, and strategic coherence.
- Separate verified repo evidence from inferred user reaction.
- Prefer buildable recommendations over generic product advice.

## Invocation Modes

- `design_partner`: default mode for roadmap shaping, early critique, and product direction work.
- `release_gate`: explicit go/no-go mode for features close to shipping. Requires success criteria
  and should use the most concrete artifact bundle available.
- `usability_lab`: explicit benchmarking mode for comparing outputs, clustering complaints, and
  tracking quality drift across runs.

## Input Contract

The stable review manifest is defined in `references/review-contract.md` and
`assets/review-manifest.schema.json`.

Every automated invocation should provide:

- `mode`
- `artifact_type`
- `artifact_paths`
- `persona_set`
- `focus_areas`
- `constraints`
- `success_criteria`

Default assumptions:

- `mode = design_partner`
- `persona_set.panel = core-finance`
- `focus_areas` should reflect the lightest review lens that answers the request

## Workflow

1. Validate the review manifest and note any missing evidence.
2. Inspect the artifact bundle: code, XAML, screenshots, workflow manifests, smoke-test notes,
   docs, or roadmap text.
3. Select the panel from `references/personas.md`. Honor user-specified roles exactly.
4. For each persona, evaluate:
   - first reaction
   - core task attempt
   - trust check
   - adoption decision
   - owner-minded improvement push
5. Score every persona on the six required rubric dimensions with 1-5 ratings backed by evidence:
   - Workflow Fit
   - Trust / Controls
   - Time-to-Value
   - Data Confidence
   - Extensibility
   - Learning Curve
6. Synthesize repeated strengths, repeated complaints, disagreements, and owner-priority actions.
7. End with the shared output contract, even when the user wants a concise answer.

## Output Contract

Use these headings unless the user explicitly asks for another format:

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
- `Rubric (1-5 with evidence)` across all six required dimensions

Within `Owner Actions`, group recommendations as:

- `Now`
- `Next`
- `Later`

Within `Confidence Notes`, always separate:

- `Verified`
- `Inferred`
- `Missing evidence`

The stable result shape is defined in `assets/eval-result.schema.json`.

## Quality Bar

- Use at least 4 personas when the user asks for a panel.
- Name tradeoffs when one persona's request hurts another persona.
- In `release_gate`, separate blockers from polish and end with `ship`, `ship_with_caveats`, or
  `hold`.
- In `usability_lab`, surface repeated complaint clusters and note whether the output is strong
  enough to compare against prior runs.
- If the artifact bundle is weak or incomplete, say so plainly and treat that as product evidence.
