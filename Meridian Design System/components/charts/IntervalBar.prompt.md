Quantile rows for one metric on a shared domain — the compact uncertainty encoding for inspectors and side rails.

```jsx
<IntervalBar domain={[3.9,5.1]} format={(v)=>v.toFixed(2)}
  rows={[{ id:"p90", label:"90%", lo:4.07, hi:4.91, mid:4.49, wide:true },
         { id:"p50", label:"50%", lo:4.29, hi:4.68, mid:4.49 },
         { id:"mean", label:"Mean", lo:4.49, hi:4.49, mid:4.49, text:"4.49%" }]}
  note="Intervals describe the scenario model, not observed market-data uncertainty." />
```

Label every level explicitly and keep the note — an unlabelled band, or one that blurs model range into data freshness, is an anti-pattern. Range over time → `TraceChart` `band`; distribution of outcomes → `Histogram`; a value's freshness or quality → `DataCondition`.
