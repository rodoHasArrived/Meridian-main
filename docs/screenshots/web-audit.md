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
| 10 | `web-accounting-workspace.png` | `/accounting` | Watch | The recovery navigator and compact workflow dock improve the top of the page, but the full-page capture still combines close control, GL evidence, private capital, reconciliation, reporting, and review proof in one long route. |
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
| 27 | `web-data-live-quotes.png` | `/data/quotes` | Needs attention | Empty quote state is valid but ambiguous: the primary next action competes with disabled detail controls and a selected-symbol panel with no selection. |
| 28 | `web-data-backfills.png` | `/data/backfills` | Watch | Backfill route is readable, but it resembles the Data workspace capture closely. |
| 29 | `web-settings-workspace.png` | `/settings` | Needs attention | Settings remains too long for recovery work in the catalog capture, with access, operations, providers, runtime, and diagnostics visible in one long page. |
| 30 | `web-settings-preferences.png` | `/settings/preferences` | Needs attention | The capture renders effectively the same state as Settings overview, so route intent is not visible from the screenshot. |
| 31 | `web-settings-integrations.png` | `/settings/integrations` | Needs attention | The capture also renders effectively the same state as Settings overview, leaving provider/integration recovery work indistinct. |

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

1. Several route variants remain near-duplicates in the screenshot catalog. Settings
   `/settings`, `/settings/preferences`, and `/settings/integrations` currently capture the same
   long rendered state, and Trading/Reporting variants still share substantial above-fold material.
2. Accounting improved above the fold, but the full page remains high-load because several proof
   lanes are visible in one capture. Operators can find recovery jumps faster, but the route still
   needs stronger progressive disclosure for repeated recovery work.
3. Settings is now the highest-risk screenshot group. The route is comprehensive, but the catalog
   evidence does not show distinct task states for preferences or integrations.
4. Empty states vary in clarity. Data Live Quotes shows the valid no-symbol state, but selected
   symbol controls still read as inactive UI rather than a guided next step.
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

1. Make Settings path variants route-aware in the screenshot catalog, or update the route config to
   use hash/task targets that show distinct Preferences and Integrations states.
2. Continue reducing duplication by either capturing more distinct route states or documenting when
   a route intentionally shares the same view.
3. Split Accounting recovery evidence into clearer route-specific captures for close control,
   reconciliation, ledger inquiry, external GL, private capital, and reporting support.
4. Tighten empty states so the next productive action is first, and hide or de-emphasize disabled
   dependent controls until there is a selected row or symbol.
5. Run a rendered accessibility pass for keyboard order, focus indicators, target sizes, and
   contrast. Screenshots support likely-risk findings, not compliance claims.

## Evidence Limits

- This audit is based on regenerated screenshots only. It does not prove keyboard access,
  screen-reader behavior, dynamic loading announcements, pointer target sizing, zoom reflow,
  responsive behavior, or live backend behavior.
- The regenerated screenshots use demo fixtures. They are useful for layout and workflow review but
  should not be treated as evidence of production data accuracy.
- The desktop WPF screenshot catalog under `docs/screenshots/desktop` was not refreshed or audited
  in this pass.
