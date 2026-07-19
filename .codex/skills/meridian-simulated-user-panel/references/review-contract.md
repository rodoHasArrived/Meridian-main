# Review Contract

Use this contract for the Codex skill, portable mirrors, specialist profiles, and eval runners.
The panel is simulated product analysis, not observed user research.

## Manifest Contract

Use `assets/review-manifest.schema.json` as the machine-readable source of truth. Every review has:

- `mode`
- `artifact_type`
- one or more `artifact_paths`
- `persona_set`
- one or more `focus_areas`
- one or more `constraints`
- one or more `success_criteria`

Use `artifact_evidence` and `artifact_freshness` for repeatable or release-gate reviews. A custom
panel must name at least one required role. Treat inaccessible paths as missing evidence rather
than silently relying on their names.

## Evidence Sufficiency

Classify the bundle before simulating reactions:

- `sufficient`: artifacts are accessible and current enough to test every success criterion.
- `partial`: the main artifact is inspectable, but non-critical states or corroborating evidence
  are absent.
- `insufficient`: the core artifact, a critical workflow state, or release evidence is unavailable.

For `release_gate`, require current verified functional evidence such as a workflow manifest,
smoke result, or targeted test result. Missing critical evidence forces `hold`. `ship` requires
`sufficient` evidence and every success criterion verified. The panel recommendation remains
advisory; GitHub Actions and implementation assurance remain the merge and release authorities.

## Output Contract

Use these headings in order:

1. `Executive Summary`
2. `Panel`
3. `Persona Findings`
4. `Cross-Persona Tensions`
5. `Owner Actions`
6. `Release Recommendation`
7. `Confidence Notes`

Put this statement in `Executive Summary` or `Confidence Notes`:

> This is simulated persona feedback, not observed user research.

In `Panel`, name at least four distinct roles and label each one as `canonical`, `advisory`, or
`custom` when that distinction is not already obvious.

Every persona entry must include:

- `Liked`
- `Didn't like`
- `Missing or risky`
- `Owner-minded improvement ideas`
- `Adoption verdict`
- `Rubric (1-5 with evidence)` for all six dimensions in `references/rubric.md`

Do not collapse persona scores into a single panel average. Explain conflicts in
`Cross-Persona Tensions` instead.

Within `Owner Actions`, use `Now`, `Next`, and `Later` in order. Tie each action to evidence and
the affected personas; leave a bucket empty rather than inventing filler.

## Recommendation Rules

- `design_partner`: `steer`, `prototype`, or `defer`.
- `release_gate`: `ship`, `ship_with_caveats`, or `hold`.
- `usability_lab`: `advance_to_release_gate`, `rerun_after_changes`, or `defer`.

For `release_gate`:

- Use `ship` only with sufficient current evidence, no blockers, and all criteria verified.
- Use `ship_with_caveats` only when remaining gaps are non-critical and explicitly owned.
- Use `hold` for missing critical evidence, failed criteria, unresolved blockers, or stale evidence.

## Confidence Rules

Always separate:

- `Verified`: what inspected artifacts directly prove.
- `Inferred`: persona reactions, adoption predictions, or strategic extrapolation.
- `Missing evidence`: unavailable states, files, tests, research, or telemetry.

Use `assets/review-result.schema.json` for structured panel results. Use
`assets/eval-result.schema.json` only for deterministic grading output.
