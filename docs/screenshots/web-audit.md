# Web Workstation Screenshot Audit

Date: 2026-11-11 UTC

The original program audit below describes PR #3497's source branch. The combined
Design System 4.3 integration is recorded at the end of this document.

## Outcome

The browser UX program regenerated all **82 fixture-backed routes** at 1440 × 1100.
The completed manifest reports 82 passed and zero failed captures. Freshness validation
confirmed 82 expected routes and 82 matching PNG files. The two new captures cover the
separate Factor Paydown and Consolidation workflows.

| Family | Captures | Current assessment |
| --- | ---: | --- |
| Daily Control Tower | 1 | Combined queue appears without selecting scope; health and priority stay compact. Fixture persistence is explicitly unavailable. |
| Trading | 5 | Recovery remains visible; Orders and Fills share one active detail rail. Metrics and technical confidence are disclosed separately. |
| Portfolio | 7 | Source figures retain basis/context; positions expose additional decision fields. Missing currency, history or equity stays unavailable. |
| Accounting | 26 | Dedicated mutation routes, compact investigation rails, scoped comparisons and collaboration replace stacked workflows. |
| Reporting | 13 | Export browsing uses the shared workbench; governed lifecycle, retained amounts and decision controls preserve their authority. |
| Strategy | 7 | Run browsing exposes retained performance and trends; absent measurements are not replaced with illustrative production statistics. |
| Data | 12 | Provider health leads the overview; freshness, coverage and retained history have text/table alternatives. Upload previews are labeled explicitly. |
| Settings | 11 | Evidence-based setup checklist replaces the repeated navigation table. Detailed configuration remains on its own routes. |
| **Total** | **82** | **Current synthetic catalog complete.** |

## Evidence

- Screenshots: `docs/screenshots/web/*.png`
- Manifest: `artifacts/web-screenshots/manifest.json` (generated artifact)
- Route catalog: `scripts/dev/web-screenshot-routes.json`
- Fixture catalog: `scripts/dev/web-screenshot-fixtures.json`, plus the typed accounting scenario
- Capture: `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=/usr/bin/chromium node scripts/dev/capture-web-screenshots.mjs --skip-server --port 5189 --output-dir docs/screenshots/web --manifest artifacts/web-screenshots/manifest.json --readiness-timeout-ms 12000`
- Freshness: `python3 scripts/dev/validate-screenshot-captures.py --surface web --output-dir docs/screenshots/web --web-routes scripts/dev/web-screenshot-routes.json --manifest artifacts/web-screenshots/manifest.json --require-fresh`
- Capture-contract tests: `python3 -m unittest tests.scripts.test_refresh_screenshots_workflow` — 41 passed.

The maintained fixture Vite server ran in explicit `fixture-only` mode. Capture readiness now
checks current headings and the selected reconciliation rail itself, rather than removed shell
headings or collapsed metrics. Capture success is not evidence that an unsupported API exists.

## Rendered review

The shared shell keeps seeded provenance visible, uses one route H1 and combines operating
scope and workflow context. Selected rows use blue-grey rather than the danger treatment.
The footer reports synchronization/connection state. Mandatory trading recovery remains visible.

A separate 20-route/viewport layout inspection covered ten task routes at 1440 and 1366 pixels.
Every inspected route had one H1, no document-level horizontal overflow and no JavaScript page
error. The task region began at 101 pixels at both widths, and the largest measured initial task scroll
extent was 1,901 pixels. These measurements describe the task region, not the first table row:
Today's first queue row was at 445 pixels and its first priority action was at 130 pixels at both widths.
Expanded evidence, warnings and configuration inventories are not hidden to satisfy a height target.

Additional rendered checks exercised scoped Paydown navigation, exact-decimal comparison CSV,
authoritative cancellation preview, Today assignment/snooze/retry outcomes, approval discussion,
Reporting keyboard selection, and figure-context hover/focus/Escape. Financial writes were blocked
in cancellation/Accounting inspection. Stateful UI checks used explicit synthetic API interception.
Four additional light/dark checks at 1440/1366 verified visible table input borders, opaque menus,
sticky selected-cell backgrounds and left/right overflow shadows, with zero page errors.

Browse tables use the shared toolbar and contained scrolling; financial statements and editable
worksheets retain their appropriate layouts. The screen AST governance baseline is empty for its
checked raw-table, literal-status-badge and JSX formatter patterns. This is scoped coverage, not
proof that every possible status or financial presentation is automatically checked.

## Evidence limits

- These are synthetic fixtures, not live-provider or production-data verification.
- Chromium on Linux was available. Windows Chrome/Edge and assistive-technology verification
  remain release-environment checks; screenshot success does not prove those environments.
- The whole catalog was captured; focused workflows and selected dense screens received deeper
  interaction/layout inspection. An 1100-pixel PNG alone does not establish full route height.
- Missing retained figure subjects remain explicitly unverified. Legacy Close history gaps,
  missing currency and absent observations are not synthesized. See the program's source limits.
- The WPF source and desktop screenshots were not changed. Browser-first differences are recorded
  in `docs/engineering/plans/wpf-web-ui-alignment-plan.md`.

The implementation and verification record is in
[`browser-workstation-ux-program.md`](../engineering/plans/browser-workstation-ux-program.md).
Hosted checks remain the integration authority.

## Design System 4.3 integration verification

The combined PR #3501 tree refreshed all 82 synthetic routes in four disjoint successful
capture runs. Coverage, actual route identity, image quality and freshness passed validation.
The existing WPF changes in #3501 are preserved; this browser program authors no WPF changes.

Eighteen interaction groups passed at 1920 × 1080, 1440 × 900 and 1366 × 768. Eighteen axe
scans reported no violations; manual incomplete checks remain. All 28 navigation mask checks
passed in light, dark and forced-color appearances. Masthead text measured 11.34:1 in light
appearance and 16.38:1 in dark appearance. All ten WOFF2 faces loaded and decoded without
font errors. No runtime errors, missing assets or page-wide horizontal overflow were observed.

Today has one local authoritative priority row and a default authorized queue. Source, scope,
retained history, personal triage, complete exports and technical identities remain inspectable.
Run identities stay copyable in collapsed technical details. Limit and Stop typing retain
`188.0` → `188.05`; local review acknowledgement cannot carry into another account scope.
Those input checks used synthetic data and prevented all API writes.

These remain Linux Chromium and synthetic-data results. Windows, assistive technology, live
providers and production activation are outside this evidence. See the implementation record
linked above for builds, tests and remaining source limits.

Final accessible-name checks retain the full native masthead summary, including screen-reader
provenance, and keep the visible Inspect record label in its accessible name. The maintained
smoke checks open retained Portfolio evidence explicitly, separately from current holdings.
Six exact read-only smoke fixtures provide empty personal views or explicitly unavailable/empty
history; they exercise no persistence, provider uptime or live financial operations.

The final maintained browser command passed all 64 checks (22 base and 42 expanded). All 15
axe scans reported zero violations; nine scans retain incomplete/manual checks. No unexpected
runtime, asset or expanded request errors occurred, and all 163 published files remained
byte-identical. Seventeen known fixture gaps remain explicit. This is synthetic Linux Chromium
evidence, not live-provider or native Windows certification.
