In-panel tab switch with an optional count per tab. `tabs` are `{label,count,disabled}` or bare strings; child `TabPanel`s render in order.

```jsx
<Tabs tabs={[{ label: "Blotter" }, { label: "Fills", count: 12 }]} onChange={setTab}>
  <TabPanel>{/* blotter */}</TabPanel>
  <TabPanel>{/* fills */}</TabPanel>
</Tabs>
```

Use for switching views inside one surface — not for top-level navigation (that's `NavRail`). Keep to a handful of tabs; overflow means the surface is doing too much.
