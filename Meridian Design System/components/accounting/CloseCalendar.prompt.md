An operational control surface for the accounting close — time, responsibility, dependency, evidence, and sign-off over one task set. Not a P&L heatmap and not a month view.

```jsx
<CloseCalendar
  title="Close calendar" period="July 2026"
  days={[{date:"2026-07-31",label:"Day −1 · Jul 31"},{date:"2026-08-01",label:"Close · Aug 1"}]}
  tasks={[{id:"t4",title:"Prices approved",date:"2026-07-31",due:"14:00 UTC",owner:"S. Grant",
           team:"Investment accounting",status:"blocked",dependsOn:["t3"],
           evidence:"96 of 100 sources",blocker:"3 stale prices, 1 uncovered security",
           signoff:"Investment accounting"}]}
  currentDate="2026-08-01" currentTime="16:05 UTC"
  selectedId={selectedId} onSelect={openTask}
  defaultView="calendar"
/>
```

**Three views, one task set.** `calendar` orients in time, `dependency` orders by longest-path depth and indents each task under what it requires, `owner` groups by team with per-owner completion. All three read the same `tasks` array and the same `selectedId`, so a selection made in one view survives a switch to another. Control the view with `view`/`onViewChange` or leave it uncontrolled from `defaultView`.

**Every task carries its operational facts.** Task or control, reporting period, owner and team, due date and time, `dependsOn`, status, evidence completeness, sign-off requirement, blocking reason, completion timestamp. A task without a due time says "No due time" rather than rendering blank.

**Nine statuses, each with a mark and a word.** Not started · in progress · waiting on dependency · at risk · blocked · ready for review · complete · reopened · waived with rationale. Tone is secondary to the glyph and the label; the legend at the foot carries live counts.

**The current-period Datum stays visible** so past-due, current, and upcoming work are distinguishable at a glance without reading dates. Days before the current date are dimmed, not hidden.

**Selection drives the rest of the screen.** `onSelect` is meant to open required evidence, dependency detail, the assigned owner, a `ResolutionDrawer`, an `AttestationPanel`, the related journal or reconciliation, or the audit timeline — the calendar itself stays put.

**Narrow screens** collapse the day grid to a single column agenda rather than compressing a desktop grid; the dependency and owner views are already list-shaped and need no fallback.
