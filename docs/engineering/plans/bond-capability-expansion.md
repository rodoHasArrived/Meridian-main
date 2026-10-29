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

Declared amortization uses model v4. The existing PR #3328 default v2 and remaining-face /
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
