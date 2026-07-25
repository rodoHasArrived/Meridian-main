# Web Workstation Screenshot Audit

Date: 2026-07-25 UTC

## Outcome

The browser-workstation catalog was regenerated after the Accounting structural-polish and Phase E
design-system pass. All 77 fixture-backed captures completed successfully at a 1440 x 1100
viewport, and the strict freshness validator confirmed that the route manifest and PNG inventory
agree. The maintained catalog contains 77 routes rather than the previously documented 75.

| Family | Captures | Review outcome |
| --- | ---: | --- |
| Daily Control Tower | 1 | Clear operator posture, blocker, and next-action framing. |
| Trading | 5 | Route-specific task states remain readable within the shared workstation shell. |
| Portfolio | 6 | Source, run, freshness, and record evidence use operator-facing language; technical references are progressively disclosed. |
| Accounting | 22 | The close cockpit, reconciliation rail, canonical Ledger Explorer, configuration workflow, external GL, approvals, and evidence routes retain only route-relevant work. |
| Reporting | 13 | Setup, run, validation, governance, scheduling, export, and operations-record tasks remain distinct. |
| Strategy | 7 | The Strategy Lab now renders a populated, tokenized twelve-observation chart fixture with selected-marker evidence. |
| Data | 12 | Provider, ingestion, storage-assurance, alert, query, and evidence routes prioritize operator tasks while retaining technical proof. |
| Settings | 11 | Preferences, access, provider setup, accounting systems, and diagnostics remain distinct routes; stale provider evidence fails closed. |
| **Total** | **77** | **Complete current-run catalog.** |

## Evidence

- Screenshots: `docs/screenshots/web/*.png`
- Capture manifest: `artifacts/web-screenshots/manifest.json`
- Route catalog: `scripts/dev/web-screenshot-routes.json`
- Fixture catalog: `scripts/dev/web-screenshot-fixtures.json`
- Capture command: `node scripts/dev/capture-web-screenshots.mjs --port 5173 --output-dir docs/screenshots/web --config scripts/dev/web-screenshot-routes.json --manifest artifacts/web-screenshots/manifest.json`
- Freshness validation: `python scripts/dev/validate-screenshot-captures.py --surface web --output-dir docs/screenshots/web --web-routes scripts/dev/web-screenshot-routes.json --manifest artifacts/web-screenshots/manifest.json --require-fresh`
- Workflow tests: `python tests/scripts/test_refresh_screenshots_workflow.py`
- Browser unit and accessibility suite: `npm --prefix src/Meridian.Ui/dashboard run test`

The capture manifest reports `status: passed`, `selectedCaptureCount: 77`,
`capturedCount: 77`, `failedCaptureCount: 0`, and `totalCaptureCount: 77`. Strict freshness
validation reported 77 expected captures and 77 PNG files. The screenshot workflow contract suite
passed all 35 tests. The full browser suite passed all 259 test files across its 33 maintained
batches, including component accessibility and axe coverage.

## Improvements Confirmed In The Rendered Catalog

- Phase E density, typography, semantic surface, and chart-plot tokens resolve consistently across
  the workstation. The default and compact table-row contract is 32px; the terminal and spacious
  variants remain 26px and 48px respectively.
- Accounting reconciliation uses one comparison-and-casework workspace: statement runs and the
  break queue occupy the master column while case rationale, evidence, ownership, and actions stay
  in the detail rail.
- `/accounting/ledger` is the canonical Ledger Explorer. The shared financial-record explorer owns
  search, filters, and saved views, while run-scoped journal evidence remains subordinate; the old
  hash-only `showLedgerExplorer` alias is intentionally removed.
- Deep Accounting routes no longer inherit the generic workflow, posture, external-GL, and
  multi-asset tail indiscriminately. Each route renders only the sections named by its route model.
- The extracted Accounting configuration panel preserves the existing workflow while separating
  its implementation from the parent Accounting screen.
- Strategy Lab renders a deterministic twelve-point scatter plot, fit line, selected marker,
  labeled axes, and residual context rather than an empty or invented fallback chart.
- The seven root workspaces remain Trading, Portfolio, Accounting, Reporting, Strategy, Data, and
  Settings, with one shared operating-context shell across the catalog.

## Visual Review

The regenerated Accounting workspace, reconciliation, Ledger Explorer, configuration, and Strategy
Lab captures were inspected at their captured viewport for broken layout, cropping, duplicated
route tails, contradictory status, empty chart state, and stale fixture language. No blocking
layout defect was found. The Ledger Explorer has one clear search/saved-view owner, the Accounting
workspace remains a compact master-detail cockpit, and the Strategy chart is visibly populated.

Long administration and evidence routes remain intentionally dense, but their technical content is
confined to routes whose stated purpose is configuration, retained proof, or diagnostics.

## Residual Risks And Evidence Limits

- Screenshots prove rendered layout and visible copy for fixture states; they do not prove live
  backend behavior, production-data accuracy, authorization enforcement, or successful mutations.
- Screenshot inspection cannot prove keyboard order, focus restoration, screen-reader
  announcements, pointer-target sizing, or zoom reflow. Automated unit and accessibility coverage
  is complementary evidence, not a substitute for a rendered assistive-technology pass.
- Twenty-two expert routes exceed 2200px in full-page height. The tallest are Operations Continuity,
  Reporting Run Detail, Accounting Configure, Settings coverage inventories, and evidence
  workbenches. Further progressive disclosure and operator testing remain a post-pass backlog item;
  this release proof does not claim that every expert route meets a universal page-height target.
- The WPF desktop catalog under `docs/screenshots/desktop` was not changed or audited by this web
  pass.

## Follow-Up Validation

Keep the 77-route capture and strict freshness check in the release lane. GitHub Actions remains the
authoritative integration result, and the hosted screenshot workflow should be rerun from `main`
after a human merges the pull request so the published evidence is tied to the protected branch.
