# Intercompany and entity controls

**Status:** active procedure

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-12

Use Accounting's entity/intercompany and consolidation views to inspect the shared entity timeline,
agreements, books, source evidence, counterpart proposals and consolidation output. Desktop uses
the same host APIs. Open a retained source before approving a material journal; a proposal or
preview is not a posted balance.

1. Establish the tenant-owned organization and entity/book mappings. Retain evidenced lifecycle
   windows and successor identities. Uncovered history remains unknown.
2. Configure chart roles and append bindings for stable chart nodes. Keep old ledger names as
   historical aliases when renaming. Use one unambiguous output account per elimination role.
3. Create and independently approve legal terms and exact retained agreement evidence in the
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
`/api/ledger/intercompany/breaks`, book `/controls`, `/api/ledger/consolidation`, and its `/transitions` preview/activation/history routes.
Use the host's authenticated capability responses; service absence is an unavailable capability.
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
