# Intercompany consolidation — W10-CONSOL-001 first slice

**Status:** implemented slice, validation tracked with the change
**Owner:** Accounting and Ledger
**Reviewed:** 2026-10-07

Consolidation is a reviewed overlay on immutable entity journals. A proposed elimination is not a
posted balance. The shared consolidation view reports normal account balances as gross, proposed
incremental elimination, posted elimination, actual consolidated balance, and a separately labeled
preview. Actual consolidated equals gross plus posted eliminations. Preview additionally includes
the proposed increment. Unmatched receivable and payable amounts remain separate by direction.

## Supported scope and setup

- Exactly two direct legal-entity children of one authoritative fund ownership root, each with one
  effective `Owns` link at 100%. The organization/business/fund path is also verified. Ownership
  uses `[EffectiveFrom, EffectiveTo)` evaluated at the end of the as-of date in UTC; missing,
  conflicting, indirect and partial ownership blocks. Roots with any additional owned children
  are unsupported rather than silently narrowed.
- Both entities and all three books use one explicit shared functional currency. The response and
  retained evidence record that currency; USD is used in acceptance fixtures but is not hard-coded.
  No currency translation, FX differences, minority interests or proportional consolidation.
- Each entity has exactly one Primary source book in the root book's fund profile. Every source
  line explicitly identifies its posting entity and must agree with the book's retained entity.
- A dedicated Primary elimination book belongs to the fund root, with policy `consolidation-v1`
  version `w10-v1` and an open period containing the consolidation date. The entity books are not
  mutated by elimination posting. The dedicated book must contain only this perimeter's overlays.
- The first typed treatment is `ConsolidationElimination`, rule
  `consolidation.receivable-payable`, version `w10-v1`. It requires evidence and independent
  approval and never permits auto-posting. Configure chart paths **and account names** exactly as
  `Assets:Intercompany Receivable` (Asset) and `Liabilities:Intercompany Payable` (Liability).
  These accounts must be active and have no symbol or financial-account sub-scope.
- This slice does not eliminate investment/equity, revenue/expense, tax or other bases. Legacy
  `ProjectLedgerBook.ConsolidatedTrialBalance` and WPF's existing summed view remain gross views;
  the new shared service/HTTP projection is the authority for this slice's eliminated figures.

## Matching and review

For A posting against B, the rule compares A's receivable from B with B's payable to A. The reverse
direction is a different pair. The lesser nonnegative balance is proposed; a 100 receivable and 80
payable produce an 80 elimination and a 20 unmatched receivable. Missing, self or out-of-perimeter
counterparties on the supported accounts block drafting. Abnormal signed balances remain unmatched.
Other account balances remain in gross group totals.

Accounting's Ledger Explorer offers preview and draft generation. The shared routes are
`GET /api/ledger/consolidation/preview` and `POST /api/ledger/consolidation/drafts`; both take
organization, ownership root, elimination book, period and as-of date. Tenant/company and actor
come from the authenticated session. Production composition requires the authoritative PostgreSQL
ownership and journal stores. The generated draft enters the existing journal workbench for submit,
independent approval, posting, evidence and audit history. Source journal and line IDs and ownership
link IDs are retained for drill-through.

## Freshness, reruns and corrections

Evidence binds both source books, the elimination book, exact source lines, authoritative ownership,
policy, period/date and expected draft lines. Review is checked at submit, approve, post and recovery.
A changed source book (including a backdated or amount-neutral new journal) invalidates the draft;
the operator reruns and obtains renewed review. Old drafts remain inspectable and explicitly stale.
The PostgreSQL append boundary rechecks the as-of journal count and maximum sequence for all three
books while holding the same ledger audit lock used by every journal append, preventing a source
commit between the final balance check and elimination posting. Future-dated source journals do not
invalidate an earlier as-of review. The final append also re-resolves the complete evidence while
holding ownership-table and book-mapping locks plus the policy provider's mutation lease through
commit. Missing authority providers block posting. Consolidation uses the journal store's owned
transaction; caller-owned transaction append is rejected because it cannot retain these authority
leases through an external commit. These first-slice locks serialize ownership changes and book
configuration changes during elimination posting; ordinary source journal appends retain their
existing audit-lock ordering.

Deterministic scope and evidence keys include perimeter, date, reciprocal entities and rule version.
An unchanged rerun returns the same retained draft, and an already-posted target creates no new
elimination. Posted eliminations through the as-of date are included in the cumulative target;
subsequent dates propose only the remaining increment. Corrections create signed adjustments linked
to a prior posted journal. Reductions reverse the excess through a newly reviewed adjustment;
no posted entry is replaced. Generic manual reversal/rebook is blocked for these drafts so that
corrections always use current consolidation evidence. Exact committed posting receipts can finish
recovery even if source balances have subsequently moved.

## Evidence and remaining roadmap scope

Implementation: `ConsolidationPerimeterResolver`, `ConsolidationService`,
`ConsolidationWorkbenchService`, the manual journal consolidation guard, and
`PostgresLedgerJournalStore.Consolidation.cs`. Automated coverage includes
`ConsolidationPerimeterResolverTests`, `ConsolidationServiceTests`,
`ConsolidationPostingGuardTests`, `ConsolidationSourcesPostgresTests`,
`ConsolidationManualWorkflow` tests and the browser consolidation panel tests.

The full roadmap item remains planned: governed FX translation and separate translation breaks,
additional treatment families and full desktop presentation remain outside this first slice.

Validation on 2026-10-06: 249 focused .NET tests passed, including the accounting workflow,
authorization/query-binding checks and four live PostgreSQL tests for freshness and concurrent
appends; 55 ownership policy/service tests also passed. The full .NET CI roster passed 19,047
tests with five registered skips. Workflow tests exercise actual policy, draft, approval, posting
and projection services.

Initial browser validation passed 114 relevant tests, TypeScript, Vite, touched-file lint and axe
checks using preprovisioned dependencies. Chromium exercised the canonical Ledger Explorer at
1440×900 and 1366×768 using intercepted API fixtures. After a clean dependency install, strict
TypeScript and all nine consolidation panel/API tests passed. The first full repository CI attempt
stopped on a baseline dependency mismatch: Tailwind 4.3.3 with the Tailwind 3 PostCSS interface.

Integration on 2026-10-07 incorporates upstream's Tailwind 3.4.19 correction. A fresh install from
the merged lockfile, the canonical production bundle build and all 73 relevant app/consolidation
browser tests passed. Chromium rechecked preview, source disclosure, draft creation and scope
invalidation at both desktop sizes with no console errors or overflow. The bundle is regenerated
from those locked dependencies; the earlier clean-install blocker is resolved. Final repository
and hosted CI results are tracked with the pull request.
