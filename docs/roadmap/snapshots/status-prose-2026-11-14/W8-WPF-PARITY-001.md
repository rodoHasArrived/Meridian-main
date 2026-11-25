# W8-WPF-PARITY-001 status-detail migration snapshot

Captured from `docs/roadmap/data/roadmap-items.yml` at `3503ae000e` on 2026-11-14.
This retains the original implementation detail, reservations and historical claims verbatim.
Read the current registry for status and decisions; this snapshot does not grant new acceptance.
Detailed boundaries remain applicable until a later scoped change replaces them.

```yaml
    current_summary: Reactivated 2026-07-06. The WPF desktop workstation returns to the active product/UI lane as a co-equal surface alongside the browser workstation. The desktop shell already projects the seven canonical workspaces (Trading, Portfolio, Accounting, Reporting, Strategy, Data, Settings) over shared contracts and read models. Remaining work closes the parity gap with browser-first screens that shipped while WPF was deferred, tracked in `docs/engineering/plans/wpf-web-ui-alignment-plan.md`. Both surfaces continue to consume shared `Meridian.Ui.Services`, `Meridian.Ui.Shared`, and `Meridian.Contracts` seams so neither client forks product state.
```
