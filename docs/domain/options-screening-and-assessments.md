# Options screening and imported broker assessments

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-12

The browser Market Data desk exposes **Options screening** at
`/data/quotes?view=options`. It plots current call and put contracts by calendar
days to expiry and signed delta, alongside an accessible contract table and
selected-contract evidence. A screen pass means only the selected neutral
criteria match. It does not assert account permission, available collateral,
broker acceptance or likely execution.

## Snapshot coverage

`POST /api/options/screen` applies DTE, signed-delta, right, open-interest,
volume, premium-basis and spread/mid criteria on the server. It returns rejected
contracts with every applicable rejection reason. Missing delta remains in the
table. Quote identity and source timestamps stay inspectable. A child quote
whose underlying or expiration disagrees with the requested chain cannot pass.

Reads are bounded to twelve expirations and an explicit strike range of 0–100
strikes either side of ATM. Loaded, failed and omitted expirations remain
separate. Provider coverage may be smaller than the requested range. These
provider snapshots are not historical point-in-time chain data. The browser
never substitutes development fixtures for these authoritative requests.

## Basket and account meaning

The proposed basket accepts 1–50 call, put and equity legs, including arbitrary
combinations and uncovered selling. Each leg retains its exact symbol, side,
opening/closing intent, ratio and currency. Option legs also retain underlying,
right, expiry, strike, multiplier, exchange and an explicit deliverable.
Adjusted deliverables are not inferred from the multiplier. Basket quantity,
market/limit basis, debit/credit/even price effect and currency form part of the
identity. The browser submits no order and calculates no general option margin
or generic payoff.

An imported broker assessment applies only to its exact owned account,
provider/account identity, complete basket and stated assessment time. Changes
to any basket term remove the historical match. Account permission and
restriction statements remain separate dated observations. Even an imported
`Approved` outcome is displayed **as of** its broker assessment time; it never
establishes present acceptance or reserves stock/cash.

## Evidence-backed import

Account routes are rooted at
`/api/options/assessments/accounts/{accountId}`. Reading history or source
documents requires `ViewOptionAssessments`; previewing and accepting imports
requires both `ViewOptionAssessments` and `ManageOptionAssessments`, current
account-scoped authority, an actual account from the host's tenant-aware query
service, and authenticated tenant/company context. Existing retained brokerage
links cannot be changed or contradicted by imported account text.

The form and canonical JSON share schema version `1` and accept an original
broker source document of at most 8 MiB. No provider-specific PDF extraction or
live broker what-if request occurs. Reported amounts are exact decimal strings.
Margin figures and cash impact require their own explicit reported currency;
neither is silently assigned the basket's premium currency or converted.
Absent amounts and permission statements remain absent.

CSV optionally replaces the entire basket's legs. Its exact header is:

```csv
legId,instrumentType,symbol,side,positionIntent,ratio,currency,underlyingSymbol,right,expiration,strike,multiplier,exchange,deliverable
```

`instrumentType` is `Option` or `Equity`; side is `Buy` or `Sell`; position intent
is `Open` or `Close`. Equity rows leave option-specific columns empty. All rows
are validated together. Quoted commas, escaped quotes and multiline fields are
supported; malformed rows are not discarded. `outcome` is the explicitly
transcribed `Approved`, `Conditional`, `Rejected` or `NotReported` broker claim,
with its original status text retained alongside it.

`POST .../preview` returns normalized terms, source digest and a reviewed
fingerprint without retaining anything. `POST .../accept` recomputes those
inputs and requires the reviewed fingerprint and a stable UUID idempotency key.
The same key is bound to its original actor and input; retrying returns the
same durable result. An uncertain browser response retains that exact command
for retry while the view remains open. After leaving the view, load account
history to inspect the retained outcome before making another import.

The Evidence Vault retains both original source bytes and the normalized JSON.
The import receipt is partitioned by authenticated tenant/company and account.
Verified source and canonical digests back history reads. Corruption or missing
evidence fails explicitly instead of presenting an empty history. Corrections
append a new record linked to the previous assessment; the original remains
immutable and a second correction fork is refused. Source retention proves
which bytes were imported, not the authenticity of the broker's signature.

## Validation

Focused suites are `OptionScreeningServiceTests`,
`OptionAssessmentImportServiceTests`, `options-screen.test.tsx`,
`options-screen.assessments.test.tsx`, `options-screen.view-model.test.ts`, and
`option-visualization.api.test.ts`. Deterministic synthetic scenarios cover
scope mismatch, missing Greeks, partial chain coverage, exact/tiny decimals,
CSV rejection, immutable correction history, restart/retry behavior, tenant and
account isolation, source verification, stale responses and selection behavior.
Synthetic browser intercepts verify rendering and interaction only; they are
not live broker or API compatibility evidence.
