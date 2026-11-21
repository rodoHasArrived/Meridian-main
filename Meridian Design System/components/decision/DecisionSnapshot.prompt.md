Exactly what was reviewed, frozen at the moment of the decision. `ActionGuard` answers *may I*,
`ActionEnvelope` answers *did it land*, this answers *what did I review*.

```jsx
const snap = DecisionSnapshot.capture({
  object: { type: "journal", id: "JE-20418", version: 3 },
  context: { entity: "General Account", book: "STAT", period: "2026-07" },
  time: { asOf: "2026-07-31T23:59:00Z", evidenceAt: "2026-08-01T06:14:00Z" },
  evidence: [{ id: "DOC-8841", version: "2", hash: "9f2c", condition: "confirmed" }],
  calculations: [{ id: "CALC-7", version: "3", inputHash: "aa71" }],
  guards, consequences: { reversibility: "compensatable" },
  decision: { type: "approved", actorId: "apatel", actorName: "A. Patel", authority: "Controller" },
});

<DecisionSnapshotPanel snapshot={snap} current={{ object: live, evidence: liveEvidence }} />
```

- **Immutable by construction.** `capture` deep-freezes and there is no setter. Capture BEFORE the
  attempt, then commit and attest against `snapshot.id`.
- **Small by design.** Evidence and calculations are stamped by id + version + hash, not embedded —
  the documents stay where they live.
- **`verify` returns findings, not a boolean.** A changed object version or a changed evidence set is
  `void` (the approval is void, not stale); a re-run calculation or a different workspace context is
  `review`. The panel reads *Sealed · Re-read · Superseded* from those findings.
- **`describe` is the audit sentence** — actor, authority, object, version, context, evidence count,
  calculation version, guard count, UTC instant. Use it in the audit trail and the attestation body.
- `DecisionSnapshot.use(input, current)` is the hook form: `{ snapshot, capture, findings, isVoid }`.
- A commit that cannot name a snapshot is the defect this component exists to make visible.
