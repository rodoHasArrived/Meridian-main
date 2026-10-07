# Shared browser scenarios

**Status:** active
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-10-07

Use one typed API scenario for a development preview, a Vitest component test, and a Playwright
screenshot. The scenario registry lives in
[`src/Meridian.Ui/dashboard/src/scenarios/`](../../src/Meridian.Ui/dashboard/src/scenarios/index.ts).
Each response declares its HTTP status, body, and optional delay. The consumers use this registry
directly rather than maintaining separate copies of the accounting response.

The shared payload also retains the Approval Inbox sample. Its legacy close-plan summaries have
an explicit scenario-only type extension; the production workspace contract stays separate.
The empty scenario clears those summaries along with the reconciliation queues.

## Preview an accounting state

Use Node.js 22.12 or newer and npm on Windows, macOS, or Linux. Run commands from the repository
root with dashboard dependencies installed, following the
[browser development launcher](../engineering/web-development.md#install-and-choose-a-mode).
Start the fixture-only preview in a terminal:

```sh
npm run dev:fixtures
```

Once the launcher prints `[dev] Ready`, choose a preview below. If you selected another port,
replace `5173` in the URL.

| Scenario | HTTP status | Response behavior | Preview URL |
| --- | --- | --- | --- |
| `accounting.normal` | 200 | Populated accounting workspace and break queue. | [Open normal](http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.normal) |
| `accounting.empty` | 200 | Empty reconciliation and break queues, with explicit empty-state copy. | [Open empty](http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.empty) |
| `accounting.delayed` | 200 | Populated response after 1,500 ms. | [Open delayed](http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.delayed) |
| `accounting.forbidden` | 403 | Forbidden problem response. | [Open forbidden](http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.forbidden) |
| `accounting.failed` | 500 | Failed-service problem response. | [Open failed](http://127.0.0.1:5173/workstation/accounting/reconciliation?scenario=accounting.failed) |

Both `/api/workstation/accounting` and `/api/workstation/reconciliation/break-queue` use the selected status
and delay. Vite reads the selector from the requesting page's Referer, so two preview tabs can
select different scenarios. Direct API clients can send `x-meridian-scenario: accounting.failed`
instead. Responses carry that header so the API client preserves scenario errors instead of
substituting its legacy development fallback. An unknown scenario returns HTTP 400 with the
available IDs; an unsupported API request returns HTTP 501 with its method and URL.

Supporting reconciliation reads are also declared explicitly. Empty scenarios clear statement runs
and cases; sample validation stays blocked because it does not contain retained backend validation.
Forbidden and failed previews show the workstation's existing degraded-data banner. Their exact
403/500 status and problem bodies remain available in the network response and consumer assertions.

## Use the scenario in Vitest and screenshots

In a Vitest test, import `installScenario` from `@/test/scenarios` and call
`installScenario("accounting.normal")` before fetching or rendering. Strict request checking is
enabled by default. The helper restores the preceding fetch and development mode after each test
and fails if unexpected API requests occurred, including rejections caught by application code.
Known read-only development fixtures supply supporting shell requests; writes require an explicit
scenario route. Routes match the HTTP method and complete pathname rather than a pathname prefix.

The [accounting scenario suite](../../src/Meridian.Ui/dashboard/src/scenarios/scenarios.test.tsx)
uses the real API client, production reconciliation queue card, and Approval Inbox, and covers empty states,
delays, cancellation, status preservation, and strict request failures. Run it with the Vite adapter
and screenshot adapter checks:

```sh
npm --prefix src/Meridian.Ui/dashboard run test:vitest -- src/scenarios/scenarios.test.tsx src/vite-config.scenarios.test.ts
node --test scripts/dev/web-screenshot-scenarios.test.mjs
```

Install Playwright Chromium if it is not already available:

```sh
npm --prefix src/Meridian.Ui/dashboard exec -- playwright install chromium
```

Capture the same scenario from another terminal. Port `5174` keeps the capture's Vite server
separate from the preview above, and ignored artifact paths keep local screenshots out of retained
documentation evidence:

```sh
node scripts/dev/capture-web-screenshots.mjs --scenario accounting.normal --list
node scripts/dev/capture-web-screenshots.mjs --scenario accounting.normal --strict --port 5174 --output-dir artifacts/web-scenarios/accounting-normal --manifest artifacts/web-scenarios/accounting-normal/manifest.json
```

`--scenario` selects the accounting reconciliation capture automatically when no `--capture`
selector is supplied. The Playwright adapter intercepts API calls before navigation, applies the
shared statuses and delays, and waits for the scenario's readiness text. `--strict` fails capture
on an unexpected API request and reports its scenario, method, and URL. The manifest records the
selected scenario and preview URL. To use an already installed system Chromium, set
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to its executable path before running capture.

## Demonstrate one payload edit across all three consumers

1. In [`accounting-payload.ts`](../../src/Meridian.Ui/dashboard/src/scenarios/accounting-payload.ts),
   edit only `accountingPayload.reconciliationQueue[0].strategyName` to a recognizable label.
2. Reload the `accounting.normal` preview and inspect the first reconciliation queue row.
3. Run the scenario Vitest suite above. Its production-card row assertion derives the label from
   `accountingPayload`, so the changed label is verified without updating test data or assertions.
4. Run the strict `accounting.normal` capture above. Its readiness text derives from the same
   queue, and the PNG shows the changed label. No screenshot JSON payload needs editing.

[`accounting.ts`](../../src/Meridian.Ui/dashboard/src/scenarios/accounting.ts) reuses that payload
for normal and delayed responses and derives the empty response from it. The compatibility
development fixture resolver also reads the shared payload. Keep fixture changes in this module
instead of copying accounting records into tests or `web-screenshot-fixtures.json`.

## Add another scenario

1. Put its payload in a domain module under `src/Meridian.Ui/dashboard/src/scenarios/` and type it
   against the existing API response DTO.
2. Define an `ApiScenario` with a unique `id`, `previewPath`, and `responses`. Every route specifies
   `method`, `path`, and a response containing `status` and `body`; use `delayMs` for delayed reads
   and `headers` when the contract needs them. Declare readiness text in `waitForTexts` and,
   optionally, stale text that must disappear in `waitForAbsentTexts`.
3. Register the scenario in `src/scenarios/index.ts`. Import its payload in consumer assertions,
   and use its ID through the URL selector, `installScenario`, and `--scenario` capture flag.
4. Add behavior coverage for the new state and run strict capture. An unexpected request names
   the missing method and URL; add a deliberate route instead of broadening a pathname match.

Retained screenshots still follow the [screenshot artifact policy](../screenshots/README.md).
Fixture-based checks verify browser behavior; shared service and operator acceptance procedures
remain linked from [Engineering](../engineering/README.md#buildtestrun).
