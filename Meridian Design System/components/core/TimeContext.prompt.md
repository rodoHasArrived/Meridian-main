The temporal contract. Twelve kinds of time appear in Meridian — trade, settlement, effective, received, posting, valuation, reporting period, fiscal period, close deadline, as-of, evidence timestamp, calculation timestamp — and a number is only interpretable when the surface says which ones it is stated in. Declare them once per screen instead of letting each panel invent its own `date` prop.

```jsx
<TimeContextProvider value={{
  trade: "2026-07-30", settlement: "2026-08-02", posting: "2026-07-31",
  reportingPeriod: "2026-07", closeDeadline: "2026-08-05T18:00:00Z",
  asOf: "2026-07-31T23:59:00Z", evidenceAt: "2026-08-01T06:14:00Z",
  calculatedAt: "2026-08-01T04:02:00Z",
}}>
  <TimeContextPanel sources={{ settlement: "Northern Trust", calculatedAt: "meridian-accrual@4.2.1" }} />
</TimeContextProvider>
```

Roles group by whose clock they are: **in the world** (trade, settlement, effective, received), **in the books** (posting, valuation, reporting period, fiscal period, close deadline), **in this figure** (as-of, evidence timestamp, calculation timestamp). The panel renders only the roles the context carries, so a screen declares what it actually knows.

`checkTimeCoherence` is the part that earns the contract: it catches settlement before trade, a figure calculated before the evidence it cites, a posting outside its reporting period, evidence that arrived after the close deadline, a position settling past that deadline, and a valuation date that quietly differs from the as-of instant. Run it in a gate as well as in the panel — an attestation should not be signable over an incoherent date set.

Use `TimeRole` for a single role inline (a table header, an evidence line, a chart caption). This is a declaration, not a control: `AccountingPeriodSelector` still owns choosing the period and `WorkspaceContextBar` still carries period and as-of in the chrome — they read the same roles rather than restating them.
