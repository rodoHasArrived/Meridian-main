# Artifact Bundles

The simulated-user-panel system is artifact-driven. Prefer concrete bundles over freeform
summaries whenever possible.

## Bundle Types

### `screen-review`

Use for screenshots, page captures, and static UI critique.

Recommended evidence:

- one or more screenshots
- the owning XAML or view model path
- a short artifact summary

### `workflow-walkthrough`

Use for repeatable operator flows that can be walked through step by step.

Recommended evidence:

- a workflow `manifest.json`
- per-step screenshots
- smoke-test notes or result JSON
- any relevant page, service, or route paths

For WPF flows, prefer existing automation outputs described in:

- `docs/development/desktop-workflow-automation.md`
- `docs/development/desktop-testing-guide.md`

### `roadmap-review`

Use for roadmap items, blueprints, plans, and product bets.

Recommended evidence:

- the roadmap or blueprint path
- nearby implemented features or docs that show current platform reality
- constraints and success criteria

### `ship-readiness`

Use when the review should end with an explicit release recommendation.

Recommended evidence:

- screenshots or workflow manifests
- smoke-test outputs
- targeted test evidence
- explicit launch or rollout criteria

## External Capture Guidance

Artifact capture stays outside this skill. Use the existing desktop capture and manual-generation
workflows to produce bundles, then feed the bundle into this skill.
