Interactive split of a fixed total across editable weights — cent-exact via `Money.allocateAmount` (largest-remainder), so the footer total always equals the input total, never off by a cent. Amounts and % shares recompute live as weights are typed.

```jsx
const { AllocationEditor } = window.MeridianDesignSystem_4f61be;

<AllocationEditor
  total={125000}
  currency="USD"
  lines={[
    { label: "Fund A — management fee", weight: 3 },
    { label: "Fund B — management fee", weight: 2 },
    { label: "Advisory account",        weight: 1 },
  ]}
  onChange={(amounts, weights) => setSplit(amounts)}
/>
```

Non-obvious: weights are relative — [3,2,1] and [30,20,10] split identically, and they need not sum to 100. Zero-weight lines get a dash and no allocation; an all-zero weight set falls back to an equal split (the allocateAmount contract). `onChange` fires with the exact amounts array — post those, not consumer-side percentages, so the journal ties out.
