# Bond Capability Expansion

**Status:** active
**Owner:** Instruments / Accounting and Ledger
**Reviewed:** 2026-11-13
**Direction:** three-stage browser/shared-service expansion, preserving retained model history and reviewed activation boundaries

## Outcome and boundaries

One explicit set of retained bond economics must explain reference data, expected payments,
valuation, settlement and accounting support. Security Master remains the identity and terms
authority. Instruments owns interpretation; Financial Operations owns governed posting candidates;
Ledger remains the immutable journal authority. The current increment extends the browser workstation
and shared APIs. Existing WPF analytics and cutover panels remain available; this increment changes
no WPF screens and adds no mobile lane. Continuous exercise/OAS, convertibles and structured-credit
accounting remain outside the new bond workflow model.

## Current three-stage increment

The [operator workflow](../../operators/bond-workflows.md) documents the concrete routes, input
formats, book-policy adoption and recovery paths. Implemented behavior is separate from test results,
live-provider verification and deployment activation; obtain those results from the change's actual
validation record rather than inferring them from this plan.

| Outcome | Implemented seams and behavior | Required evidence or limitations |
| --- | --- | --- |
| Import holdings and compare reliably | Versioned fixed-income workflow template; reviewed integration handoff to accepted account statements; bounded issuer/maturity/currency/coupon/call-protection/agency-rating screening; retained quote selection; position-sized clean/accrued/dirty cash and settlement comparison | Original/current face, principal factor and inflation ratio stay distinct. Cost-derived broker placeholders cannot establish observed marks. Historical reference/rating gaps remain visible. |
| Plan and collect income | Account/book-scoped planning, native-currency ladders, dated retained FX, exact reset observations, expected/actual allocations, gross/net/tax/fee explanations, revised receipt dates and durable collection casework | Opening cash and actual amounts need retained reviewed source bytes. Overdue/unexplained cash stays visible. A clearer source observation does not automatically resolve or sign off a case. |
| Manage exposure and explain results | Retained comparisons and risk runs; security/issuer/currency/maturity buckets; parallel/pillar rate and spread scenarios; sequential performance explanation; distinct US GAAP income and coupon-recognition workflows | Missing model, trade, mark, fixing or FX inputs produce partial coverage. Accounting income is never added again as market return. Preparation creates reviewable canonical input, not posted cash or journals. |

`bond-workflow-v1` uses the new signed cash/pricing model for fixed, zero, simple floating,
compounded-overnight and capital-indexed bonds, including explicit principal schedules and reconciled
factor history. Negative coupons remain signed obligations; no implicit zero floor is added. Yield
is withheld when a globally unique admissible root is not established. Rate DV01 reprojects future
floating coupons; CS01 changes discount spread. Indexed forecast-cash-flow yield is labelled
separately from real yield. Overnight contracts require retained fixing-calendar, lookback,
observation-weighting and lockout conventions; midperiod overnight principal changes remain refused.
Inflation requires an explicit base index, lag, interpolation and any contractual floor.

Curve definitions opt into `SignedRates` through immutable `rate_policy`; omitted policy retains
`LegacyNonNegative` admission and legacy serialized receipts. Signed discount factors remain strictly
positive and may exceed one or rise between pillars. The existing PostgreSQL curve authority remains
canonical. Typed FRED observations retain units/vintage dates separately from synthetic OHLC;
benchmark yields are not automatically discount factors. Newly expanded provider workflows remain
`live-untested` until their particular workflow has real retained evidence; synthetic fixtures establish Meridian
contract behavior only.

`bond-income-us-gaap-v1` is an explicit book-policy election for supported performing long face lots.
It separates clean cost, purchased accrued interest, eligible direct acquisition costs, expense/tax
items, premium/discount income and observed indexation. Floating policy explicitly chooses acquisition
index or prospective reset treatment. `bond-coupon-recognition-v1` separately prepares signed
receivable/payable accrual and retained cash clearing through the canonical accounting event spine,
period locks and atomic posting boundary. Preview/review fingerprints are revalidated before
preparation or posting. Deployment does not adopt a book policy or execute customer transactions.

Current analytical workflows pin `bond-workflow-v2`, separating active-period accrual from earlier
earned coupons awaiting payment. Retained v1 requests replay v1 even when their original request
omitted the model selector; risk scenarios and monitoring revalidation retain the selected model.
Booked income uses this separation only with the explicitly adopted and acquisition-captured
`bond-income-accrual-v2` subpolicy. Legacy elections, actual acquisition facts and historical
receipt/reversal arithmetic remain unchanged. Full-snapshot imports require complete source
populations, including quarantine and failed-page evidence; replay subsets cannot imply a flat or
complete account. The browser exposes an intraday UTC cutoff without advancing it on quote selection.

Monitoring v3 persists invariant numeric text and Gregorian date identities. Historical v1/v2 replay
must reproduce the complete serialized snapshot under the current settings or a bounded invariant,
`en-US`, `fr-FR`, `de-DE`, `th-TH`, `ar-SA` or `fa-IR` renderer. It ignores no financial or display
fields and never rewrites retained bytes. Unsupported custom output requires its original settings
before history can reopen or a new capture can be created. New obligation hashes are invariant;
localized aliases require the exact scoped payment/accrual hash in durable history.

Account planning and fresh monitoring capture block unproven detached-coupon holder rights and
`bond-workflow-v1` future zero-interest intervals without an entitlement marker. An explicit
`bond-workflow-v2` refresh resolves only supported economics. Missing unpaid claims remain visible
across overdue, inactive and absent holdings; validated gross allocations of observed payments can settle them.
Accepted face, payment dates and offsetting long/short quantities do not establish coupon ownership
or setoff.

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
- `bond-analytics-v3` (stacked branch) prices explicitly typed Floating bonds with one named-index
  leg: each coupon is the period's observed fixing (already-reset periods, from the retained reset
  schedule or a sourced current fixing) or the retained forward curve's projected fixing (future
  periods) plus the spread, capped and floored. The yield discounts those projected flows and is not a
  discount margin; duration, convexity and DV01 are rate measures of the projected flows, and the
  optional `SpreadDv01` reprices the flows with the spread raised one basis point at the same yield.
  Reset status follows the economic cutoff (`AsOf`): a reset dated after it is projected even when
  settlement falls later, while settlement governs entitlement, accrued interest and discount times.
  A missing historical fixing (`MissingFixing`), a missing forward curve (`ForwardCurveRequired`),
  duplicate retained fixings for one reset date (`DuplicateFixing`) or a floor above the cap
  (`InvalidRateBounds`) refuses the calculation; exercise scenarios are not calculated for floaters.
  v3 admits one `Forward:<index>` pin for the bond's own index so a stored curve version can be
  selected explicitly; other curve roles are refused, and v1/v2 still consume no curves. A stored
  curve is recorded by its retained version input; an unretained curve falls back to a
  source-generated content hash (`ForwardCurveContent`). v1/v2 output is unchanged; `SpreadDv01` and
  per-flow `FloatingCoupon` display evidence are omitted from their serialized results.
- `BondSettlementConsiderationDto` optionally names the book's functional currency and the
  settlement-date FX rate (stacked branch). Consideration stays in the bond's currency; the posting
  guard requires every component line to carry the bond currency as transaction currency, the book
  currency, that rate, and the component translated once and rounded to the book currency's minor
  units. No FX result arises at settlement; later remeasurement of the foreign cash balance belongs
  to the translation lane. Same-currency receipts omit both fields and keep their fingerprints.
- `BondExerciseNoticeDto` (stacked branch) is governed evidence that a call or put was actually noticed:
  source document hash, receipt date, noticed dates, price and amount, recorded by one actor and
  approved or rejected once by another (`POST /api/security-master/{id}/exercise-notices`,
  `.../exercise-notices/{noticeId}/decision`). The contractual cash-flow preview accepts a retained
  (non-hypothetical) scenario exercise only when `IExerciseNoticeVerifier` matches it to an approved
  notice for that security; loans and hosts without the store keep the existing blocker. Actual
  redemption posting is a separate treatment; this only establishes evidence.
- `IBondAnalyticsService` produces metrics, projected payments, capabilities and typed blockers.
- Cash-flow context overloads preserve existing call signatures as compatibility adapters.
- Reference array routes remain available; paged routes use bounded keyset cursors.
- Migration preview and staging routes require Security Master modification authority. Cutover and
  inventory review require administrative authority; actors are resolved from the authenticated
  request. Staging never approves or publishes its own draft.

## Workstation floating analytics

Browser: **Accounting → Security Master → select a Bond → Evidence → Bond analytics**.
WPF: **Security Master → open the selected bond's passport editor → Bond analytics**.
Both panels retain v2 as their default, offer explicit legacy v1, and expose v3 for named-index
floating bonds. Selection travels through the existing analytics GET's optional
`calculationVersion` query; the service enforces eligibility. Neither panel guesses eligibility
from the asset class or substitutes a different model after a refusal.

V3 displays projected coupon cash flows with each reset's observed/forecast status, named index,
fixing source, reset date, annual-fraction index/effective coupon rates and spread in basis points.
Observed describes the fixing, not payment receipt. Retained curve versions/hashes remain visible
in calculation inputs. The projected-flow yield discounts those cash flows and **is not discount
margin**. DV01 measures a one-basis-point yield change on the fixed projected flows; `SpreadDv01`
is the signed dirty-price change for a one-basis-point coupon-spread increase at unchanged yield.
Both sensitivities are price points per 100 face, not position-sized currency exposure.

The deterministic synthetic example used by service, HTTP and workstation tests is:

| Input | Synthetic value |
| --- | --- |
| Security | Typed Floating, USD, SOFR + 100 bp, bullet principal |
| Issue / maturity | 2026-01-15 / 2028-01-15 |
| Coupon / day count | Semiannual level coupons, 30/360, annual-fraction rates |
| Calculation / settlement | 2026-02-15 / 2026-02-15 |
| Observed reset | 2026-01-15: SOFR 0.03 (3%) |
| Retained synthetic forward curve | 2026-01-01 as-of; flat 0.02 (2%) through 2029-01-01 |
| Input projected-flow yield | 0.03 (3%), nominal periodic |

Select v3, enter those dates, choose annual yield, enter `0.03`, and calculate. Expected remaining
interest per 100 is `2`, `1.5`, `1.5`, `1.5`, with `100` principal on the last date; accrued interest
is `1/3` per 100, rounded at the service's decimal boundary. The first fixing is observed and the
three future resets are forecasts. Removing the historical fixing yields `MissingFixing`; removing
the curve yields `ForwardCurveRequired`. Each refusal exposes its code/reason and withholds metrics
and cash flows. Editing the model or any input clears prior results and invalidates delayed replies.
This synthetic example requires no live account or provider. Apply the repository's Level 2
financial-change verification and separate-agent review; the owner retains merge and activation
decisions under the enforced repository rules.

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
entitlement. Analytical bond workflows return `MandatoryRedemptionTermsUnsupported` for every
non-null `mandatoryPutDate` or `preRefundDate`, including nested `profileFields`, past dates and
malformed values; a scalar date does not establish a supported mandatory redemption contract.
Unsupported exercise types, elapsed notice deadlines without retained notice
proof, missing projection-bound terms and changed calendars block the affected scenario. Undeclared
records retain their legacy compatibility behavior; explicit historical v1 dispatch remains available.

Declared floating coupons retain reset date, observed/forecast status, original rate units, source,
index fraction, spread, contractual cap/floor, principal and accrual fraction. Observations must match
the supported accrual-start reset exactly. A scalar current index requires the same reset date and
source and cannot override an explicitly declared missing fixing schedule. Future resets require
retained forward inputs. Ladder shocks reuse these inputs, preserve observed coupons, and shift
future index rates before applying spread and cap/floor. Zero-rate forecasts can reprice; absent
contractual floors never imply a zero floor. Unsupported negative cash blocks the retained ladder
scenario. Signed coupons are first-class in the explicit new bond workflow and account planning
model; legacy engine dispatch is unchanged.

Independent fixtures in `tests/fixtures/fixed-income/bond-consistency.json` state hand-verifiable
formulas and expected cash amounts. `BondConsistencyTests` compares analytics, cash-flow projection,
Asset Operations, settlement and amortization v5 and checks redemption and floating-scenario guards.
This increment performs no customer-data migration, live cutover, exercise, or journal posting.
