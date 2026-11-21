`ChartCursor` exposes the crosshair hooks and `ChartCursorSync` provider through
one public carrier. Call hooks inside React function components. Spread `bind`
onto the measured HTML wrapper, and pass the same `padLeft` and right-hand `rail`
used by the chart so pointer positions map to the intended data index.

```jsx
const { useSyncedCursor } = ChartCursor;
function PricePlot({ values }) {
  const cursor = useSyncedCursor(values.length, { padLeft: 10, rail: 62 });
  return <div {...cursor.bind}><ChartFrame n={values.length}
    crosshairIndex={cursor.index}>{g => <PriceMarks g={g} />}</ChartFrame></div>;
}
```

Wrap plots in `ChartCursorSync` only when their indices identify the same ordered
observations. Outside the provider the synchronized hook uses local state.
Empty/invalid counts do not produce an index. Pointer exit clears the cursor;
this machinery does not supply keyboard exploration or an accessible data table.
Keep consequential values in the chart's named accessible data or inspector.
