The annotated line primitive: roles instead of colors, direct labels instead of a legend, committed selection instead of hover-only discovery, model bands that can't be mistaken for observed data, and a table fallback in the box.

```jsx
<TraceChart
  description="The belly steepened while the long end remained anchored."
  categories={tenors} yUnit="Yield (%)" xUnit="Tenor" yFormat={(v)=>v.toFixed(1)+"%"}
  series={[
    { id:"cur",   label:"Current",        role:"actual",   values:current },
    { id:"prior", label:"1M prior",       role:"prior",    values:prior },
    { id:"scen",  label:"Bull steepener", role:"scenario", values:scenario },
  ]}
  annotations={[{ type:"range", from:4, to:6, label:"2Y–5Y belly" }]}
  selected={sel} onSelect={setSel} />
```

Pass `null` for a missing observation — the line breaks and the fallback table prints `Missing`. Use `band` for model uncertainty only; freshness/quality of an observation is a `DataCondition`, never a band. For four or more series, keep `directLabels` and drop series instead of adding a detached legend. Comparing repeated entities → `ComparisonPlate`/`PlateGrid` (shared domain); one metric with an interval → `IntervalBar`.
