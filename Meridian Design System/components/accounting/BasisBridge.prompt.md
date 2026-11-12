One event, several accounting bases, side by side — the surface that makes "statutory only" a visible, reviewable scope instead of a hidden consequence.

```jsx
<BasisBridge
  bases={[{ id:"stat", label:"Statutory", meta:"Selected" },
          { id:"gaap", label:"GAAP", reference:true },
          { id:"tax",  label:"Tax",  reference:true }]}
  rows={[{ label:"Ledger account", changed:["stat"],
           values:{ stat:"Private placement debt", gaap:"Corporate bonds", tax:"Tax investments" } }]}
  footer={[{ label:"Approval", values:{ stat:"Required", gaap:"Not required", tax:"Not required" },
             tones:{ stat:"exception" } }]} />
```

Show it **before** an override is applied, next to the before/after comparison — the reviewer's question is always "which basis moves and who signs it". Mark differences with `changed` (rail + marker + tone), never tone alone. Use `DiffView` for a field-level before/after audit trail.
