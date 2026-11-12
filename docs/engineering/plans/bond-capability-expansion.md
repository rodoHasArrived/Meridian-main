# Bond Capability Expansion

**Owner:** Instruments / Accounting and Ledger
**Reviewed:** 2026-10-29
**Direction:** user-approved staged expansion, including a reviewed full active-bond cutover

## Outcome and boundaries

One explicit set of retained bond economics must explain reference data, expected payments,
valuation, settlement and accounting support. Security Master remains the identity and terms
authority. Instruments owns interpretation; Financial Operations owns governed posting candidates;
Ledger remains the immutable journal authority. Browser integration extends existing Security Master
details. WPF changes, overnight compounding, inflation-index forecasts, continuous exercise models,
OAS, convertibles and structured-credit accounting are outside this increment.

## Delivery and acceptance

| Area | Required behavior | Evidence |
| --- | --- | --- |
| Canonical terms | ZeroCoupon resolves to zero; missing fixed/floating rates never become zero; explicit rate units survive calculation | Codec-to-projection regression tests |
| Schedules | Declared bonds share contextual kernel day counts, calendar adjustments and stable period identities across consumers | Declared schedule and Asset Operations tests |
| Historical inputs | One terms version and recorded cutoff reaches curves and cash flows; unavailable retained inputs block | Calculation-context and receipt replay tests |
| Analytics | Per-100 clean/dirty prices, settlement accrued interest, annual fractional yields, duration, convexity and DV01; discrete exercise scenarios remain analytical | Independent numerical examples and round-trip/finite-difference tests |
| Reference | Canonical principal schedules, coupon structures and exercise terms survive mapping; principal factors remain separate from inflation | Reference mapping and PostgreSQL keyset-page tests |
| Migration | Preview binds terms, proposed conventions and economic differences; staging creates a normal workbench draft | Stale/tampered preview and workbench integration tests |
| Cutover | Full active inventory is hash-bound; every bond declares supported conventions or a version-bound reviewed block | Inventory/cutover persistence and live-policy tests |
| Browser | Imported values and calculated results remain distinct; input dates, units, provenance and blockers are inspectable | API/component tests and desktop Chromium QA |

Declared amortization uses model v5. The existing PR #3328 default v2 and remaining-face /
yield-to-worst v3 models retain their schemas, admission and replay arithmetic.

## Public seams

- `BondCalculationContext` names economic date, settlement date, recorded cutoff and pinned inputs.
- `IBondAnalyticsService` produces metrics, projected payments, capabilities and typed blockers.
- Cash-flow context overloads preserve existing call signatures as compatibility adapters.
- Reference array routes remain available; paged routes use bounded keyset cursors.
- Migration preview and staging routes require Security Master modification authority. Cutover and
  inventory review require administrative authority; actors are resolved from the authenticated
  request. Staging never approves or publishes its own draft.

## Migration procedure

1. Read the complete active-bond inventory; retain its hash and per-security version/hash.
2. Assign explicit coupon units, roll/stub behavior, calendars and payment conventions from reviewed
   contractual evidence. Do not infer a percent/fraction unit from the rate's magnitude.
3. Preview before/after payments and available downstream impacts. Missing valuation/accounting
   evidence is a gap, not a zero difference. Review batches of at most 100 securities.
4. Stage the hash-bound previews through Security Master, then complete its ordinary independent
   approval and publication workflow. Changed terms invalidate the preview.
5. Refresh the complete inventory. Resolve every remaining unsupported/undeclared security or name
   an explicit reviewed block bound to its retained version and content hash.
6. Submit the fresh inventory hash to enable the durable live-calculation gate. A stale inventory
   refuses cutover. Subsequent undeclared or explicitly blocked records cannot silently use legacy
   calculations. Repeated requests must retain the original committed receipt.

Steps 5–6 have an operator console in both workstations: the browser Accounting configuration
section (`accounting-screen.bond-cutover.tsx`) and the WPF Admin maintenance page
(`BondCutoverConsoleViewModel`). Both require AdminMaintenance, read the inventory only on demand,
bind each block to the row's retained version and content hash, and send the current receipt hash
when amending; the server records the authenticated actor as reviewer.

Reference ladders: both workstations now page the issuer and maturity ladders through the bounded
keyset routes (`.../issuer-ladder/page`, `.../maturity-ladder/page`). The unbounded array routes
(`.../issuer-ladder`, `.../maturity-ladder`) stay only for the reference-data endpoint catalog; retire
them in a reviewed follow-up once no retained workflow evidence names them, with a roadmap note rather
than a silent removal.

Existing posted journals and published evidence are never rewritten by this procedure. Historical
receipt replay retains its calculation version; corrections use existing reversal/restatement
workflows. Recorded timestamps establish eligibility among retained rows, not historic transaction
visibility. No customer-data cutover is performed merely by deploying this implementation.

## Validation and operations

Run focused bond, contextual cash-flow, replay, reference and amortization tests; run the service-backed
PostgreSQL fixtures; run dashboard component/API tests and build; then run `bash scripts/ci.sh`.
Required GitHub Actions results remain authoritative. Capture exact results in the change handoff.

After an installation's reviewed cutover, monitor calculation blockers, stale/missing input evidence,
cash-reconciliation differences and posting refusals. A missing or unreadable previously enabled
cutover receipt must fail closed. A deployment rollback must preserve retained receipts and journals;
never restore legacy live calculations by deleting cutover evidence.

## Existing capability access: next delivery priority

The next increment makes retained fixed-income functionality discoverable and usable before adding
new calculation or planning products. Account-specific forecasting **already supports dated FX**;
its access and input preparation need improvement. This is not a new forecast-engine requirement.

The browser feature catalog links to the operation traces in
[`fixed-income-capabilities.ts`](../../../src/Meridian.Ui/dashboard/src/lib/fixed-income-capabilities.ts).
Each operation records **capability → service → API → client → screen/action → permission → browser
acceptance**, including null seams and remaining gaps. Catalog tests check source references and
shared API route identities. Component coverage does not certify browser acceptance; pending browser
scenarios remain pending until executed. These records describe discoverability and evidence, and
never grant permission or replace server authorization.

An operation counts as fully accessible only when an authorized operator can find it, prepare its
supported inputs without internal IDs or JSON, complete the workflow, inspect relevant evidence,
and understand configuration/data blockers. Finding a service, API route, or working form alone
does not meet this standard.

| Capability | Access increment | Remaining supported-workflow gap |
| --- | --- | --- |
| Bond analytics and convention migration | Contextual Asset detail and holding actions; exact Security Master selection, Evidence deep links and command-search shortcuts | Adjusted schedules need a retained-calendar picker; complete browser acceptance must cover governed staging; no automatic approval/publication |
| Issuer and maturity ladders | Browse before security selection; activate rows into exact authoritative instrument detail | Verify query, paging and keyboard drill-through in the rendered screen |
| Contractual cash-flow scenarios | Multiple supported prepayment assumptions and explicit exercise notice dates | Curve/version selection; retained-run enumeration needs store/API support beyond save and get-by-ID |
| Portfolio cash ladder | Contributing instrument links and inspection of the exact retained AssetOperations projection run | Source domain/entity references need an authoritative mapping before becoming Evidence Vault artifact links; contractual run IDs are a different identity family |
| Account forecasts and reviewed close prices | Visible account oversight entry, forecast/close-price shortcuts and structured reuse of retained forecast snapshot inputs | First-run source discovery/assembly needs shared preparation APIs; reuse preserves exact opening observations, projection identities and dated FX rather than inventing missing data |
| Curves and pricing-source hierarchy | Existing API seams catalogued | Browser clients, curve/version inspection, source priority controls, freshness explanations and calculation input selection |
| Settlement consideration | Existing preparation API catalogued | Holding/trade input assembly and preview of clean cost, accrued interest, fees, total cash and reconciliation differences |
| Bond amortization | Existing canonical lot preparation/reversal services catalogued | Governed operator API and browser preview, review, draft-journal, posting and reversal workflows for supported methods |
| Factor paydowns | Existing Ledger explorer preparation/draft/review controls retained and catalogued | Selected holding/factor-observation launch and contextual input preparation; manual IDs and evidence JSON remain a gap |
| Provider-backed bond discovery | Existing provider fixed-income classification below browser search catalogued | Bond-specific provider search, duplicate/identity review and staged Security Master import |

The implementation and acceptance scope of this increment is the first five rows' access work.
API-only and service-only rows remain incomplete. Configuration and data failures must stay explicit;
the workstation must not substitute fixtures or cached financial authority in ordinary operation.
Development browser acceptance may use deterministic, visibly identified synthetic request fixtures.

Validation for the 2026-11-12 browser increment: the focused 15-file regression run passed 306 tests;
an 84-test Accounting/migration follow-up passed after the final form/focus changes. The dashboard
TypeScript/Vite production build passed. Scoped ESLint had no errors or new warnings; the shared
explorer retains four pre-existing hook warnings. Full `scripts/ci.sh` and live-provider verification
were not run for this UI-only increment.

`node scripts/dev/verify-fixed-income-access.mjs` exercises eight synthetic Chromium checks at
1440×900 and 1366×768: issuer/maturity browsing, keyboard instrument selection, contextual analytics,
deep-link reload, visible migration focus, exact cash-ladder projection/terms inspection, command
search to the account configuration blocker, and multiple prepayments with explicit notice dates.
Supporting evidence gaps return named 503 blockers. Migration staging, forecast capture, and
multi-page ladder continuation remain outside this browser evidence; their complete-operation
catalog acceptance stays pending. Screenshots and machine-readable evidence are written outside
the repository under `/tmp/meridian-fixed-income-access` by default.

Delivery order remains:

1. Complete existing access: contextual navigation, working deep links, usable inputs, API/client
   connections and browser acceptance for each supported operation.
2. Expose existing calculation depth: curves, pricing hierarchy, settlement and governed amortization.
3. Add saved scenario comparison with payment-level explanations and provider revision impact review,
   retaining original runs and reconciling payment differences to totals.
4. Add hypothetical reinvestment/ladder-roll plans, liability matching and funding gaps, and lot-aware
   sale/replacement previews against one retained market snapshot. Cash-alternative comparison remains
   a later planning capability with explicit compounding, liquidity and reinvestment assumptions.

The new planning features are deferred by this access increment; their appearance in this plan is
not implementation or verification evidence.

## Bond consistency increment

Declared analytics v2, context cash-flow receipts v2 and amortization v5 share the Contracts
`BondContractualEconomics` calculator. Level-coupon stubs use contractual reference-period units;
DayCount coupons use contextual day-count fractions. Settlement and exercise accrue through the
same calculator. Consumers retain their existing cash-rounding boundaries. Historical analytics v1,
context receipts v1 and lot amortization v1–v3 keep their original dispatch and arithmetic; the interim v4 declared model was removed before any instruction was produced.
Calendar-adjusted amortization still requires retained calendar inputs and remains refused when those
inputs cannot be supplied to the governed instruction.

Cash-ladder v2 separates declared issuer calls from the `holder-put` analytical scenario. Whole
fixed-price redemption uses outstanding principal, non-par redemption price, and supported coupon
entitlement. A scalar mandatory-put/pre-refund date does not establish a supported mandatory
redemption contract. Unsupported exercise types, elapsed notice deadlines without retained notice
proof, missing projection-bound terms and changed calendars block the affected scenario. Undeclared
records retain their legacy compatibility behavior; explicit historical v1 dispatch remains available.

Declared floating coupons retain reset date, observed/forecast status, original rate units, source,
index fraction, spread, contractual cap/floor, principal and accrual fraction. Observations must match
the supported accrual-start reset exactly. A scalar current index requires the same reset date and
source and cannot override an explicitly declared missing fixing schedule. Future resets require
retained forward inputs. Ladder shocks reuse these inputs, preserve observed coupons, and shift
future index rates before applying spread and cap/floor. Zero-rate forecasts can reprice; absent
contractual floors never imply a zero floor. Unsupported negative cash blocks the scenario.

Independent fixtures in `tests/fixtures/fixed-income/bond-consistency.json` state hand-verifiable
formulas and expected cash amounts. `BondConsistencyTests` compares analytics, cash-flow projection,
Asset Operations, settlement and amortization v5 and checks redemption and floating-scenario guards.
This increment performs no customer-data migration, live cutover, exercise, or journal posting.
