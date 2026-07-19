---
name: meridian-simulated-user-panel
description: Critique concrete Meridian screens, workflows, documents, roadmaps, and release candidates with artifact-grounded multi-persona panels. Use for simulated user testing, persona-based product critique, multi-role reactions, workflow-fit and adoption-risk analysis, usability-lab comparison, or advisory release-gate feedback. Do not use as a substitute for real user research, for code correctness review, for unconstrained brainstorming without an artifact, or for final roadmap sequencing.
---

# Meridian Simulated User Panel

Run evidence-led product reviews in `design_partner`, `release_gate`, or `usability_lab` mode.
Treat every reaction as a simulation, never as observed user research.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before making
current-capability claims. Load these references directly from this file as needed:

- `references/review-contract.md` for manifest, output, evidence, and verdict rules.
- `references/personas.md` before selecting or normalizing a panel.
- `references/rubric.md` before scoring personas.
- `references/review-modes.md` when mode, focus, or evidence requirements are unclear.
- `references/artifact-bundles.md` for screenshots, workflows, browser/WPF comparisons, and
  freshness evidence.

Use the manifest templates under `assets/bundles/` when the user needs a repeatable review.

## Use When

Use this skill for structured persona feedback on a concrete Meridian artifact, including release
advice, usability comparison, workflow fit, adoption risk, and owner-priority synthesis.

Trigger examples:

- "Run a simulated user panel on this workstation screen."
- "Give release-gate feedback from accounting and compliance users."
- "Compare the browser and WPF workflows with realistic personas."

## Do Not Use When

- Use `meridian-code-review` for code correctness.
- Use `meridian-brainstorm` for idea generation without a concrete artifact.
- Use `meridian-roadmap-strategist` for final sequencing.
- Do not present simulated reactions as interviews, telemetry, or validated customer demand.

Non-trigger examples:

- "Review this diff for bugs."
- "Brainstorm product ideas from scratch."
- "Update the roadmap registry."

## Invocation Modes

- `design_partner`: shape an early artifact and end with `steer`, `prototype`, or `defer`.
- `release_gate`: provide advisory go/no-go feedback. Fail closed when critical evidence is missing;
  end with `ship`, `ship_with_caveats`, or `hold`.
- `usability_lab`: compare repeatable runs and end with `advance_to_release_gate`,
  `rerun_after_changes`, or `defer`.

## Workflow

1. Validate or construct the manifest from `assets/review-manifest.schema.json`.
2. Inspect every accessible artifact. Classify evidence as `sufficient`, `partial`, or
   `insufficient`; never infer a missing screen, state, test, or control.
3. Select at least four roles from the canonical Persona Matrix. Keep advisory lenses explicitly
   labeled and honor user-specified custom roles without silently relabeling them.
4. When the user explicitly requests independent panel voices and persona agents are available,
   give each agent only the raw artifact and manifest. Keep one coordinator responsible for
   synthesis. Otherwise evaluate the roles locally.
5. For each persona, record first reaction, core-task attempt, trust check, adoption decision, and
   one owner-minded improvement push.
6. Score all six rubric dimensions with bounded 1-5 ratings and artifact evidence. Higher is always
   better; do not average conflicting roles into a single false-precision score.
7. Synthesize repeated strengths, complaint clusters, disagreements, blockers, and prioritized
   owner actions.
8. Apply the mode-specific recommendation rules. In `release_gate`, `ship` requires sufficient,
   current, verified functional evidence and every success criterion verified.
9. Return the ordered output contract and its simulation disclaimer.

## Handoffs

- Hand findings to `meridian-brainstorm` when new opportunity generation is needed.
- Hand one selected action to `meridian-blueprint` for a technical design.
- Hand release blockers to `meridian-implementation-assurance` for implementation and proof.

## Validation

- Validate manifests with `scripts/validate_review_manifest.py`.
- Validate Markdown results with `scripts/simulated_user_output_check.py`.
- Run deterministic package scenarios with `scripts/run_evals.py --all --dry-run --summary`.
- Score qualitative candidates with `scripts/score_eval.py`.
- Check positive and negative routing fixtures with `scripts/check_trigger_prompts.py`.
- Check host-neutral mirror drift with `scripts/check_shared_contracts.py`.

## Output Standards

Begin with the four-field `Skill Selection` receipt required by
`../_shared/codex-execution-contract.md`. Then use these exact H2 headings in order unless the user
explicitly requests a shorter artifact; do not promote or demote them. Start every persona with an
H3 heading under `Persona Findings`.

1. `## Executive Summary`
2. `## Panel`
3. `## Persona Findings`
4. `## Cross-Persona Tensions`
5. `## Owner Actions`
6. `## Release Recommendation`
7. `## Confidence Notes`

Follow `references/review-contract.md` for required persona fields, action buckets, confidence
boundaries, recommendation rules, and the structured `assets/review-result.schema.json` shape.

## Quality Bar

- Use at least four distinct personas for a panel.
- Name tradeoffs when one persona's request harms another persona's workflow.
- Keep browser and WPF evidence co-equal when both surfaces are in scope.
- Treat missing or stale evidence as a finding; in `release_gate`, critical gaps force `hold`.
- Treat mobile-only requests as out of scope unless Meridian's product direction explicitly changes.
