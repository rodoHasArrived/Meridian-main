The eleven data conditions every Meridian value can be in — `confirmed · provisional · estimated · stale · overridden · missing · na · pending · exception · reconciled · restricted` — rendered as text + marker so the state survives grayscale, color deficiency, and static export. Loose read-model strings normalize automatically (`Matched` → reconciled, `BreaksDetected` → exception).

```jsx
<DataCondition condition="stale" detail="18m" chip />
<DataConditionValue value="4.35%" condition="estimated" align="right" />
<DataConditionLegend items={[
  { label: "Price observation",  condition: "confirmed" },
  { label: "Scenario result",    condition: "estimated" },
  { label: "Reference attributes", condition: "stale", state: "Stale 18m" },
  { label: "Optional liquidity field", condition: "missing" },
]} />
```

Use `DataConditionValue` in any numeric cell whose value may not be observed — it prints `4.35% Est.`, `4.35% · Stale 18m`, `Missing`, or `—` instead of a bare number or an empty cell. Use `DataConditionLegend` as the *data-state contract* block in `EvidenceInspector`. Observed-data condition and **model uncertainty** are different things: conditions describe the datum, `IntervalBar`/`TraceChart` bands describe the model.
