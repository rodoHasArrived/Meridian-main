# Coordinated analytical components

Release 3.1 turns Meridian’s conceptual primitives into reusable operating contracts. The additions are deliberately compound: they coordinate context, evidence, state, selection, and consequential actions rather than adding another layer of generic cards.

## Architecture at a glance

| Concern | Owner | Durable state | Main guarantee |
| --- | --- | --- | --- |
| Workspace identity | `WorkspaceContextProvider` + `WorkspaceContextBar` | entity, book, basis, period, as-of, currency, scenario, lock and freshness | every dependent surface describes the same analytical frame |
| Committed selection | `LinkedSelectionProvider` | selected entity/observation and selection origin; optional URL parameter | tables, charts, graphs, and evidence rails stay synchronized |
| Connection continuity | `ContinuityFrame` | live, delayed, stale, partial, offline, recovering | last-known-good evidence remains visible while unsafe actions are disabled |
| Configurable composition | `Capsule` | optional persisted collapse state; standard size contract | replaceable modules retain one predictable anatomy |
| Staged mutation | `ChangeSetProvider` + `ChangeSetReview` | original, draft, field changes, warnings | edits are reviewed before mutation and can be discarded as a set |
| Record concurrency | `VersionConflictNotice` | proposed value and latest saved value | no silent overwrite after an intervening update |

`Datum` supplies the shared reference language inside these surfaces: target lines, current-time markers, selection rails, zero lines, and control-position markers all use one structural primitive.

## Component families

### Foundations and coordination

- `Datum` — horizontal or vertical reference with role, label, value, position, strength, and optional marker.
- `Capsule`, `CapsuleHeader`, `CapsuleBody`, `CapsuleFooter` — fixed module anatomy with 144/240/336/432px standard heights, condition state, saved collapse state, and optional move/resize affordances.
- `ContinuityFrame`, `ContinuityStatus` — retains evidence during stale, partial, offline, delayed, or recovering states.
- `LinkedSelectionProvider`, `LinkedSelectionTarget`, `useLinkedSelection` — committed selection contract for coordinated views.
- `WorkspaceContextProvider`, `WorkspaceContextBar`, `useWorkspaceContext` — persistent analytical context immediately below application chrome.

### Analytical figures and domain visualizations

- `AnalyticalFigure` — complete claim, context, unit, condition, source, coverage, selected-observation, annotation, note, fallback, and export anatomy.
- `AnalyticalField` — data-bearing condition or population matrix; never a decorative field.
- `CashFlowLadder` and `MaturityLadder` — shared-scale asset/liability timing comparisons.
- `DurationContribution` — signed or positive contribution ranking with committed selection.
- `ControlDependencyGraph` — stable layered topology, orthogonal routing, blocker treatment, keyboard selection, and a nonvisual relationship outline.

React owns the surrounding structure, controls, selection, URL/persistence state, and inspectors. The visualization component owns mark geometry, shared scales, keyboard mark navigation, and the nonvisual fallback. The implementations are DOM/SVG and require no additional chart runtime.

### Accounting proof

- `VarianceCell` — actual versus expected, residual, tolerance, and pass/review/exception result.
- `TieOutPanel` — independently sourced values, explained adjustments, residual, tolerance, evidence, and reviewer metadata.
- `CalculationTrace` — formula, inputs, operators, sources, intermediate values, rounding, basis, override, and conditioned result.
- `BasisComparisonMatrix` — statutory, GAAP, tax, and management treatments on one fixed geometry.
- `RollforwardMatrix` — standardized movements, totals, and control differences across comparable columns.
- `AccountingPeriodSelector` — open, soft-closed, locked, or adjustment periods with explicit consequence.
- `CloseCalendar` — calendar and keyboard-accessible dependency-list representations of close tasks.
- `PostingImpactPreview` — balanced journal, period gate, before/after effects, downstream reports, reversal behavior, and commit action.

### Change, attestation, and evidence

- `ChangeSetReview`, `ChangeSetDiff`, `ChangeReviewBar` — visible draft delta and consequence gate.
- `VersionConflictNotice` — compare and intentionally resolve optimistic-concurrency conflicts.
- `ResolutionDrawer` — canonical exception statement, evidence, root cause, correction, impact, ownership, SLA, audit, and resolution actions.
- `AttestationPanel` — readiness gates, required role, version, certification, rationale, and sign-off actions.
- `EvidencePreview` — source-document excerpt with page/section, highlighted value, dates, completeness, match confidence, and original-document navigation.
- `SourceCoverageSummary` — compact or expanded evidence completeness across a calculation or population.

## Reference composition: analytical workstation

```jsx
const {
  WorkspaceContextProvider,
  WorkspaceContextBar,
  LinkedSelectionProvider,
  ContinuityFrame,
  AnalyticalFigure,
  EvidencePreview,
} = window.MeridianDesignSystem_205d17;

<WorkspaceContextProvider value={workspaceContext}>
  <WorkspaceContextBar />
  <LinkedSelectionProvider defaultValue={portfolioRows[0]} urlParam="selection">
    <ContinuityFrame
      state={connectionState}
      snapshotAt="2026-07-31 23:59 UTC"
      coverage="96 of 96 positions"
    >
      <AnalyticalFigure
        question="How has the statutory curve changed?"
        title="US Treasury term structure"
        unit="Yield, %"
        asOf="2026-07-31"
        source="Validated market-data composite"
        condition="confirmed"
        fallback={<CurveTable rows={rows} />}
      >
        <CurvePlot rows={rows} />
      </AnalyticalFigure>
    </ContinuityFrame>
    <EvidencePreview {...selectedEvidence} />
  </LinkedSelectionProvider>
</WorkspaceContextProvider>
```

The URL may own the committed selection and filter state. Tiny personal preferences such as density can remain local. Saved capsule layouts belong in a versioned workspace payload. Hover, pointer position, drag-in-progress, and uncommitted tooltips remain ephemeral.

## Reference composition: reconciliation and close

1. Put `WorkspaceContextBar` above the work plane so entity, book, period, basis, currency, and lock state are never inferred.
2. Use `TieOutPanel` as the proof summary and a table of `VarianceCell` values as the scan surface.
3. Link the selected exception to `EvidencePreview` and `ResolutionDrawer` through `LinkedSelectionProvider`.
4. Stage corrections in `ChangeSetProvider`; show `PostingImpactPreview` before journal commitment.
5. Use `AttestationPanel` only after every blocking gate is complete for the exact record version being signed.

## Responsive contract

Large screens use a context bar, dominant evidence plane, and persistent inspector. Narrow screens retain the context summary, place the main evidence before settings, stack comparison plates, and move the inspector inline or into a drawer. `ControlDependencyGraph` should show the selected neighborhood when a complete topology would become illegible. `CloseCalendar` offers a list representation rather than shrinking calendar cells below their readable minimum.

## Accessibility contract

- Every important visual has a text, table, or relationship-outline path.
- Essential values remain visible without hover.
- Selected and focused states are distinct; selection uses a datum rail plus surface treatment.
- `AnalyticalField`, `DurationContribution`, and `ControlDependencyGraph` support keyboard commitment.
- Data condition is stated in text and structure, not color alone.
- `ContinuityFrame` announces degraded state while retaining previously valid evidence.
- Reduced motion never removes meaning; reflow and trace transitions collapse to their final state.

## Performance assumptions

The new charts use SVG/DOM and are intended for one focal figure plus a small number of supporting views. A typical workstation should keep fewer than eight substantial SVG figures mounted concurrently. Large repeated populations should use summarized fields, virtualized tables, or a Canvas adapter rather than thousands of interactive DOM marks. Expensive transformations belong outside repeated React render paths and should be memoized by stable data version.

## Source contracts

Each component ships the complete trio in its domain folder:

- `<Name>.d.ts` — public TypeScript contract
- `<Name>.jsx` — token-only implementation
- `<Name>.prompt.md` — usage and behavioral brief

Interactive reference compositions are the ten 3.1 specimen cards in `components/*/*.card.html`; all are included in `tests/gallery.html`.
