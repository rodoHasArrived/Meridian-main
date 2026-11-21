Sign-off bound to a specific object, version, period, and evidence set — not a generic approval
button.

```jsx
<AttestationPanel title="July STAT close attestation" scope="General Account"
  period="July 2026" objectId="CLOSE-2026-07" version={19} snapshot="2026-08-01 07:14 UTC"
  gates={readinessGates} evidence={{attached:11, required:12}}
  signatories={[{role:"Preparer", name:"M. Okafor", at:"08-01 07:20"},
                {role:"Reviewer", name:"A. Rivera", at:"08-01 09:02"},
                {role:"Attester", name:"R. Chen", status:"Pending"}]}
  currentUser="R. Chen" certification={certText} requireComment onApprove={c => ...} />
```

- Readiness gates distinguish **blocking** from merely incomplete; the panel states plainly why it
  cannot be attested.
- Evidence completeness is shown as attached-of-required.
- Maker–checker: `signatories` plus `currentUser` enforce segregation of duties (set
  `segregation={false}` to disable).
- States: `not-ready` · `blocked` · `ready` · `awaiting` · `approved` · `rejected` ·
  `changes-requested` · `reopened` · `superseded` · `expired`. An attestation whose underlying
  version changed is void, not stale — say so with `supersededBy` or `expiredReason`.

**Readiness is asserted, never assumed.** An under-declared panel is not ready: absent `gates`, `evidence` counts, `snapshot`, `version` or `signatories` are reported by name ("Attestation under-declared — no readiness gates declared · not bound to a reviewed snapshot"), and a caller-declared `state="ready"` does not survive them. Pass **`snapshotFindings`** — the output of `DecisionSnapshot.verify(snapshot, current)` — so a changed reviewed snapshot expires the pending signature (`void` findings) or asks for a re-read (`review` findings) instead of letting a signature attach to a record that has moved.

Private required rationale is bound to objectId, version and reviewed snapshot. Changing a binding clears an uncontrolled comment before approval; a controlled comment remains parent-owned. Approval/rejection callbacks are synchronous notifications and the parent owns any remote outcome.
