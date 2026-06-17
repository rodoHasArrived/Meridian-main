# Web Workstation Screenshot Audit

Date: 2026-06-17 UTC

Scope: combined UX and accessibility audit of the refreshed browser workstation screenshot catalog in `docs/screenshots/web`. The evidence is fixture-backed, captured at a 1440 x 1100 viewport, and validated against the route manifest. This pass does not cover the WPF desktop screenshot catalog.

Evidence used:

- Screenshot set: `docs/screenshots/web/*.png`
- Capture manifest: `artifacts/web-screenshots/manifest.json`
- Capture routes: `scripts/dev/web-screenshot-routes.json`
- Validation command: `python scripts/dev/validate-screenshot-captures.py --surface web --output-dir docs/screenshots/web --require-fresh`

## Step Health

| Step | Screenshot | Route | Health | Notes |
| --- | --- | --- | --- | --- |
| 1 | `web-overview-workspace.png` | `/trading` | Watch | Useful operational overview, but it duplicates the Trading route and makes the catalog look less intentional. |
| 2 | `web-trading-workspace.png` | `/trading` | Watch | Core trading state is visible, but the first viewport has dense chrome, workflow cards, metrics, tabs, and posture panels competing for attention. |
| 3 | `web-trading-orders.png` | `/trading/orders` | Watch | Order workflow is discoverable, but route-specific content does not visually separate enough from the shared Trading shell. |
| 4 | `web-trading-positions.png` | `/trading/positions` | Watch | Position state is readable, but primary row selection and downstream detail could be more visually tied together. |
| 5 | `web-trading-risk.png` | `/trading/risk` | Watch | Risk posture is clear, but compact labels and many small controls raise scan and target-size risk. |
| 6 | `web-operator-readiness-console.png` | `/trading/readiness` | Healthy | Readiness state, primary blocker, and next action are clear. Dense evidence tables remain a later accessibility check. |
| 7 | `web-portfolio-workspace.png` | `/portfolio` | Watch | Portfolio metrics and evidence are visible, but similar cards make the first decision point less obvious. |
| 8 | `web-portfolio-attribution.png` | `/portfolio/attribution` | Watch | Attribution content appears structured, but the heavy shared header pushes task content down. |
| 9 | `web-portfolio-brokerage-sync.png` | `/portfolio/brokerage-sync` | Healthy | Brokerage state and account sync evidence have a clear frame and readable summary posture. |
| 10 | `web-accounting-workspace.png` | `/accounting` | Needs attention | The route is rich but overloaded: ledger, close command center, GL evidence, private capital, reconciliation, and reporting support all compete in one long page. |
| 11 | `web-accounting-reconciliation.png` | `/accounting/reconciliation` | Watch | Reconciliation route has strong evidence grouping, but many same-weight panels make escalation priority hard to scan. |
| 12 | `web-accounting-security-master.png` | `/accounting/security-master` | Watch | Security coverage is visible, but dense status tiles and compact route actions need keyboard and screen-reader verification. |
| 13 | `web-accounting-approvals.png` | `/accounting/approvals` | Watch | Approval queue has a clear purpose, but approval posture relies on many small badges and table rows. |
| 14 | `web-accounting-external-gl-reconciliation.png` | `/accounting/reconciliation` | Watch | External GL evidence is visible, but sharing the same route as reconciliation may confuse catalog consumers. |
| 15 | `web-accounting-exceptions.png` | `/accounting/exceptions` | Healthy | Exception queue has the strongest problem-action shape in Accounting: case queue, cause, gate, and action are visible. |
| 16 | `web-reporting-workspace.png` | `/reporting` | Watch | Reporting overview is coherent, but repeated report-pack panels make the route distinction subtle. |
| 17 | `web-reporting-report-packs.png` | `/reporting/report-packs` | Watch | Report-pack state is understandable, but it visually overlaps heavily with the Reporting workspace capture. |
| 18 | `web-reporting-evidence-workbench.png` | `/reporting/evidence` | Healthy | Evidence workbench has a clearer bounded task and lower visual noise than most routes. |
| 19 | `web-reporting-exports.png` | `/reporting/exports` | Healthy | Export workbench makes the action area and export rows visible without excessive above-fold clutter. |
| 20 | `web-strategy-workspace.png` | `/strategy` | Watch | Strategy overview is useful, but the chart and proof panels appear low in a long scroll path. |
| 21 | `web-strategy-promotions.png` | `/strategy/promotions` | Watch | Promotion state is present, but route-specific distinction from the Strategy workspace is modest. |
| 22 | `web-strategy-lab.png` | `/strategy/lab` | Healthy | Strategy lab exposes the chart, mode tabs, and selected proof area clearly. |
| 23 | `web-strategy-quant-lab.png` | `/strategy/quant-lab` | Healthy | Quant Lab has a simple empty/editor state and a clear run action. |
| 24 | `web-strategy-designer.png` | `/strategy/designer` | Healthy | Builder layout has strong three-column structure, visible validation state, and clear proof output. |
| 25 | `web-data-workspace.png` | `/data` | Healthy | Data command deck and provider management are discoverable with a clear workspace frame. |
| 26 | `web-data-watchlist.png` | `/data/watchlist` | Healthy | Watchlist empty and add states are understandable and not visually overloaded. |
| 27 | `web-data-live-quotes.png` | `/data/quotes` | Needs attention | Empty quote state is valid but ambiguous: the primary next action competes with disabled detail controls and a selected-symbol panel that has no selection. |
| 28 | `web-data-backfills.png` | `/data/backfills` | Watch | Backfill route is readable, but it resembles the Data workspace capture closely. |
| 29 | `web-settings-workspace.png` | `/settings` | Watch | Settings posture is comprehensive, but profile, access, provider, and workflow controls are dense for first-pass setup. |
| 30 | `web-settings-preferences.png` | `/settings/preferences` | Watch | Preferences are present, but the screenshot does not make a single save/review task obvious. |
| 31 | `web-settings-integrations.png` | `/settings/integrations` | Watch | Integration management is visible, but provider status, credentials, and routing controls need stronger grouping for repeated operator use. |

## Strengths

- The seven-workspace navigation is consistent across the refreshed catalog, and the visible root set stays aligned to Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.
- The shell gives operators persistent context: mode, source fixture status, provider warning count, paper/live status, search, and breadcrumb/workspace labels.
- Most routes show explicit evidence posture through badges, retained evidence links, route labels, and disabled reasons instead of hiding blocked state.
- The strongest screens have a clear task spine: Operator Readiness Console, Accounting Exceptions, Reporting Evidence Workbench, Reporting Exports, Strategy Lab, Strategy Designer, Data Workspace, and Data Watchlist.

## UX Risks

1. Above-fold density is the dominant issue. Shared shell chrome, operating-context cards, decision briefs, workflow strips, metrics, route tabs, and content panels often compete before the route's main job appears.
2. Several catalog entries are near-duplicates because they capture the same route or highly similar route states. That weakens the screenshot set as workflow evidence and makes route ownership harder to review.
3. Accounting is the highest-risk experience. It surfaces many important proof lanes in one page, but the long full-page screenshot shows too many dense responsibilities without a clear progressive path.
4. Empty states vary in clarity. Data Live Quotes shows the valid no-symbol state, but the selected-symbol detail panel still presents disabled controls that can read as broken rather than unavailable.
5. Detail panels and selected-row evidence are sometimes visually far from the row or card that controls them, especially in long trading and accounting screenshots.

## Accessibility Risks

- Small uppercase labels, compact badges, and low-contrast secondary text on the dark shell may not meet contrast or readability expectations at default zoom.
- Many small action chips and table controls may not meet comfortable pointer target size, especially in route tabs, `Rows` buttons, filters, and compact evidence actions.
- Color is usually paired with text, which is good, but dense ready/review/blocked palettes still need contrast checks against both light panels and dark cards.
- Screenshot evidence cannot confirm keyboard order, focus visibility, table semantics, live-region announcements, or screen-reader names for compact controls.
- Long pages may create focus-management risk when row activation updates a detail panel far below or beside the triggering element.

## Recommendations

1. Pick one primary route task per screenshot and make its next action visually dominant above the fold; move secondary proof panels below the first task.
2. Reduce duplication in the catalog by either capturing more distinct route states or documenting when a route intentionally shares the same view.
3. Split Accounting into clearer progressive sections or route-specific captures for close control, reconciliation, ledger inquiry, private capital, and reporting support.
4. Tighten empty states so the next productive action is first, and hide or de-emphasize disabled dependent controls until there is a selected row or symbol.
5. Run a follow-up rendered accessibility pass for keyboard order, focus indicators, target sizes, and contrast. Screenshots support likely-risk findings, not WCAG compliance claims.

## Evidence Limits

- This audit is based on refreshed screenshots only. It does not prove keyboard access, screen-reader behavior, dynamic loading announcements, pointer target sizing, zoom reflow, responsive behavior, or live backend behavior.
- The refreshed screenshots use demo fixtures. They are useful for layout and workflow review but should not be treated as evidence of production data accuracy.
- The desktop WPF screenshot catalog under `docs/screenshots/desktop` was not refreshed or audited in this pass.
