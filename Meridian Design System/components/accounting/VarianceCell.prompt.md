The table-level primitive for actual-versus-expected comparison. It shows the observed value, the
signed variance (and relative variance where meaningful), the tolerance result, the data condition,
and an optional explanation. Result is never carried by color alone — every state has a mark and a
word.

```jsx
<VarianceCell actual={12441284.12} expected={12439902.44}
  tolerance={500} notation="accounting" currency="USD"
  comparisonLabel="custodian statement" condition="confirmed" />
```

- `notation`: `currency` · `accounting` (negatives in parentheses) · `percent` · `bp` ·
  `quantity` · `count` · `duration` (early/late) · `number`.
- Tolerance is absolute (`tolerance`) or relative (`tolerancePct`). Values inside tolerance but
  above `nearFactor` (0.8) of it resolve to **Review**, not agreement.
- States: `within` · `near` · `outside` · `pending` · `not-comparable` · `missing-actual` ·
  `missing-reference`. Missing and not-comparable never render as agreement.
- `condition` accepts the eleven data conditions; estimated values are suffixed `Est.`, overridden
  values marked `*`.
- `onSelect` makes the cell a selectable datum (blue rail). `layout="inline"` for dense rows.
