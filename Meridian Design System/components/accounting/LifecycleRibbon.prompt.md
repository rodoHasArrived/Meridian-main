The investment-event lifecycle as a selectable rail — capture, accounting, settlement, custody, income, valuation, reporting — with an expanded lane view for operators who need the parallel tracks.

```jsx
<LifecycleRibbon selectedId={stage} onSelect={setStage} stages={[
  { id:"trade", label:"Trade captured", meta:"May 29", state:"done" },
  { id:"acct",  label:"Accounting generated", meta:"May 29", state:"current" },
  { id:"settle",label:"Settlement expected", meta:"Jun 2", state:"pending" },
]} />
<LifecycleLanes lanes={[{ label:"Cash", steps:[{label:"Payable",state:"done"},{label:"Movement",state:"pending"}] }]} />
```

Selecting a stage must actually synchronize the workspace (schedule, rollforward, next journal, lot, rule, reporting) — a decorative ribbon is worse than none. Use `GateRail` for close/approval gates that block, `Stepper` for a form wizard, and `EventTimeline` for the audit trail of what already happened.
