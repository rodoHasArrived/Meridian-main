Standardized small multiples on one shared scale — the honest way to compare repeated entities. `PlateGrid` derives a single domain and tick set for the whole set; each plate keeps identical plot size, padding, label placement, and footer.

```jsx
<PlateGrid categories={["2Y","5Y","10Y","30Y"]} format={(v)=>v.toFixed(0)}
  plates={[
    { id:"ust",  title:"United States",  tag:"UST",  values:ust,  footerLabel:"10Y change", footerValue:"+11 bp" },
    { id:"bund", title:"Germany",        tag:"BUND", values:bund, footerLabel:"10Y change", footerValue:"+9 bp" },
  ]}
  selectedId={sel} onSelect={setSel} />
```

Never let a plate scale itself to its own data — that is the prohibited deviation this component exists to prevent. Selection tints and rails a plate; it never resizes it. Beyond ~12 plates switch to a table plus one focused plate. For one entity over time use `TraceChart`; for one metric with an interval use `IntervalBar`.
