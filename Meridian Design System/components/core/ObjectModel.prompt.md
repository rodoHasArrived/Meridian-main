Use `ObjectHeader` whenever a screen is about one financial object — a security, position, journal entry, reconciliation, exception, control, attestation, or any of the sixteen in `OBJECT_TYPES`. It replaces the identity block each workstation would otherwise invent.

```jsx
<ObjectHeader
  type="position"
  id="91282CKQ3" identifierLabel="CUSIP"
  name="US Treasury 4.375% 05/15/34"
  context={{ portfolio: "General Account", book: "STAT" }}
  value={{ label: "Market value", amount: "$46.9M", secondary: "Par $48.2M" }}
  condition="confirmed" asOf="2026-07-31" version={4}
  coverage={{ attached: 99, required: 100, label: "sources" }}
  exceptions={{ count: 1, blocking: true }}
  onOpenEvidence={openEvidence} onOpenExceptions={openExceptions}
/>
```

The ten facets always render in one order — type, identifier, name, context, condition, as-of, material value, version, evidence coverage, open exceptions — and any facet the object does not carry is absent rather than blank. Declaring `type` is enough to get the right labels: `objectMeta("journal")` already knows the identifier is an *Entry* and the material value is a *Debit total*.

It is a pattern, not a card: one rule beneath it, no fill or shadow. Place it directly under `WorkspaceContextBar` and above the dominant work plane — screen scope (entity, period, basis, currency) stays in the bar; the header carries only what qualifies this object. Use `dense` in inspector rails and split panes.

`describeObject(record)` normalizes any record into the same contract, so a header, a chip, a breadcrumb, and a search result describe an object identically. Reach helpers through the bundle carrier: `const { ObjectHooks } = window.<Namespace>` → `describeObject`, `objectMeta`, `OBJECT_TYPES`.

Give `onOpenEvidence` and `onOpenExceptions` wherever those rails exist — they are the header's only interactive facets. Coverage flags negative when incomplete; exceptions flag negative when blocking. See `guidelines/OBJECT_MODEL.md` and `guidelines/WORKSTATION_HIERARCHY.md`.
