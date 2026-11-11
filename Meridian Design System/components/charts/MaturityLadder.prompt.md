Maturity profile of exposure by bucket — the distribution of what is held, not the schedule of what it pays. For scheduled flows use `CashFlowLadder`.

```jsx
<MaturityLadder
  items={buckets}
  comparisonMode="Portfolio versus benchmark"
  primaryLabel="Portfolio" comparisonLabel="Benchmark"
  measure="Market value" currency="USD" asOf="July 31, 2026"
  bucketDefinition="By effective maturity, call-adjusted"
  onSelect={bucket => inspect(bucket)}
/>
```

**Bucket discipline.** Buckets are declared, not inferred from the data. Pass `buckets` (or accept the default `MATURITY_BUCKETS`) and every bucket renders in that fixed order — an empty bucket shows at zero rather than vanishing, so the profile's shape is comparable period to period. Any item whose label falls outside the declared set is appended and marked *Outside declared buckets*; nothing is dropped to make the ladder tidy.

**Unknown maturity is exposure.** A bucket labelled Unknown (or `unknown: true`) renders like any other and is called out in the note beneath the ladder. Never redistribute it pro rata into the dated buckets and never net it away.

**Amount and percent are separate readings.** The mode switch changes both series and the difference column together; the difference is stated in currency or in percentage points, and the footnote says which. Do not read a percent bar against an amount label.

**Comparison.** `comparisonMode` names what is being compared — portfolio versus benchmark, assets versus liabilities, current versus prior period, base versus scenario, statutory versus management, entity versus consolidated. Both series share one scale and one baseline; a dashed parity marker sits at the comparison value so the gap is visible without measuring. The difference column is directly labelled — the reader never infers magnitude from bar length alone.

**Totals and fallback.** Exact totals for both series sit under the ladder, and the summary table carries bucket, both series, both shares, difference, and condition. `showTable={false}` keeps that table for assistive technology and restores it on narrow screens, where the bars are withdrawn.

Selecting a bucket is meant to reveal what is inside it: securities, issuers, liability groups, exposure by currency, source coverage, concentration contributors.
