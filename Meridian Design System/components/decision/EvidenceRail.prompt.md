The persistent "why is this value what it is" plane. Slots, not a schema — the rail owns the reading
order, the coverage denominator and the continuity; the screen owns the bodies.

```jsx
<EvidenceRail
  object={{ type: "position", id: "91282CKQ3", version: 18 }}
  coverage={{ attached: 99, required: 100, label: "sources" }}
  continuity="retained" freshness="15m" asOf="2026-07-31"
  counts={{ sources: 12, calculation: 3 }}
  sections={{
    summary: <EvidenceSummary />,
    sources: <EvidencePreview evidence={documents} />,
    calculation: <CalculationTrace trace={trace} />,
    lineage: <LineageGraph object={position} />,
  }} />
```

- **Sections are absent, not blank.** A section the screen cannot prove is not a tab. `EVIDENCE_SECTIONS`
  fixes the order (summary → sources → calculation → lineage → audit) so the rail reads the same on
  every screen.
- **Driven by the committed selection.** Put it inside `LinkedSelectionProvider` and pass the
  selected object; hovering must not move it.
- **Coverage always shows its denominator** — `99 of 100 sources`, plus what is outstanding. A short
  set is amber, never silent.
- **Continuity is the evidence's own state**, separate from the work plane's `ContinuityFrame`:
  `live · retained · partial · recovering · stale`. `retained` states that writes are locked.
- Real tablist: arrow keys, Home/End, roving tabindex. In print the tabs disappear and the active
  panel expands.
