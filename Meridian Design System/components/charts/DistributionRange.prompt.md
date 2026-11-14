Use `DistributionRange` for forecast intervals over time (`mode="fan"`) or
five-number category summaries (`mode="box"`). Use `IntervalBar` for compact
quantile rows without a meaningful x-axis.

```jsx
<DistributionRange mode="fan"
  median={[100, 103, 105]} actual={[100, null, null]}
  bands={[{ label: "Model 90% interval", lower: [100, 95, 90], upper: [100, 111, 120] }]}
  labels={["Observed", "Year 1", "Year 2"]}
  ariaLabel="Synthetic portfolio projection"
  ariaDescription="The forecast range widens after the observed starting point."
  accessibleData={[{ label: "Year 2 model median", value: "105 USD" }]}
  note="Synthetic model interval; assumptions and probabilities are supplied by the caller." />
```

Fan bands are ordered outermost to innermost. Align array indices and labels;
retain nulls for unobserved periods instead of inventing zeroes. State model,
probability level, horizon, units, and basis. The component does not estimate
uncertainty, validate coverage, or distinguish confidence from prediction
intervals. Observed and modeled paths must remain separate.

For box mode provide finite `min ≤ q1 ≤ median ≤ q3 ≤ max`, optional outliers,
and one category label per box. Choose a shared domain for comparability. No
quantile validation, loading indicator, or error recovery is built in: the
containing surface owns those states. Empty arrays do not prove zero risk.

Supply `ariaLabel`, a reading in `ariaDescription`, and `accessibleData` through
the shared `ChartFrame` contract. Crosshair position is controlled; this component
does not create keyboard navigation or interactive data selection.
