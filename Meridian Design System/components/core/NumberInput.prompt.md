Numeric field with +/- stepper buttons — mono value, hairline border. `min`/`max` clamp the value and disable the boundary button.

```jsx
<NumberInput label="Quantity" value={qty} onChange={setQty} min={0} step={100} />
<NumberInput label="Leverage" value={lev} onChange={setLev} min={1} max={4} step={0.5} />
```

Keeps its own local state seeded from the initial `value` — it isn't a fully controlled input past mount, so if you need to force a new value externally (e.g. a "reset" button), remount it (`key={resetToken}`) rather than relying on the `value` prop alone.
