# Alpaca corporate-action coverage and corroboration

**Status:** active

**Owner:** Data Confidence and Validation

**Reviewed:** 2026-11-23

This guide records the endpoint boundary, fixture provenance and regression procedure for
[`AlpacaCorporateActionProvider`](../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaCorporateActionProvider.cs).
All returned observations remain `ReviewOnly`. Reference-data staging does not authorize
accounting acceptance, change positions, post journals, or resolve successor security identity.

## Selected endpoint and bounded coverage

The adapter uses `GET https://data.alpaca.markets/v1/corporate-actions`, with explicit event types,
`region=us`, `data_quality=complete`, and a process-date interval from `1990-01-01` through the
current UTC date. The start date is a request boundary, not a promise of available history.
The provider's `start` and `end` filters are inclusive **process dates**, not ex-dates or
announcement dates. Alpaca does not guarantee how soon an announced event enters its feed.
Every returned record must include a valid `process_date` within the requested interval. A
missing, malformed or out-of-window process date fails the fetch closed; it cannot be treated
as a complete result or replaced with an ex-date or effective date.

Alpaca's `complete` data-quality setting excludes unprocessed incomplete records, but processed
records can still have missing fields. A completed fetch therefore describes the selected query
and the adapter's validation outcome; it does not establish universal market coverage, complete
announcement history, economic correctness, or live-provider certification.

Sources checked on the review date:

- [Alpaca REST corporate-actions reference](https://docs.alpaca.markets/us/reference/corporateactions-1).
- [Official Alpaca OpenAPI](https://github.com/alpacahq/cli/blob/main/api/specs/market-data-api.json),
  inspected blob `6c67f0e7042407832ec32cd5a7de1e8279d698fc`.
- [Official Python model source](https://github.com/alpacahq/alpaca-py/blob/master/alpaca/data/models/corporate_actions.py),
  inspected blob `a78b59a1762d9e59c71728f81960fc62da278ea9`. This SDK source models an older
  13-family subset; the current REST reference and OpenAPI expose 16 families.

### Implemented families

All 16 families exposed by the selected REST endpoint are requested and mapped into review-only
source observations. A family being mapped does not declare its accounting treatment supported.

| Alpaca request type | Response group | Meridian staging classification | Preserved terms |
| --- | --- | --- | --- |
| `cash_dividend` | `cash_dividends` | `Dividend`; `ReturnOfCapital` for that explicit subtype | Per-share rate, dates and reported currency. The `interest` subtype is not mapped and reports an explicit failure. |
| `forward_split` | `forward_splits` | `StockSplit` | Original and new share rates. |
| `reverse_split` | `reverse_splits` | `ReverseStockSplit` | Original and new share rates and source identifiers. |
| `stock_dividend` | `stock_dividends` | `StockDividend` | Extra shares per held share; `rate=0.05` means a 5% distribution. |
| `spin_off` | `spin_offs` | `SpinOff` | Source and recipient identifiers and rates; recipient/source distribution ratio. |
| `cash_merger` | `cash_mergers` | `MergerAbsorption` | Acquiree, optional acquirer, effective date and cash consideration. |
| `stock_merger` | `stock_mergers` | `MergerAbsorption` | Acquiree/acquirer identifiers and rates, effective date and exchange ratio. |
| `stock_and_cash_merger` | `stock_and_cash_mergers` | `MergerAbsorption` | Both stock legs and the original cash rate; per-unit cash consideration only when `acquiree_rate=1`. |
| `unit_split` | `unit_splits` | `UnitSplit` | Original unit rate and separate main/alternate successor allocations in `successorLegs`; no single split factor is invented. |
| `redemption` | `redemptions` | `SecurityRedemption` | `redemptionRate` retains the reported rate without assuming a percent-of-par basis. |
| `name_change` | `name_changes` | `NameChange` | Old/new symbols, CUSIPs and ISINs; source identity remains bound to the old symbol. |
| `worthless_removal` | `worthless_removals` | `WorthlessRemoval` | Source identifiers and notice date; no zero price, loss amount or write-off is inferred. |
| `rights_distribution` | `rights_distributions` | `RightsDistribution` | Source/rights identifiers, rights per held unit and expiry; no rights exercise is inferred. |
| `partial_call` | `partial_calls` | `PartialCall` | Price, dividend rate, lottery classification and lottery/results dates when supplied; holder allocation remains unresolved. |
| `reorganization` | `reorganizations` | `Reorganization` | Original `cashRate` and each `stockMovements` allocation with its own source/new rates. |
| `capital_gains_distribution` | `capital_gains_distributions` | `CapitalGainsDistribution` | Separate `longTermRate` and `shortTermRate`, either or both; no ordinary dividend amount is synthesized. |

Merger, unit-split and reorganization effective dates use the existing staging date field with
`dateBasis=effective_date` in the terms payload. Stock-dividend, spin-off, rights-distribution
and capital-gains mappings carry `dateBasis=ex_date`. Redemption, name-change, worthless-removal
and partial-call schemas expose no ex-date or effective date: those observations retain the
process date in the staging date field with `dateBasis=process_date` and
`eventDateUnspecified=true`. This is an explicit source-date boundary, not a guessed economic
effective date. Their payable, lottery and publication dates remain separate where supplied.

The exact provider record also retains process date, CUSIPs/ISINs, optional dates and fields not
yet mapped to typed terms, including unknown fields within allocation legs. For mixed mergers,
`sourceCashRate` retains the reported `cash_rate`. If
`acquiree_rate` is not one, the payload records `cashRateBasis=unresolved` and omits
`cashConsiderationPerUnit`: the endpoint schema does not establish how the cash rate scales
against a non-unit acquiree rate, so the adapter does not invent that conversion.
Unknown currency remains unknown: Alpaca says an empty currency may mean USD, not applicable,
or unknown. The adapter never infers USD from its US region or a separate broker activity.

A symbol-filtered response may include an event because the queried symbol is its recipient
rather than its source. These related observations are counted explicitly by event family in
the coverage outcome, but are not staged as actions on the queried security. Spin-offs belong
to the source security; mergers belong to the acquiree. The recipient's presence alone cannot
establish a governed successor identity or transfer the action to that security.
Name changes bind their source observation to `old_symbol`. Querying only the new symbol may
therefore return a counted related observation without staging it; retrieving the source history
requires the old ticker or a governed historical alias. The adapter does not silently attach
the old-symbol event to an unresolved new-symbol security.

### Canonical and accounting boundaries

`UnitSplit`, `SecurityRedemption`, `WorthlessRemoval`, `PartialCall`, `Reorganization` and
`CapitalGainsDistribution` remain payload-only canonical terms, with no automatic price/position
adjustment or ledger posting. Scalar schema checks do not approve accounting. Multi-leg arrays
remain in the open payload envelope; the source adapter validates their structure and rates,
while mastered successor identities, basis allocation and holder-level treatment require review.
Capital-gain legs remain distinct even when both occur in one native event.

`SecurityRedemption` deliberately preserves the older `Redemption` alias, which continues to
mean `BondMaturityRedemption`. A generic provider rate must not reclassify a historical maturity
event or become a percent-of-par redemption price. Stock dividends retain extra-share semantics
and have no automatic split adjustment. Existing NameChange and RightsDistribution vocabulary
is reused without granting source observations accounting authority.

Broker activity codes alone do not expand announcement coverage: for example, a broad voluntary
offering activity can represent tenders or warrant exercises that are not listed REST families.
Unknown future response families and unmapped cash-dividend subtypes remain explicit failures.
Complete outcomes report only the implemented query, its observed source/related counts and its
validation result; all observations still require review.

## Identity, revisions, pagination and raw evidence

The native Alpaca corporate-action `id` is the provider event identity. An observed source
version combines that native identity with the response family, the exact canonical JSON
content of the full event object, and the mapping namespace `alpaca-rest-v2`. The version token
is `payload-sha256:<canonical-content-sha256>:<event-family>:alpaca-rest-v2`; it is retained
alongside the native ID. Canonicalization preserves numeric values without rounding them to a
CLR numeric type; property order, insignificant whitespace and equivalent numeric spellings
do not create a revision. The mapping namespace lets the evidence-retaining adapter append a
new revision to an older mapped observation without overwriting its retained terms.

The original event JSON and its separate raw UTF-8 SHA-256 are retained with the source
proposal. A formatting-only replay keeps the first retained original JSON. Canonical revision
identity and raw-byte integrity serve different purposes.

Re-observing the same native identity and revision stages idempotently. A changed payload for
the same identity is retained as another observed revision for review. Dates and rates are not
used to fabricate a replacement provider identity. Matching external evidence never changes
the adapter's `ReviewOnly` release status.

Conflicting content or response families for one native ID within the same paged fetch prevent
a complete result and withhold its observations. This check runs before source/recipient
classification, so a conflicting recipient-only record cannot conceal a changed revision of
an action on the queried source security.

REST responses group records by type and provide `next_page_token`. The adapter requests up to
1,000 records per page and follows bounded pagination. Repeated tokens, a page ceiling, partial
HTTP responses, failed continuation requests and malformed responses cannot certify a complete
history. A successful empty result only means no matching records were returned for the explicit
query. Callers that need coverage use `FetchWithOutcomeAsync`; collection-only results do not
distinguish an empty history from an unavailable fetch.

### REST mutation limitation

REST records do not expose an ordered amendment cursor or deletion tombstone. Re-querying can
discover changed retained payloads, but absence from a later response is not cancellation proof,
and unobserved intermediate versions cannot be reconstructed.

Alpaca separately exposes
[`GET /v1beta1/events/corporate-actions` SSE](https://docs.alpaca.markets/us/reference/subscribetocorporateactionseventssse)
with `insert`, `update`, `delete`, stable `ca.id`, mutation `event_id`, and replay cursors. That
stream is outside this REST adapter. Its decimal values are strings, unlike the REST numeric
fields. This adapter does not claim complete lifecycle or cancellation coverage.

## Corroboration fixture provenance

[`AlpacaCorporateActionCorroborationTests`](../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaCorporateActionCorroborationTests.cs)
embeds six public OpenAPI event examples. Its HTTP response envelopes and account activities
are **synthetic**, with synthetic account IDs and credentials. They are not live broker captures.
Five examples call the production provider and `AlpacaBrokerageGateway.GetActivitySnapshotAsync`
through HTTP stubs, then compare normalized quantities and cash with the facts below. The
rights-distribution example is an independent-source comparison described separately.

| Event | Independent source and confirmed terms | Disagreement deliberately retained |
| --- | --- | --- |
| MSBC stock dividend | [Mission Bancorp, May 2, 2023](https://s205.q4cdn.com/450322562/files/doc_news/2023/Stock-Dividend-Repurchase-Press-Release-2023_Q4.pdf): 5%; record May 22; payable June 5, 2023. | Alpaca's public example says payable May 5. |
| JUPW → SRM spin-off | [Nasdaq ECA2023-382](https://www.nasdaqtrader.com/TraderNews.aspx?id=ECA2023-382): one SRM per 19.35 JUPW; ex August 15; record July 7, 2023. | Alpaca's public example says record August 15. |
| GLOP cash merger | [GasLog Partners Form 20-F](https://www.sec.gov/Archives/edgar/data/1598655/000110465924031663/glop-20221231x20f.htm): $5.37 per common unit; transaction closed July 13, 2023. | Alpaca's example effective date is July 17. These are preserved as distinct source dates. |
| LSI → EXR stock merger | [Extra Space July 20, 2023 Form 8-K](https://ir.extraspace.com/static-files/825c268c-3638-4516-98c9-6806c0a3552b): 0.895 EXR per LSI, effective July 20. | The fixture's holding produces whole shares; fractional cash-in-lieu is not validated. |
| MLVF → FRBA mixed merger | [Nasdaq ECA2023-401](https://www.nasdaqtrader.com/TraderNews.aspx?id=ECA2023-401): $7.80 plus 0.7733 FRBA per MLVF; July 17 closing; July 18 suspension. | Alpaca's example effective date is July 18; it is not silently relabelled as closing. |

The IFN rights regression compares the provider example with the issuer's
[June 30, 2024 shareholder report](https://www.sec.gov/Archives/edgar/data/917100/000110465924097701/tm2419201d3_ncsrs.htm).
The report confirms one right distributed per held share, record April 18 and expiry May 14, 2024. The right
to purchase one new share for every three rights is a separate exercise term. The official
Alpaca example's `rate=1` therefore describes the rights allocation, not a one-third new-share
allocation. Its provider ex-date, payable date and rights symbol remain preserved without
claiming the issuer report verifies those fields. This is an independent-source comparison;
it makes no brokerage-activity claim and must not create mastered rights IDs or subscription
payments.

The tests distinguish a stock-dividend allocation from a replacement ratio, a spin-off recipient
from its parent, and merger share removal from successor/cash allocation. Non-trade activities
retain their signed quantity when no buy/sell side is supplied. This follows the removal/allocation
semantics shown in Alpaca's
[mandatory corporate-action examples](https://docs.alpaca.markets/us/docs/mandatory-corporate-actions).
Trade side normalization retains its existing behavior.

`DIVSTOCK`, `SPIN` and `MA` exercise the existing Meridian brokerage mapper; this is contract
corroboration, not certification that those exact synthetic records are emitted by every Alpaca
activity endpoint. The newer
[Activity SSE](https://docs.alpaca.markets/us/docs/activity-sse) uses type/subtype combinations
such as `DIV/SDIV` and `MA/SMA`, with its own activity and mutation identities. That feed is not
implemented by these tests.

[`AlpacaCorporateActionProviderTests.AdditionalFamilies`](../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaCorporateActionProviderTests.AdditionalFamilies.cs)
uses explicitly synthetic, official-schema-based records for the additional eight families.
It exercises multi-leg allocations, component preservation, source/recipient roles, optional
dates, malformed records and review-only outcomes. The all-family regression in
[`CorporateActionProviderEvidencePostgresTests`](../../tests/Meridian.Tests/SecurityMaster/CorporateActionProviderEvidencePostgresTests.cs)
uses synthetic records for all 16 families and asserts durable replay, amendment and restart
behavior, retained original payloads and zero canonical/accounting acceptance. These fixtures
do not establish independent issuer or actual broker corroboration for every family.

## Run and interpret validation

From the repository root:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter "FullyQualifiedName~AlpacaCorporateActionCorroborationTests|FullyQualifiedName~AlpacaCorporateActionProviderTests|FullyQualifiedName~AlpacaBrokerageGatewayTests" \
  /p:EnableWindowsTargeting=true /p:EnableIbApiSmoke=true /p:NodeReuse=false
```

Run the ingestion/staging and accounting-acceptance regression slice with the implementation
change as well:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter "FullyQualifiedName~CorporateAction|FullyQualifiedName~AlpacaBrokerageGatewayTests" \
  /p:EnableWindowsTargeting=true /p:EnableIbApiSmoke=true /p:NodeReuse=false
```

The PostgreSQL fixture uses the repository's test server setup and accepts
`MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` for a configured test database. Record which
database tests executed or were skipped. The test-run record or PR is the authority for commands
actually executed and their results; this procedure is not a passing test result.

**Live evidence status: `live-untested`.** No live credentials or private account history are
needed for this deterministic regression suite. A passing fixture run establishes only the stated
synthetic behavior. Live entitlement, complete history, upstream field population, mutation
stream behavior, successor resolution, fractional treatment, and accounting acceptance remain
outside that claim.
