The synchronized rail beside a dominant viewport: selected value → evidence → data-state contract → provenance trace → next action. It answers "where did this number come from and how certain is it" without repeating the chart.

```jsx
<EvidenceInspector value="4.45%" caption="Current · 10Y · +11 bp vs 1M" condition="confirmed">
  <EvidenceSection title="Evidence" fields={[
    { label: "Source", value: "Refinitiv" },
    { label: "Observed", value: "11:29:42 UTC" },
    { label: "Quality", value: "Confirmed · 98%" },
  ]} />
  <EvidenceSection title="Data-state contract"><DataConditionLegend items={contract} /></EvidenceSection>
  <EvidenceSection title="10Y model range" note="Intervals describe the scenario model, not observed market-data uncertainty.">
    <IntervalBar rows={ranges} />
  </EvidenceSection>
  <EvidenceSection title="Provenance trace"><ProvenanceTrace steps={trace} selectedId={node} onSelect={setNode} /></EvidenceSection>
</EvidenceInspector>
```

Keep it to ~352px (`--inspector-width`) and to five sections. On mobile it becomes an inline disclosure *below* the primary evidence — never above it. Use `EntitySummary` instead when the rail describes a record rather than a selected datum.
