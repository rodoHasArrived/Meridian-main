# Review Contract

This is the stable manifest and output contract shared by the Codex skill, the portable skill,
the GitHub prompt family, and the eval runner.

## Review Manifest

Use `assets/review-manifest.schema.json` as the machine-readable source of truth.

Required fields:

- `mode`: `design_partner`, `release_gate`, or `usability_lab`
- `artifact_type`: `screen-review`, `workflow-walkthrough`, `roadmap-review`, or
  `ship-readiness`
- `artifact_paths`: file paths, screenshot paths, manifest paths, or doc paths used as evidence
- `persona_set`: panel selection such as `core-finance`, `research`, `operations-controls`, or
  `growth-adoption`
- `focus_areas`: one or more review lenses such as `workflow_fit` or `trust_and_controls`
- `constraints`: business, implementation, or review constraints
- `success_criteria`: what a strong outcome must prove

Recommended optional fields:

- `artifact_summary`
- `decision_deadline`
- `notes`

## Output Contract

Use these headings in this order:

1. `Executive Summary`
2. `Panel`
3. `Persona Findings`
4. `Cross-Persona Tensions`
5. `Owner Actions`
6. `Release Recommendation`
7. `Confidence Notes`

Within `Owner Actions`, use these buckets in order:

- `Now`
- `Next`
- `Later`

Every persona entry must include:

- `Liked`
- `Didn't like`
- `Missing or risky`
- `Owner-minded improvement ideas`
- `Adoption verdict`
- `Rubric (1-5 with evidence)` for:
  - Workflow Fit
  - Trust / Controls
  - Time-to-Value
  - Data Confidence
  - Extensibility
  - Learning Curve

## Recommendation Rules

- In `design_partner`, use `steer`, `prototype`, or `defer`.
- In `release_gate`, use `ship`, `ship_with_caveats`, or `hold`.
- In `usability_lab`, use `advance_to_release_gate`, `rerun_after_changes`, or `defer`.

## Confidence Rules

Always separate:

- `Verified`: what the repo or artifact bundle proves
- `Inferred`: persona interpretation, adoption predictions, or strategic extrapolation
- `Missing evidence`: gaps that should reduce confidence or block a release call
