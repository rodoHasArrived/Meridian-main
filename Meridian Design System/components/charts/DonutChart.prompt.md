Use `DonutChart` for a small nonnegative set of genuine parts of one total, such
as allocation across sleeves. Prefer ranked bars when there are many categories
or exact comparison matters more than composition.

```jsx
<DonutChart ariaLabel="Synthetic sleeve allocation"
  segments={[{ id: "equity", label: "Equity", value: 60 }, { id: "cash", label: "Cash", value: 40 }]}
  centerValue="100%" centerLabel="Allocation"
  fmt={value => `${value}%`}
  caption="Synthetic allocation; sleeves share one valuation date and USD basis." />
```

`total` defaults to the sum of absolute segment values. Supply nonnegative finite
values and ensure an overridden total reconciles with the displayed parts. Do
not pass signed P&L or net exposures: the renderer uses absolute magnitudes and
would hide their economic sign. `pctFmt` receives a fraction, not percentage points.

For `views`, control `view` and `onViewChange` together. Each view carries its own
segments and optional center text; an unknown view id displays the first view.
The component does not fetch, reconcile, or persist data. Empty/zero totals need
an explicit explanation in the parent; a blank ring is not evidence of success.

Keep the legend or an equivalent visible data list. `ariaLabel` names the SVG and
emits a hidden part/value/share table. Without it the SVG is presentational.
View buttons support native Tab/Enter/Space activation; the current implementation
has no roving arrow-key tab pattern. Do not claim complete tab-widget conformance
without consumer validation.
