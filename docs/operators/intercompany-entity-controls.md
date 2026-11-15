# Intercompany and entity controls

**Status:** active procedure

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-14

Use Accounting's entity/intercompany and consolidation views to inspect the shared entity timeline,
agreements, books, source evidence, counterpart proposals and consolidation output. Desktop uses
the same host APIs. Open a retained source before approving a material journal; a proposal or
preview is not a posted balance.

1. Establish the tenant-owned organization and entity/book mappings. Retain evidenced lifecycle
   windows and successor identities. Uncovered history remains unknown.
2. Configure chart roles and append bindings for stable chart nodes. Keep old ledger names as
   historical aliases when renaming. Use one unambiguous output account per elimination role.
3. Create and review legal terms and exact retained agreement evidence in the
   canonical agreement registry. In Accounting profiles, select that exact approved legal version
   and hash, then configure the party books, accounting template and stable account mappings.
   Legal terms are read-only there. Approve the exact profile revision before governed posting.
   Configuration owner approval cannot approve or amend the legal agreement.
   Guarantees remain record-only; loan counterpart templates support principal.
4. For existing books, inspect `LegacyWarning` decisions. Use legacy-attribution candidates to
   obtain exact source digests and retain approved attribution evidence. Do not edit old journals
   to populate new dimensions. Prepare controls preflight and activate only its current version
   and digest after all required evidence is resolved. New books begin enforced.
5. Source posting retains counterpart work durably. Review delivery failures, retry the same
   proposal, or explicitly link a supported existing counterpart with evidence. The destination
   reviewer uses the ordinary journal workbench. Compare transaction identities; equal amount and
   period alone do not establish a duplicate. Split source journals that mix several event groups
   with unattributed cash or other contra legs; proposals never allocate shared legs by guesswork.
   An automatic reversal uses the original reviewed economics and rate and requires an isolated,
   posted two-leg event with retained party, book and chart identity. A broader manually linked
   journal or an existing target correction requires explicit manual correction/linking; a second
   automatic inverse is refused.
6. For consolidation, select the organization, root, dedicated elimination book, period and date.
   The book's exact policy version selects v1/v2/v3/v4 behavior. Configure approved rate-set and FX
   policy revisions before v3/v4. Retain historical/opening facts and any declared average period;
   a missing rate blocks eliminated output rather than producing a misleading mismatch.
7. For an owned-member change, create a distinctly named empty dedicated reporting book in the same
   fund/root, configure its role/FX authority and period, and prepare the reporting transition.
   Inspect the member diff, old history, new sources and fingerprint. An independent reviewer reopens
   the retained preview ID and approves that exact fingerprint. Use the old book before cutover and
   the new book at/after it. The new target never imports old overlays; former-member external
   exposures remain gross and non-eliminable. A changed operand requires a new preview.
8. Inspect gross, proposed, posted, actual and preview columns, directed intercompany breaks,
   translation reserve, economic translation differences and rounding separately. Create drafts,
   obtain normal independent review, and post through the existing workbench. A changed source or
   authority requires a rerun. Corrections are new linked adjustments; process elimination dates
   in order.

For structured legal terms, retain a new native proposal rather than editing an approved version
or an accounting profile. Declare annual decimal rates, basis-point spreads, schedules, calendar
and allocation references explicitly. Use the date preview to inspect boundaries and payment dates;
it calculates no interest, fees or ledger due dates. Legacy calendar-day settlement terms are not
converted to business days automatically. A missing calendar or an unstructured agreement remains
unavailable for date preview.

## Accruing loan interest

Interest on a fixed-rate, simple-interest intercompany loan can be calculated from its approved
structured terms. It needs two approved accounting profiles of the same legal agreement: the `Loan`
profile that carries posted principal, and a `LoanInterest` profile that maps interest receivable and
payable and puts interest income on one party against interest expense on the other.

1. Preview with `POST /api/ledger/intercompany/accruals/preview`, giving the source book, the two
   profile IDs and the window. `accrualTo` is exclusive: June is `2026-06-01` to `2026-07-01`.
   Nothing is saved. The response shows each slice, the rounded amount, the draft that would be
   retained and an evidence digest.
2. Draft with `POST /api/ledger/intercompany/accruals/drafts`, sending the same selection and the
   digest from the preview. If terms, profiles, principal or FX changed in between, the request is
   refused and you preview again.
3. Review the draft in the source book's journal queue like any other entry. It is dated on the last
   accrued day and is not submitted, approved or posted for you.
4. After it posts, the counterpart arrives as a proposal draft in the other entity's review queue.

Both routes need ledger management permission. You cannot supply a rate, day count, principal or
amount; they are resolved from approved records. Do not edit the computed lines. If the amount is
wrong, correct the terms, profile or principal and recompute: a draft that has not yet been submitted
is refreshed in place, and edited lines block review.

An accrual is unavailable, with the reason stated, when a day in the window has no approved
`LoanInterest` profile revision or falls outside the declared schedule; when the loan is
floating-rate, compounding or unstructured; when no principal is posted under the `Loan` profile;
when a posted accrual already covers part of the window (reverse it first); when the source book has
no open period containing the last accrued day; or when a cross-currency loan has no approved FX
configuration. After an amendment, approve a `LoanInterest` profile revision that pins the amended
legal version before accruing the affected days; the days on each side of the amendment then use
their own rate. Fees are not calculated.

Ordinary legal approval retains the independent-reviewer rule. To attest an external legal review,
select the exact submitted hash and accepted retained review document, identify the distinct
external reviewer and date, and supply the attestation rationale. The preparing Meridian operator
may record this separate decision; the audit identifies the external reviewer and authenticated
attester separately. The same evidence-bound option applies to legal lifecycle revisions. It does
not authorize self-approval of a journal.

Existing legacy-attribution review stays independent by default. Explicit operator-reviewed mode
requires the same retained preparing/reviewing operator, evidence and review rationale. Inspect the
exact source digest and legal/profile/account pins before recording it; a correction appends a new
attribution rather than changing the posted line.

To retract an active assertion, append a reviewed successor with `Revoked = true`, the exact
expected prior attribution and a review rationale. Preserve every source, legal/profile, account
and transaction pin; do not use revocation to relink an assertion. The active projection excludes
the retracted assertion, and its immutable history remains available.

Evaluate obligation advisories for the selected book/fund and as-of date. The default expiry horizon
is 30 calendar days. Inspect expiry, coverage, lifecycle and guarantee findings and their source
versions; refresh stale results and investigate unavailable authority. These advisory findings do
not change close-readiness blockers or posting eligibility. Existing blocking balance-schedule
requirements and controlled-close rules remain authoritative.

For WindingDown, use a retained controlled-close authorization for the exact settlement, closing
expense, tax, revaluation or correction obligation. New commitments remain blocked. Dissolved and
Merged entities remain readable; exceptional correction requires separately approved evidence.
Do not remove an owned entity merely to make a perimeter preview succeed.

Prepare, submit and approve a closing or correction draft before obtaining its exact controlled-close
authorization. Review checks legal, profile, account and FX authority without granting posting
eligibility or requiring a posting approval in advance. Actual append always rechecks lifecycle
status and the final retained approval and authorization; a successful review posts nothing.

The shared API families are `/api/fund-structure/intercompany-agreements`,
`/api/ledger/intercompany-accounting-arrangements`, entity
`/lifecycle` and `/timeline`, `/api/ledger/intercompany/account-bindings`,
`/api/ledger/intercompany/legacy-attributions`, `/api/ledger/intercompany/proposals`,
`/api/ledger/intercompany/accruals`, `/api/ledger/intercompany/breaks`, book `/controls`, `/api/ledger/consolidation`, and its `/transitions` preview/activation/history routes.
Use the host's authenticated capability responses; service absence is an unavailable capability.
The native agreement `/{id}/schedule` read selects an approved version or proposal for dates-only
preview. `/api/ledger/intercompany/obligations` requires an owned ledger book and explicit as-of
date, with optional `expiryHorizonDays` defaulting to 30. The response retains schedule/guarantee
observations and source fingerprints without granting financial authority.
Decimal amounts on this new monitor response are exact strings. The existing canonical balance
schedule's numeric JSON and fingerprint remain unchanged; clients must not reserialize the
monitor response to manufacture a canonical schedule hash.
PostgreSQL is the governed multi-resource commit authority. File stores are durable local/demo
counterparts and do not establish production certification or cross-process transactional fencing.

Canonical legal-reference postings require the ledger-owned standalone transaction or the
sealed native producer that retains authority through its outer commit. Generic caller-owned
transactions and ordinary atomic-lot append APIs refuse those references; acquiring a caller
lease does not override that capability boundary. Native legal-only journals remain valid,
but governed role-based elimination requires reviewed accounting-profile attribution first.

See the [domain dictionary](../domain/intercompany-agreements-and-entity-lifecycle.md),
[versioned consolidation scope](../domain/intercompany-consolidation.md) and
[implementation sequence](../engineering/plans/intercompany-entity-controls.md) for evidence and
unsupported boundaries. Retain exact check results and Windows limitations with the change.
