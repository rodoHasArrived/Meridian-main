# Web Workstation Screenshot Audit

Date: 2026-06-20 UTC

Scope: combined UX and accessibility audit of the regenerated browser workstation screenshot
catalog in `docs/screenshots/web`. The evidence is fixture-backed, captured at a 1440 x 1100
viewport, and validated against the route manifest. This pass does not cover the WPF desktop
screenshot catalog.

Evidence used:

- Screenshot set: `docs/screenshots/web/*.png`
- Capture manifest: `artifacts/web-screenshots/manifest.json`
- Capture routes: `scripts/dev/web-screenshot-routes.json`
- Validation command: `python scripts/dev/validate-screenshot-captures.py --surface web --output-dir docs/screenshots/web --require-fresh`

## Step Health

| Step | Screenshot | Route | Health | Notes |
| --- | --- | --- | --- | --- |
| 1 | `web-overview-workspace.png` | `/trading` | Watch | Useful operational overview, but it duplicates the Trading route and keeps the catalog less intentional. |
| 2 | `web-trading-workspace.png` | `/trading` | Watch | Core trading state is visible. The compact workflow dock reduces header dominance, but the first viewport still carries shell chrome, metrics, tabs, posture, and route cards at once. |
| 3 | `web-trading-orders.png` | `/trading/orders` | Watch | Order workflow is discoverable, but route-specific content still shares much of the same above-fold structure as the Trading shell. |
| 4 | `web-trading-positions.png` | `/trading/positions` | Watch | Position state is readable, but selected-row detail and downstream evidence remain visually distant in the full-page capture. |
| 5 | `web-trading-risk.png` | `/trading/risk` | Watch | Risk posture is clear. Compact labels and small controls still need rendered target-size and keyboard-order proof. |
| 6 | `web-operator-readiness-console.png` | `/trading/readiness` | Healthy | Readiness state, primary blocker, and next action remain clear. Dense evidence tables still need a separate accessibility pass. |
| 7 | `web-portfolio-workspace.png` | `/portfolio` | Watch | Portfolio metrics and evidence are visible, but similar card weights make the first decision point less obvious. |
| 8 | `web-portfolio-attribution.png` | `/portfolio/attribution` | Watch | Attribution content is structured, but the shared shell and overview materials still push task content down. |
| 9 | `web-portfolio-brokerage-sync.png` | `/portfolio/brokerage-sync` | Healthy | Brokerage state and account sync evidence have a clear frame and readable summary posture. |
| 10 | `web-accounting-workspace.png` | `/accounting` | Watch | Source now gates the close-cockpit landing through task-mode section visibility, with hash deep links forcing requested proof sections visible; screenshot regeneration is still needed before this row can be promoted. |
| 11 | `web-accounting-reconciliation.png` | `/accounting/reconciliation` | Watch | Reconciliation evidence grouping is strong, but many same-weight panels still make escalation priority hard to scan. |
| 12 | `web-accounting-security-master.png` | `/accounting/security-master` | Watch | Security coverage is visible, but dense status tiles and compact route actions need keyboard and screen-reader verification. |
| 13 | `web-accounting-approvals.png` | `/accounting/approvals` | Watch | Approval queue has a clear purpose, but approval posture still relies on many small badges and table rows. |
| 14 | `web-accounting-external-gl-reconciliation.png` | `/accounting/reconciliation` | Watch | External GL evidence is visible, but sharing the same route as reconciliation still weakens catalog distinctness. |
| 15 | `web-accounting-exceptions.png` | `/accounting/exceptions` | Healthy | Exception queue keeps the strongest problem-action shape in Accounting: case queue, cause, gate, and action are visible. |
| 16 | `web-reporting-workspace.png` | `/reporting` | Watch | Reporting overview is coherent, but repeated report-pack panels make route distinction subtle. |
| 17 | `web-reporting-report-packs.png` | `/reporting/report-packs` | Watch | Report-pack state is understandable, but it visually overlaps heavily with the Reporting workspace capture. |
| 18 | `web-reporting-evidence-workbench.png` | `/reporting/evidence` | Healthy | Evidence workbench has a bounded task and lower visual noise than most routes. |
| 19 | `web-reporting-exports.png` | `/reporting/exports` | Healthy | Export workbench makes the action area and export rows visible without excessive above-fold clutter. |
| 20 | `web-strategy-workspace.png` | `/strategy` | Watch | Strategy overview is useful, but the chart and proof panels appear low in a long scroll path. |
| 21 | `web-strategy-promotions.png` | `/strategy/promotions` | Watch | Promotion state is present, but route-specific distinction from the Strategy workspace is modest. |
| 22 | `web-strategy-lab.png` | `/strategy/lab` | Healthy | Strategy lab exposes the chart, mode tabs, and selected proof area clearly. |
| 23 | `web-strategy-quant-lab.png` | `/strategy/quant-lab` | Healthy | Quant Lab has a simple empty/editor state and a clear run action. |
| 24 | `web-strategy-designer.png` | `/strategy/designer` | Healthy | Builder layout has strong three-column structure, visible validation state, and clear proof output. |
| 25 | `web-data-workspace.png` | `/data` | Healthy | Data command deck and provider management are discoverable with a clear workspace frame. |
| 26 | `web-data-watchlist.png` | `/data/watchlist` | Healthy | Watchlist empty and add states are understandable and not visually overloaded. |
| 27 | `web-data-live-quotes.png` | `/data/quotes` | Watch | Source now renders the no-symbol state as a guided empty state with starter-symbol, watchlist, and search actions; screenshot regeneration is still needed before this row can be promoted. |
| 28 | `web-data-backfills.png` | `/data/backfills` | Watch | Backfill route is readable, but it resembles the Data workspace capture closely. |
| 29 | `web-settings-workspace.png` | `/settings` | Watch | Source now includes a Profile appearance panel with theme and density controls; screenshot regeneration is still needed before this row can be promoted. |
| 30 | `web-settings-preferences.png` | `/settings/preferences` | Watch | Source/tests now route this path to provider connection tasks ahead of hash inference; the screenshot still needs regeneration to prove the distinct state visually. |
| 31 | `web-settings-integrations.png` | `/settings/integrations` | Watch | Source/tests now route this path to accounting systems tasks ahead of hash inference; the screenshot still needs regeneration to prove the distinct state visually. |

## Strengths

- The seven-workspace navigation is consistent across the regenerated catalog, and the visible root
  set stays aligned to Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.
- The global workflow dock is now less visually dominant than in the prior audit, preserving
  operator continuity without taking over the first viewport.
- Accounting now exposes a recovery navigator near the top of the route, which makes close center,
  external GL, posture, and exception jumps easier to discover.
- Most routes show explicit evidence posture through badges, retained evidence links, route labels,
  and disabled reasons instead of hiding blocked state.
- The strongest screens still have a clear task spine: Operator Readiness Console, Accounting
  Exceptions, Reporting Evidence Workbench, Reporting Exports, Strategy Lab, Strategy Designer, Data
  Workspace, and Data Watchlist.

## UX Risks

1. Several route variants remain near-duplicates in the screenshot catalog. Settings source now
   resolves `/settings/preferences` and `/settings/integrations` to distinct task states, but the
   screenshots still need regeneration; Trading/Reporting variants also share substantial above-fold
   material.
2. Accounting source now has task-mode section visibility for the close cockpit and hash-targeted
   proof sections, but the screenshot catalog still needs regeneration before the shorter landing is
   visible as evidence.
3. Settings still needs refreshed catalog proof. The source route state is distinct, but current
   screenshot evidence does not yet show the preferences or integrations task views.
4. Empty states vary in clarity. Data Live Quotes now has source coverage for a guided no-symbol
   state, but the screenshot catalog still needs regeneration before that improvement is visible.
5. Detail panels and selected-row evidence are sometimes visually far from the row or card that
   controls them, especially in long trading and accounting screenshots.

## Accessibility Risks

- Darker amber and muted tokens reduce screenshot-visible contrast risk, but screenshots alone do
  not prove WCAG contrast for every badge, chip, and inactive label.
- Many compact action chips, tabs, table controls, and evidence buttons still need rendered
  pointer-target and keyboard focus verification.
- Color is usually paired with text, which is good, but dense ready/review/blocked palettes still
  need contrast checks against both light panels and dark cards.
- Screenshot evidence cannot confirm keyboard order, focus visibility, table semantics, live-region
  announcements, or screen-reader names for compact controls.
- Long pages may create focus-management risk when row activation updates a detail panel far below
  or beside the triggering element.

## Recommendations

1. Regenerate Settings path screenshots so the catalog shows the route-aware Preferences and
   Integrations task states now covered by source tests.
2. Continue reducing duplication by either capturing more distinct route states or documenting when
   a route intentionally shares the same view.
3. Regenerate Accounting captures for close control, reconciliation, ledger inquiry, external GL,
   private capital, and reporting support so the task-mode visibility changes are represented.
4. Regenerate the Live Quotes capture and continue tightening empty states so the next productive
   action is first, with dependent controls hidden or de-emphasized until there is a selected row or
   symbol.
5. Run a rendered accessibility pass for keyboard order, focus indicators, target sizes, and
   contrast. Screenshots support likely-risk findings, not compliance claims.
   Partially addressed in source (2026-07-02): jsdom axe suites now cover all seven workspaces —
   dedicated `src/screens/*.a11y.test.tsx` suites were added for Settings (per task view), Trading,
   Portfolio, Reporting, Strategy, and Watchlist alongside the existing embedded axe assertions —
   and the violations they surfaced were fixed: empty control-column table headers in the Trading
   blotters and Strategy run library now carry screen-reader-only names, the Portfolio detail
   panels no longer nest `complementary` landmarks (now labeled `region`s), the Strategy study
   detail empty state no longer puts `role="status"` on an `aside`, and the Settings
   profile-steps/scoped-access/Alpaca-checklist lists now have valid list semantics. Axe in jsdom
   does not prove rendered keyboard order, focus visibility, pointer-target sizes, or contrast —
   that rendered pass remains open.

## Evidence Limits

- This audit is based on regenerated screenshots only. It does not prove keyboard access,
  screen-reader behavior, dynamic loading announcements, pointer target sizing, zoom reflow,
  responsive behavior, or live backend behavior.
- The regenerated screenshots use demo fixtures. They are useful for layout and workflow review but
  should not be treated as evidence of production data accuracy.
- The desktop WPF screenshot catalog under `docs/screenshots/desktop` was not refreshed or audited
  in this pass.
