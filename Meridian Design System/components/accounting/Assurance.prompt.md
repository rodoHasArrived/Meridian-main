Certification as carried state on a balance (account × period × basis), not only on a close. Stages climb **unprepared → prepared → reviewed → certified** one at a time — `signAssurance` throws on a skip — and every signature is bound to the balance it vouched for, so `assuranceState` reports a moved balance as **void, not stale**: a certification over a different number does not stand. `assuranceCoverage` is what a period-lock gate reads.

```jsx
const rec = signAssurance(undefined, { actor: "R. Chen", authority: "Preparer", balance: 96881.5 });
assuranceState(rec, 96881.5).stage            // "prepared"
assuranceState(rec, 97000).voided             // true — the balance moved
<AssuranceChip record={rec} balance={96881.5} showActor />
<CertificationMatrix accounts={leaves} records={byCode} balances={byCode} onOpenAccount={open} />
```

Reach for it on a trial balance (an Assurance column, row → Account Sheet), on an Account Sheet (the strip plus the one advancing verb), and on Period Close (the matrix is the close's real progress bar; the lock gate names the uncovered material accounts). Pair with `DecisionSnapshot` when a signature should bind to what was reviewed, not only to the figure.
