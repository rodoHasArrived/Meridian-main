# Web UI Structural Mockups

**Status:** proposal artifacts
**Owner:** core-team
**Reviewed:** 2026-07-12

Static, self-contained HTML mockups for the structural changes proposed in
[`../../web-ui-structural-improvement-proposal.md`](../../web-ui-structural-improvement-proposal.md).
Open any file directly in a browser — there are no external assets, scripts, or build steps.

These are **proposal artifacts, not production templates**. The production visual reference
remains the `Meridian Design System/` package ("Concrete / Institutional Ops"); these mockups
follow its language (concrete canvas, flat white panels, near-black chrome, steel-blue accent,
2 px corners, Segoe UI + Cascadia Mono) so the structural changes can be judged without a visual
re-theme muddying the comparison. Do not import markup or styles from here into
`src/Meridian.Ui/dashboard/` — implement against the dashboard's token sheet and component
primitives instead.

| File | Shows | Proposal items |
| --- | --- | --- |
| [`01-trading-cockpit.html`](01-trading-cockpit.html) | `/trading/orders` as a viewport-height master–detail cockpit: compact route header, stat strip, full-height blotter, sticky detail rail, masthead status pill, docked tour | P1 P2 P3 P5 P9 P10 P11 |
| [`02-workspace-overview.html`](02-workspace-overview.html) | `/trading` as a short overview: decision queue, readiness gates, recent activity — no hero prose, no route-card grid | P2 P4 |
| [`03-settings-task-route.html`](03-settings-task-route.html) | `/settings/providers` as a routed task page with a wizard entry point instead of the inline integration workbench | P7 (+P1/P9 layout) |
| [`04-degraded-states.html`](04-degraded-states.html) | Before/after of degraded-state consolidation on the Data workspace: one condition → one state → one action | P6 (+P8 severity) |

The yellow bar at the top of each mockup and the small `P#` annotation chips are annotation
devices for review only; they are not part of the proposed UI.
