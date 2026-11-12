Compare asset inflows and liability outflows by time bucket, with the net position visible in every row. Use for coupon/principal schedules, liquidity planning, and asset-liability matching.

```jsx
<CashFlowLadder items={[{label:"0–1Y",assets:840,liabilities:920},{label:"1–3Y",assets:1280,liabilities:1110}]} />
```

The center datum separates inflows from outflows. Exact values remain available through the built-in table alternative.

Unavailable-input safeguard: explicit missing/not-applicable/restricted conditions or
nonfinite required amounts replace the populated view with a stated unavailable
result. Restricted row values, labels, titles and table totals are not emitted, and
no partial totals or percentages are presented. Empty declared buckets still
represent an observed empty bucket at zero. The caller must recover usable data
before the ordinary view returns; UI redaction does not replace server permissions.
