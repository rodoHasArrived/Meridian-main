Structured field / operator / value query rows, AND-combined — for registry surfaces (security master, run ledgers, report catalogs). Where `FilteredDataTable` does free text + facet chips, FilterBuilder composes explicit predicates. Operators follow the field type: text (contains / is / starts with / is empty), number (= ≠ > ≥ < ≤ between), date (on / before / after / between), enum (is / is not).

```jsx
const FIELDS = [
  { key: "symbol", label: "Symbol", type: "text" },
  { key: "adv", label: "ADV ($)", type: "number" },
  { key: "class", label: "Asset class", type: "enum", options: ["Equity", "ETF", "ADR"] },
];
<FilterBuilder fields={FIELDS} summary={`${shown} of ${total} rows`}
  onApply={(rows, pred) => setVisible(all.filter(pred))} />
```

`onApply` hands you the rows and a compiled `(item) => boolean` predicate — filter your data with it directly. `FilterBuilder.predicate(fields, rows)` compiles the same shape anywhere. Incomplete rows are ignored, never treated as match-nothing. Use `onChange` + `showApply={false}` to filter live as the operator types.
