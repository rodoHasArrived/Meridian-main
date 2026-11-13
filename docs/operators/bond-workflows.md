---
title: Bond workflows
status: active
owner: core-team
reviewed: 2026-11-13
audience: operators
---

# Bond workflows

Import observed holdings, compare quotations and settlement cash, plan account income, investigate
collections and explain exposure, market return and accounting income. Browser screens consume shared
services. Synthetic fixtures demonstrate Meridian contracts; live-provider verification and actual
account activation remain separate. Analysis does not establish execution or posting authority.

## Scope and entry points

In **Accounting → Security Master**, select a bond and open its evidence/details for retained bond
calculations, screening/comparison and accounting-income panels. The **Portfolio cash ladder**
contains **Account bond income planning**, reviewed holding imports, risk and performance tools.
Legacy analytics panels keep their explicit calculation versions.

Account routes use `/api/ledger/books/{ledgerBookId}/accounts/{accountId}`, with canonical GUIDs and an
account-owned book in the authenticated tenant/company. The server binds scope and actors; a body
cannot move a request into another account or tenant. Follow [bounded account oversight](account-oversight.md)
for ownership, permissions and forecast provisioning. A calculation may retain a run snapshot without
posting a journal or executing a payment.

| Task | Method and suffix under the account prefix |
| --- | --- |
| Accept reviewed holdings | `POST /bond-holdings/import` |
| Analyze a bond with account pricing | `POST /bond-workflows/{securityId}` |
| Assemble account income | `POST /bond-income/preview` |
| Read/retain collection evidence | `GET /bond-monitoring`, `POST /bond-monitoring` |
| Calculate/read retained risk | `POST /bond-risk`, `GET /bond-risk/{runId}` |
| Save/read a comparison | `POST /bond-comparisons`, `GET /bond-comparisons/{runId}` |
| Explain period performance | `POST /bond-performance` |
| Import reviewed performance transactions | `POST /bond-performance/transactions/import` |
| Preview/revalidate lot income | `POST /bond-accounting/preview`, `POST /bond-accounting/prepare` |
| Preview/prepare coupon recognition | `POST /bond-accounting/coupon-preview`, `POST /bond-accounting/coupon-prepare` |

Reference routes under `/api/reference-data/bonds` include `POST /screen`,
`POST /{securityId}/workflow`, `POST /comparisons/preview`, `GET /connection-readiness` and
`GET`/`POST /market-observations`. `POST /identifier-discovery` preserves competing bond FIGIs;
`POST /fred-observations/import` performs the reviewed typed intake below, and
`GET /provider-responses/{contentSha256}` reopens the exact retained provider JSON within its original
tenant/company scope. Use account routes for account-specific pricing or retained results.

## Import holdings and terms

1. Use the versioned fixed-income workflow integration template. Retain original payloads, apply
   mappings, resolve account/Security Master identities and complete reconciliation-staging handoff
   review. Mapping alone does not publish holdings.
2. Declare currency, coupon units, day count, calendars, payment/exercise terms and floating/inflation
   conventions. The workflow template removes the older currency fallback. Reference observations
   still use normal Security Master governance.
3. Accept the complete reviewed population using connection/run IDs, account, batch, as-of date and
   revision. Corrections name the superseded batch. A partial run cannot become a full snapshot.
4. Check normalized clean/dirty values and retained source identities. Exact retries preserve the
   original result; conflicting identity/content, missing history and competing revisions block.
   An accepted empty full snapshot explicitly means a flat account. Submit flat statements through
   canonical statement intake: the reviewed-row bridge requires a nonempty reviewed population and
   never infers a flat account from missing staged rows.

Full-snapshot acceptance also checks the completed source run, its staging and quarantine counts,
and unresolved account attribution. Reviewing, ignoring or requesting replay of a rejected row
does not supply its missing holding economics. A replay containing only former rejects cannot
replace the complete statement: ingest and review the complete corrected population in a new run.
Only positively identified, authenticated other-account rows can be excluded from this account's
completeness check. Transport or pagination failures remain blockers.

The workflow-v2 catalog now offers manifest revision 3 with optional explicit family terms for
reviewed source evidence. Exact revision-2 mappings and replay remain available. Staging these
fields does not publish Security Master terms: canonical reference changes retain their own
governed intake and review, and missing canonical terms remain visible coverage gaps.

Holdings declare original/current face, principal factor, inflation ratio, quote basis, clean/dirty
quotation, accrued interest and nominal/indexed invoice basis. Current face equals original face
times principal factor; inflation is separate. Prices can be percent of current face, percent of
original face or currency per current face. Fees remain separate. Reported clean/dirty values must
reconcile at currency cash precision. Original provider values and mapped JSON remain evidence.

IB average-cost placeholders are labelled `CostDerived` and cannot establish observed marks.
Broker snapshots preserve supplied accrued interest. A broad provider `Bond` flag does not prove
reference, valuation, fixing, entitlement or execution readiness.

## Compare quotes and supply market inputs

Screen retained bonds by issuer substring, maturity range, currency, coupon kind, call protection and
agency-specific rating labels. Reference knowledge-time limitations and missing evidence remain
visible; an absent rating is not an assumed grade. Compare each bond's own quote at the same original
face and settlement, keeping incompatible currencies separate.

Call protection considers future European and Bermudan exercise dates relative to the screening
date. An expired discrete exercise does not remain an eligible call. Continuous rights, unmatched
summary dates and unsupported or calendar-dependent grids keep conservative eligibility or an
explicit coverage gap. Eligibility never establishes that an issuer has issued a call notice.

Retain quote category (evaluated valuation, reported trade, indicative or executable quote), clean/dirty
basis, unit, source, observation/publication time, original settlement, optional size/expiry and
provider yield/convention. Imported yield remains distinct from Meridian's calculated yield.
Set **Economic date and time (UTC)** to the intended intraday cutoff. Selecting a retained quote
keeps both the economic cutoff and the independent knowledge cutoff unchanged; a quote observed
after the economic cutoff remains unavailable. Comparison and original settlement remain dates.
New calculations use `bond-workflow-v2`. Its contractual accrued interest covers the active accrual
period; `deferredCouponInterest` separately retains fully earned coupons awaiting their contractual
payment dates. Invoice accrued interest includes the transferred entitlement and any ex-coupon
adjustment. Retained `bond-workflow-v1` results replay their original calculation version, including
older requests that did not contain an explicit model pin.
An existing price-selection receipt remains the pricing authority: pair it only with matching typed
source, observation time, value and unit. A matching number alone is insufficient. New selections carrying quotation evidence also require the exact clean/dirty convention, per-100 basis, settlement date and `quote.settlementBasis`; import matching typed metadata before replay. Expired liquidity
is flagged. Settlement normalization needs retained curve coverage and a uniquely calibrated spread;
an old date is not given an invented carry factor. Confirmation cash is an evidenced comparison input.

Typed market observations distinguish fixings, inflation levels, principal factors, ratings and
quotes. Keep provider/series identity, economic date, revision, source hash/reference and any
publication/vintage date; the server assigns recording time. Exact-date fixing queries do not
substitute yesterday's value. Workflow requests bind the input role explicitly: an inflation level
cannot become an annual fixing because its series name matches, or vice versa. Existing custom
legacy observation sources retain their original source contract. Future index/factor values belong to forecast curves.

FRED's typed method retains declared series units, vintage dates and missing observations separately
from synthetic OHLC compatibility. A vintage day does not prove an exact publication-time instant.
Benchmark yields never automatically become discount factors. OpenFIGI ISIN/CUSIP discovery removes
the Equity default for durable identifiers and preserves competing bond FIGIs for resolution;
equity ticker discovery retains its existing scope.

For operator-triggered FRED intake, first retain this binding JSON in the authenticated tenant/company's
Evidence Vault with subject kind `provider-trust` and subject ID `fred`. Independently accept its exact
content hash. The binding key names an immutable mapping; changed mapping bytes cannot overwrite an
already retained observation revision.

```json
{
  "schemaVersion": 1,
  "kind": "bondFredSeriesBinding",
  "bindingKey": "official-sofr",
  "seriesId": "SOFR",
  "indexName": "SOFR",
  "observationKind": "Fixing",
  "unit": "Percent",
  "sourceName": "New York Fed via FRED",
  "currency": "USD"
}
```

The intake request supplies only `evidenceUri`, `documentId`, `from`, `through` and `vintageDate`.
It uses the configured FRED adapter key, retains typed observations with the binding's evidence hash,
archives the exact returned JSON text and UTF-8 SHA-256, reports missing dates and rechecks review after
fetching. Each observation names its first retained response hash; identical rows fetched in another
window preserve that original evidence while the additional response is also archived. Scoped source
reads verify content; repeated intake refuses a missing or changed original response. Inflation bindings use `InflationIndex`
and `IndexLevel`; quote/curve mappings are refused. Discovery accepts `identifier`, `identifierType`
(`ISIN` or `CUSIP`) and optional supported bond `marketSector`; it returns candidates without changing
Security Master. These operator methods retain `LiveUntested` until separately evidenced live runs.

Use existing Security Master curve definitions and immutable published versions. A definition's
`ratePolicy` is write-once: omitted/`LegacyNonNegative` preserves nonnegative-rate admission;
`SignedRates` explicitly permits signed forwards and strictly positive discount factors that may
exceed one or rise between pillars. Define a new curve key to change policy. Pin calculation roles
as `Discount`, `Forward:<index>` or `Inflation:<index>`. Retain version/hash and economic/recorded dates.
Automatic selection admits definitions known at the knowledge cutoff and retains the resolved
curve/version pins for replay; later catalog additions do not change those selections.
Receipts that used a definition created after their knowledge cutoff require source review and
a new calculation; replay refuses that look-ahead evidence.
Negative coupons remain obligations unless a contractual floor applies; no missing floor becomes zero.

Connection readiness separates implementation, exact connection configuration, input-specific
entitlement and evidence. `SyntheticVerified` proves synthetic contracts. `LiveUntested` remains
honest after a successful login check; `Unsupported` means no typed workflow adapter supplies the
input. Configure applicable identifiers/terms/holdings/pricing/fixing/inflation/curve/factor/rating
entitlements without treating one category as permission for another.

## Plan and collect account income

Select planning dates, economic/knowledge cutoffs and retained opening cash; a twelve-month window
answers annual income questions. Plans use accepted scoped holdings and actual face/factors, never
Security Master inventory as a substitute for owned quantities. Native ladders stay separate.
Base-currency totals need retained dated directed FX; there is no 1:1 or inverse-pair fallback.
Missing or excluded inputs remain visible.
Principal installments and residual balloons sharing one obligation identity are combined before
native-currency cash rounding, so planning and monitoring capture retain one payable amount.

Simple floaters need exact observed resets for elapsed periods and eligible forward inputs for future
resets. Compounded overnight coupons also require fixing calendar, lookback, observation method,
weighting and lockout; midperiod overnight principal changes remain unsupported. Inflation requires
base level, lag, interpolation and any contractual coupon/redemption floors. Factor history must
reconcile with dated principal cash. Signed coupon obligations are first-class throughout the new model.

Monitoring must use the exact server-retained plan's obligations. Retain a stable run ID, actual
payments, allocations, deductions, receipt revisions and watch observations. Account-supplied cash,
tax/fee, FX and notice observations use the supported typed account source JSON with exact bytes and current independent
accepted Evidence Vault review. A URI or hash alone does not establish financial authority.

Planning also joins retained tenant rating and principal-factor observations to the accepted held
securities at the pinned economic and knowledge cutoffs. Monitoring captures recheck the exact market
rows and holding factors. The watch keeps each agency, provider and revision, identifies conflicting
sources and links affected obligations. Ratings remain watch information. Factor conflicts and
superseded calculation dependencies block decision-ready cash forecasts until source review and a
refreshed calculation resolve them. Earlier snapshots remain available for comparison and replay.

Reset monitoring shows the current signed whole-coupon estimate and, when a prior retained estimate
exists, its total change for that security and payment date. An unknown prior estimate stays unknown.
Daily overnight resets show their index rates separately; one coupon-revision item reports the total
change across all refreshed inputs. Contractual caps, floors, held face and payable currency rounding
come from the shared bond model.

Expected/actual status exposes overdue, partial, short, overpaid and unexplained cash. Revised receipt
dates retain original obligations and remain valid history after their dates pass. Unpaid residuals
age from the evidenced revised date. Overdue residuals do not become spendable simply because they were
forecast. Positive coupons create receivables; negative coupons payables and signed outflows.
New monitoring snapshots use `bond-monitoring-v2`; native forecast requests select
`account-cash-forecast-v3`. Historical monitoring v1 and native forecast v2 snapshots replay their
original rules and identities.
Captured exceptions enter existing reconciliation casework. Later source-clearing evidence does not
automatically resolve, sign off or reopen a governed case; use the existing casework commands.

## Explain risk and performance

Risk uses actual face/factors with security, issuer, currency and maturity coverage. Parallel rates,
dated curve pillars and spread widening are analytical scenarios. Rate DV01 reprojects future
floating income; CS01 changes discount spread. Retain run IDs for risk/comparison retrieval.

Performance starts from accepted closing marks and signed actual cash over `(periodStart, periodEnd]`.
Buy/sell settlement cash is distinct from coupon/principal collections. Sequential effects are
carry/roll, rates, spread, inflation, collection/cost differences, FX and an explicit residual.
Attribution depends on this order. Unavailable components remain unavailable; unexplained change
is not assigned a convenient cause. Accounting amortization/indexation is reported separately.

Complete reviewed trade/cash populations are required before certifying native economic P&L.
Flat boundary statements do not prove zero roundtrip trades. Net face changes must reconcile to
reviewed trade/redemption quantities; unexplained gifts/transfers cannot become investment profit.
Model attribution can be unavailable while native P&L is known. Missing dated FX blocks base
translation while preserving available native results. Current cash/FX source reviews are rechecked.

### Reviewed transaction source JSON

Retain this schema as an account Evidence Vault document. Replace synthetic identities below with
actual canonical account/book/bond IDs and accepted boundary batches. Coverage binds the exact
interval and `holdingBasis: "Settled"`; another position basis is not silently treated as settled.
The source system/reference must agree with vault document metadata.

```json
{
  "schemaVersion": 1,
  "kind": "bondPerformanceTransactions",
  "accountId": "09f59b67-e8ce-46ad-b5af-a90495720d31",
  "bookId": "654f5a09-acd5-4881-896f-600649b2a16c",
  "periodStart": "2026-01-01",
  "periodEnd": "2026-07-01",
  "revision": 1,
  "sourceDomain": "SyntheticCustodian",
  "sourceEntityId": "statement-2026-H1",
  "completeTradePopulation": true,
  "completeCashPopulation": true,
  "holdingBasis": "Settled",
  "openingHoldingsBatchId": "62a5c5f8-16ef-48cb-8558-dc0ec519db09",
  "closingHoldingsBatchId": "51e266cf-9ba5-4bd5-88b0-932c412c2222",
  "tradeCash": [
    {
      "transactionId": "buy-1",
      "securityId": "a578240a-f966-41ca-a4ce-d72db10b7f5a",
      "cashDate": "2026-02-01",
      "amount": -1000,
      "currency": "USD",
      "kind": "Buy",
      "originalFaceDelta": 1000
    },
    {
      "transactionId": "sell-1",
      "securityId": "a578240a-f966-41ca-a4ce-d72db10b7f5a",
      "cashDate": "2026-03-01",
      "amount": 1020,
      "currency": "USD",
      "kind": "Sell",
      "originalFaceDelta": -1000
    }
  ],
  "paymentVersionIds": []
}
```

Transaction IDs are unique; `amount` and `originalFaceDelta` are required, never defaulted from absence.
Ordinary buys have negative settlement cash and positive face change; sales reverse the signs.
`PrincipalRedemption` removes face without duplicating collection cash: use `amount: 0` and an exact
`paymentVersionId` linked to retained principal collection. List all active interval payments in
`paymentVersionIds`; an empty list proves zero receipts only with a reviewed complete population.

Independently accept the exact hash through existing document review, then import only `evidenceUri`
and `documentId` through the transactions-import command. Caller amounts/completeness flags in that
command cannot replace source bytes. Corrections advance the source revision without moving its
window. Current review/hash, accepted batches, canonical bond identity and active payment IDs are
rechecked. Conflicting sources, revoked evidence and changed batches require correction/review.

## Adopt US GAAP income and coupon policies

Adopt these separately on an owned US GAAP amortized-cost book. Example governed `RulesJson` sections:

```json
{
  "bondIncome": {
    "modelVersion": "bond-income-us-gaap-v1",
    "accrualPolicyVersion": "bond-income-accrual-v2",
    "floaterPolicy": "AcquisitionIndex",
    "capitalizeEligibleDirectCosts": true,
    "recognizeObservedIndexation": true,
    "amortizeQualifyingCallablePremium": true
  },
  "bondCouponRecognition": {
    "modelVersion": "bond-coupon-recognition-v1",
    "accounts": {
      "receivable": "Assets/InterestReceivable",
      "payable": "Liabilities/InterestPayable",
      "income": "Revenue/BondInterest",
      "cash": "Assets/Cash",
      "tax": "Expenses/WithholdingTax",
      "fees": "Expenses/CustodianFees"
    }
  }
}
```

Paths are illustrative: resolve distinct actual ledger accounts. Receivable/cash require assets,
payable liability, income revenue, fees expense and tax an eligible asset/expense account.

Retain reviewed clean acquisition consideration, purchased accrued interest, acquisition index/FX and
each fee's source/treatment. Eligible direct acquisition costs enter clean basis exactly once only
when capitalization is elected. Expense and tax items do not become amortizable cost. Premium
amortization, discount accretion and observed indexation remain distinct; principal is not disposal
income. Floating policy explicitly chooses `AcquisitionIndex` or `ProspectiveReset`. Qualifying
callable-premium treatment uses supported preset calls for amortization, not actual exercise/cash.

The explicit `bond-income-accrual-v2` subpolicy is captured in the retained acquisition facts as
well as the book policy. It separates active-period accrual from earlier fully earned, unpaid
coupons, so a payment lag cannot subtract the same coupon twice from clean carrying value.
Receivables and payables retain their separate signed obligations. Native payment rounding is
recognized at the obligation boundary, with any rounding adjustment retained separately from
active-period income. Purchased opening interest remains an evidenced acquisition balance.
Omitting the subpolicy preserves legacy arithmetic; new legacy-policy preparation in an unsafe
overlapping payment-lag window requires policy review. Changing a book setting does not rewrite
an existing lot's acquisition election, prior receipts or reversal authority.
An acquisition that also transfers earlier fully earned, unpaid coupons requires reviewed opening
entitlement allocation; a net purchased-interest total cannot establish separate gross rights.
Advance payment before an accrual period ends remains a booked-income coverage gap. Available
market valuation and cash forecasts remain usable. A v2 frozen income schedule can reach the
declared final payment date, including its retained calendar adjustment; later dates are not guessed.

Completed coupons round once at the security/account obligation's native currency boundary, then
allocate the rounded amount across authoritative entitled lots by residual fractions and stable lot
identity. Saved allocations retain lot versions, dated entitlement inputs and acquisition settlement;
individual lots do not independently round the same holder obligation. Gross, net, tax and fee shares
cannot exceed the retained full allocation, including when several lots share one bank receipt.

Raw modeled income retains twelve-decimal calculation economics and the retained projection inputs.
Noncash carrying and coupon-accrual targets use cumulative `bond-booked-target-10dp-v1` rounding
(ToEven, ten decimals) to match journal storage. Instructions retain raw, booked and residual amounts.
Native payment cash uses the currency minor units, including JPY zero and KWD three decimals.
Opening facts and adopted acquisition legs must already fit journal precision; otherwise a reviewed
opening correction is required. This policy never creates acquisition rounding income.

The income model supports performing long face lots and monthly, quarterly, semiannual or annual
schedules, plus at-maturity zero-coupon accretion using annual effective yield. Unsupported historical entitlement/partial-relief cases refuse rather than applying
today's quantities to past periods. Signed earned balances become receivables or payables. Cash
clearing verifies signed gross/net amounts, deductions and prior consumption, and cannot clear more
than outstanding earned/purchased interest.

First recognition also proves the canonical acquisition journal and its exact account/security/lot
history. Purchased seller accrued interest must already be booked separately. Prior manual coupon
entries, unscoped history or ambiguous legacy opening balances block recognition or clearing;
missing history is not a zero recognized balance. Each later posting also verifies the retained
native receipt chain and current book history. Exact canonical noncash amortization remains separate.
Resolve ambiguous coupon accounting through retained reconciliation before continuing.

Before final scheduled principal closes a lot, reconcile its final earned coupon target. Principal
may settle while coupon cash remains unpaid: the closing instruction pins the actual earned receipt,
journal and frozen native entitlement. Fully earned, observed coupons paid later retain their real
payment dates and holder allocations at principal payoff; unfixed or unearned rights block closure.
Later coupon cash and typed reversals use that frozen authority and the actual closed-lot mutation,
without reopening principal or recognizing new income. Exactly zero earned income with a wholly
purchased coupon uses the actual acquisition journal and separately booked purchased receivable or
payable as its anchor, without an artificial zero journal. This bounded separation supports one
fully purchased coupon and nonaccreting par principal; nonpar, mixed or unmatched tails refuse.
Single-period signed accounting yields retain an affine recurrence certificate and positive
accumulation/residual checks; this accounting allocation certificate does not alter contractual
cash dates or certify a market cash IRR. Existing holders accrue their retained contractual coupon
through an ex-coupon window; purchaser invoice accrued interest remains a separate settlement input.

Governed carrying-income preparation currently requires the acquisition and functional currencies
to match. Coupon recognition also requires acquisition/projection currency to match the book's base
currency. A historical acquisition FX rate does not establish today's functional income or coupon
translation. Foreign native previews and cash planning remain available within their coverage; booked
FX recognition needs a separately adopted dated translation policy.

Preview with account/book/lot/period IDs and an elapsed recognition date; review the fingerprint and
prepare that exact fingerprint. Coupon preparation retains a canonical event-spine projection.
Ordinary review, open-period controls, evidence checks and atomic journal/lot posting remain required.
Changed recognized economics use explicit receipt reversal/rebook. Supply the exact posted
`reversesReceiptId`, a `correctionRationale` and no new cash allocations in an authorized open period.
Unwind later recognition on the affected lot first. The retained inverse releases only that receipt's
net/gross/tax/fee shares and preserves other lots' claims; prepare a fresh reviewed rebook afterward.
Generic journal reversal cannot bypass typed coupon state restoration. Commands neither self-approve nor execute payments.
The performance accounting bridge covers governed selected-lot calculations and checks closing-face
coverage; it never adds amortization/indexation again to market return.

## Verification, recovery and activation

Exercise synthetic missing-fixing, signed-coupon, partial-payment, deduction, revised-source,
missing-FX and conflicting-statement cases first. Consult actual test/PR records for results; this
procedure does not assert that local, PostgreSQL, browser or hosted checks passed. Live-provider
behavior retains its separate evidence label.

Back up the configured data root and database before adopting migrations or policy. Retain accepted
custodian revisions, market observations, curve versions/policy, saved runs, monitoring/casework,
provider responses, performance sources and coupon receipts. Missing/corrupt evidence blocks; it is not zero holdings or
income. Correct through retained revision/review workflows rather than deleting receipts/journals.
Deployment performs no customer cutover, posting, exercise, funds transfer or live execution. Legacy
analytics, ladder, amortization and journal dispatch/payloads remain preserved. Follow the repository's
financial-change verification and the owner's merge/activation decisions; synthetic development
evidence is not production certification.

Legacy floating-v3 observed-fixing receipt pins remain replayable through floating-v3 analytics.
The new workflow explicitly refuses those receipt pins and uses retained typed bond-market inputs;
no selected receipt silently becomes a latest observation.
