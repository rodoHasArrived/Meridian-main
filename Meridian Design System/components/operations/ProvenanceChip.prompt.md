Cell-level provenance for evidence-backed values — the "where did this number come from" tuple that family-office and reconciliation read models repeat on every row (source system, document, as-of/valuation dates, evidence completeness, reconciliation status, last review). Inline chip: worst-of status dot · mono source · as-of date; everything else lives in the hover title.

```jsx
<ProvenanceChip sourceSystem="Northern Trust" asOfDate="2026-06-30"
  completeness="Complete" reconciliation="Matched" />
<ProvenanceChip sourceSystem="Manual upload" asOfDate="2026-05-31"
  completeness="Partial" reconciliation="BreaksDetected" onOpen={openEvidence} />
```

Use it beside balances, NAVs, and commitments — anywhere a figure needs its evidence posture visible without a column per field. `onOpen` turns it into a button (jump to the evidence drawer); use `EvidenceLink` instead when the artifact itself is the row's subject.
