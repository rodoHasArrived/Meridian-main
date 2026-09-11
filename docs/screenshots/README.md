# Screenshot Artifacts

**Status:** supporting-generated
**Owner:** core-team
**Reviewed:** 2026-07-19

This folder stores maintained screenshot references used by documentation and
workflow evidence.

Generated local screenshot runs should stay under ignored artifact locations
unless a reviewer explicitly chooses a new screenshot as durable documentation
evidence.

For scripted capture workflows, see
[docs/development/desktop-workflow-automation.md](../development/desktop-workflow-automation.md)
and `npm run screenshots` from `src/Meridian.Ui/dashboard`.

Captured screenshot sets are validated by
`scripts/dev/validate-screenshot-captures.py` before workflow upload or commit. The validator
checks expected files, capture freshness, manifest route/page identity, PNG dimensions, sampled
color diversity, and luminance entropy so blank, stale, low-entropy, or wrong-route captures do not
become durable documentation evidence.

Manifest coverage is part of the semantic-state evidence gate: every expected browser route or WPF
workflow step must appear in the capture manifest before the retained PNG can count as evidence for
design-system state review. Browser captures also record the actual Playwright URL path after
navigation and fail validation when it differs from the expected route path. A stray PNG with no
matching route/page manifest entry fails validation instead of being accepted as a fresh-looking but
unproven capture. Manifest capture paths must also resolve to the requested output PNG, so an older
or different run cannot satisfy the evidence gate by reusing the expected filename.

The maintained browser catalog in `scripts/dev/web-screenshot-routes.json` is expected to cover the
active workstation page surface: every non-legacy path in `WORKSTATION_ROUTE_CATALOG`, the
workstation root route, and every explicit non-redirect `<Route>` in the dashboard app shell. The
web capture script checks that coverage before launching Playwright. Update the route definition and
fixture-backed wait evidence together when adding a new browser page.

## Semantic-state review

Ready/review/blocked/current/muted mapping is reviewed across browser badges, Evidence Workbench
view models, WPF badge tone resources, and screenshot quality gates. Ready uses success, review
uses warning, and blocked uses danger. Current and muted evidence keep their informational
meaning rather than implying approval. The shared browser tone mappings and WPF theme resources
remain the implementation references; retained screenshots must show the corresponding operator
state as well as pass the capture validator above.

`src/Meridian.Ui/dashboard/src/design-system-contract.test.ts` checks this cross-surface contract.
This guard preserves evidence and operator-state consistency; it does not establish broader
Evidence Vault product acceptance.

## Desktop WPF screenshot index

The maintained desktop WPF coverage index lives at
[`docs/screenshots/desktop/README.md`](desktop/README.md). It maps retained registered WPF page
tags to committed screenshot paths, fixture/data mode, refresh dates, and TBI coverage gaps.
The generated WPF development tracker consumes that index at
[`docs/status/wpf-screen-development-tracker.md`](../status/wpf-screen-development-tracker.md).
