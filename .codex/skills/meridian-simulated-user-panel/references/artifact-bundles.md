# Artifact Bundles

Prefer concrete, current bundles over freeform summaries. Inspect paths before relying on them and
record inaccessible paths under `Missing evidence`.

## Screen Review

Use for static UI critique. Include screenshots plus the owning browser or WPF source when
available. A screenshot proves visible state only; it does not prove persistence, permissions,
loading, recovery, accessibility, or performance.

## Workflow Walkthrough

Use for repeatable operator flows. Prefer:

- a step manifest
- per-step screenshots
- smoke or targeted test results
- relevant route, component, view, service, or read-model paths
- explicit success and failure states

## Roadmap Review

Use for roadmap items, blueprints, and product bets. Include the proposal, current implementation
evidence, roadmap registry context, constraints, and success criteria.

## Ship Readiness

Use only with current functional evidence. A release-gate UI bundle should include the maintained
screenshot/workflow manifest and freshness evidence plus smoke or targeted test output. Missing
critical evidence forces `hold`.

## Cross-Surface Review

Use when browser and WPF are both in scope. Include at least one artifact from each lane and the
shared service/read-model seam when product state matters. Compare task coverage and state semantics,
not pixel identity.

## Freshness

Set `artifact_freshness` to:

- `current` when the bundle was captured or verified against the reviewed revision.
- `stale` when it predates material source or workflow changes.
- `unknown` when freshness cannot be established.

Use `artifact_evidence` to classify each item as `verified`, `supplied`, or `missing`. Eval-only
narrative artifacts should use `fixture://` paths so they are not mistaken for live repository files.

Artifact capture remains outside this skill. Feed maintained browser/WPF screenshot and workflow
outputs into the manifest rather than inventing capture behavior here.
