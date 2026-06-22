# Screenshot Artifacts

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

## Desktop WPF screenshot index

The maintained desktop WPF coverage index lives at
[`docs/screenshots/desktop/README.md`](desktop/README.md). It maps active registered WPF page
tags to committed screenshot paths, fixture/data mode, refresh dates, and TBI coverage gaps.
The generated WPF development tracker consumes that index at
[`docs/status/wpf-screen-development-tracker.md`](../status/wpf-screen-development-tracker.md).
