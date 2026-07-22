The case clock — a mono severity-tinted chip for SLA posture on a reconciliation case or break: state dot · `SLA · on track · due 2h 14m`, flipping to `over by 3h 05m` once past due. Field names mirror the server case model (`SlaState`, `SlaDueAtUtc`, `AgeBand`, `BusinessAgeHours`), so a case row can be spread straight in.

```jsx
<SlaChip state="OnTrack"  dueAtUtc="2026-07-05T18:00:00Z" now="2026-07-05T14:32:00Z" />
<SlaChip state="Breached" dueAtUtc="2026-07-05T09:00:00Z" now="2026-07-05T14:32:00Z" />
```

It never self-ticks — pass `now` (ISO or epoch ms) for deterministic renders; omit it and it reads the wall clock once per render. Falls back to `ageBand`, then `businessAgeHours`, when there's no due time. Sits inline beside a `SeverityBadge` in `CaseQueue` rows.
