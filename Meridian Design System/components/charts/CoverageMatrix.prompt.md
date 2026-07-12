Symbol × session data-availability heat grid — which instrument has which day, at what completeness. Statuses render as flat alpha washes on a hairline grid: `full` (green) · `partial` (amber) · `gap` (red) · `pending` (neutral fill) · `none` (faded, e.g. holiday). Hovering or focusing a cell writes a mono readout line under the grid.

```jsx
<CoverageMatrix
  rows={["AAPL", "MSFT", "NVDA"]}
  cols={days.map((d) => ({ id: d, label: d.slice(5) }))}
  data={{ AAPL: { "2026-06-19": { status: "partial", detail: "210/390 bars" } } }}
  onCellClick={(row, col, cell) => openScan(row.id, col.id)}
/>
```

Feed a `data[rowId][colId]` map or a `cell(row, col)` resolver. Put the evidence in `detail` ("412/780 bars") — it lands in the readout and tooltip. With `onCellClick` the cells become buttons (drill into the gap-scan). This is the coverage view for a market-data platform; pair with `LogTail` to show the scan behind a clicked cell.
