# Meridian Design System 3.1.0 release notes

**Release date:** 2026-08-11  
**Release type:** additive minor release

## Release objective

Meridian 3.1 converts the system from a broad component catalog into a more coordinated analytical operating environment. It adds shared contracts for workspace context, committed selection, last-known-good continuity, accounting proof, staged change review, evidence coverage, close control, and consequential posting.

## Added

### 25 source modules and 34 public exports

- Foundations: `Datum`, `Capsule` family, `ContinuityFrame` family.
- Coordination: `LinkedSelectionProvider` family and `WorkspaceContextBar` family.
- Analytical figures: `AnalyticalFigure`, `AnalyticalField`, `CashFlowLadder`, `MaturityLadder`, `DurationContribution`, `ControlDependencyGraph`.
- Accounting proof: `VarianceCell`, `TieOutPanel`, `CalculationTrace`, `BasisComparisonMatrix`, `RollforwardMatrix`, `AccountingPeriodSelector`, `CloseCalendar`, `PostingImpactPreview`.
- Change and evidence: `ChangeSetReview` family, `VersionConflictNotice`, `ResolutionDrawer`, `AttestationPanel`, `EvidencePreview`, `SourceCoverageSummary`.

### Ten interactive specimen surfaces

The release includes coordinated foundation, linked-selection, workspace-context, analytical-figure, asset/liability-ladder, control-dependency, accounting-proofing, close/posting, change-control, and evidence-coverage cards. The gallery and manifest are generated from `@dsCard` metadata so coverage cannot drift silently.

### Reproducible extension compiler

`scripts/compile_bundle_extensions.mjs` transpiles the additive React modules, resolves design-system imports through the existing browser namespace, updates bundle hashes and public exports, synchronizes release token metadata, and is idempotent.

### Release automation

- `scripts/sync_catalog.py` — synchronizes all card metadata into the visual gallery and manifest.
- `scripts/check_contrast.py` — resolves CSS variables and `color-mix()` values, then checks default light/dark, semantic washes, chrome, primary-action, selection, and white-label accent states.
- `scripts/validate_release.py` — runs the complete static release gate.
- Generated README inventory markers backed by `_ds_manifest.json`.

## Changed

- Primary `Button` actions now use the structural graphite/paper axis rather than the terracotta brand signal. Link buttons use the interaction-blue focus family.
- Light muted ink is `#606870`; dark muted ink is `#929CA6`, preserving AA contrast on canvas, recessed, hover, plate, and well surfaces.
- Light accent labels use dark ink; semantic solid fills retain white ink. Dark mode uses dark ink for both lighter accent and semantic fills.
- Light pressed accent is `#CB6E4E`; white-label hover tones were adjusted where necessary so one label color remains AA across rest, hover, and pressed states.
- Topbar faint text is lighter in light mode for AA contrast.
- `--workspace-context-height` and `--calendar-cell-min-height` establish shared anatomy for the new context and close surfaces.

## Fixed

- Removed the package’s remaining prohibited `!important` declarations.
- Corrected seed-data exports that could leak product fixtures onto the public namespace.
- Restored the documented contrast checker that was missing from the source package.
- Replaced the stale hand-maintained README census with a generated inventory.
- Synchronized every specimen with the visual-regression gallery.
- Removed 21 page-level `ds-base.js` loaders that the historical monolithic bundle had embedded; loaders now remain beside workstation entry points and cannot recursively reload the browser bundle.
- Added explicit mobile reflow for analytical figure metadata and consequential change/attestation action rows.

## Compatibility

This is an additive release. Existing component names and tokens remain available. The visual change to primary `Button` styling is intentional and aligns the action hierarchy with the design law; no prop migration is required.

See `COORDINATED_COMPONENTS.md` for composition guidance and `QA_REPORT_3.1.0.md` for the validated release evidence.
