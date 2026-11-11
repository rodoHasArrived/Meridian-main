The claim, not the topic. `ClaimHeader` opens an analytical view with a testable statement, the caveat that qualifies it, the state chips that describe its data, and the controls that change it.

```jsx
<ClaimHeader
  context={["Rates", "United States Treasury", "14 May 2026"]}
  claim="The belly steepened while the long end remained anchored."
  note="Current versus one-month prior on a shared tenor scale; the scenario curve is model-derived."
  states={[{ condition:"confirmed", label:"Prices live", detail:"42s" },
           { condition:"stale", label:"Reference", detail:"18m" }]}>
  <SegmentedControl options={ranges} value={range} onChange={setRange} />
</ClaimHeader>
```

Rules: one claim per view; rewrite it when the selection changes the conclusion; never let the claim assert more than the data states support (a stale or partial input belongs in `states`). Use a plain `<h1>` + `Eyebrow` for record pages — `ClaimHeader` is for evidence surfaces.
