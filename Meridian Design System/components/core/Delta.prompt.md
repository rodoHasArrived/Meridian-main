Delta — signed change value with the explicit-sign content rule built in. Mono, tabular, dim semantic tone: positive green-dim, negative red-dim, zero muted "±0.00".

```jsx
<Delta value={1.84} suffix="%" />           // +1.84%
<Delta value={-4118.22} decimals={2} />     // -4118.22
<Delta value={0.32} tone="down" arrow />    // ▼ +0.32 (tone override for inverted metrics)
```
