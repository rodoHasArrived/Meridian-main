# Screenshot Artifacts

**Status:** supporting-generated
**Owner:** core-team
**Reviewed:** 2026-10-28

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

## Browser screenshot workflow

Run **Web Screenshot Capture** from GitHub Actions to refresh the full browser catalog.
Dispatch captures the default branch; pull requests that change the dashboard or capture harness
render the proposed head commit without publishing. Set `create_pr` to `false` for an artifact-only
manual run. Its default is `true`, which opens or updates `automation/web-screenshot-capture` only
after capture and image validation both pass. A failure to open the refresh PR fails the job.

Each job retains one 14-day archive containing `captures/*.png`, `manifest.json`, and `summary.md`.
The Actions job summary reports capture, validation, and refresh-PR outcomes, the checked-out commit,
per-route attempts, and failures. Artifact names include the run attempt so reruns are distinguishable.
Captures go into ignored `artifacts/web-screenshots/captures`; setup failures cannot upload the
committed catalog as newly rendered evidence. Only a fully validated run copies images into the
requested catalog directory under `docs/screenshots/web` for the refresh PR.

The capture script checkpoints its manifest after each completed route. Failed attempts remove
rejected PNGs, and cleanup failures retain diagnostics and the original render error. An interrupted
run may therefore retain a `running` manifest with completed routes and unattempted counts; it does
not represent a validated catalog. Supplying `--manifest` to the validator requires that file to exist
and, for browser captures, report `status: passed`.

For a local full-catalog run using the same artifact layout:

```bash
npm ci --prefix src/Meridian.Ui/dashboard --include=optional
npx --prefix src/Meridian.Ui/dashboard playwright install chromium
node scripts/dev/capture-web-screenshots.mjs --output-dir artifacts/web-screenshots/captures
python scripts/dev/validate-screenshot-captures.py --surface web --output-dir artifacts/web-screenshots/captures --manifest artifacts/web-screenshots/manifest.json --require-fresh
python scripts/dev/summarize-web-screenshots.py
```

Use `--capture W01` (or a capture name) for focused local capture. Full-catalog validation still
expects every configured image; a selected-route run is diagnostic evidence only. Local summaries
show workflow-step outcomes as “not reported” unless their outcome environment variables are set.

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
