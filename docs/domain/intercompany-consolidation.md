# Intercompany consolidation — versioned reviewed overlays

**Status:** implementation extended; current verification pending
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-12

Consolidation is a reviewed overlay on immutable entity journals. A proposed elimination is not a
posted balance. The shared consolidation view reports normal account balances as gross, proposed
incremental elimination, posted elimination, actual consolidated balance, and a separately labeled
preview. Actual consolidated equals gross plus posted eliminations. Preview additionally includes
the proposed increment. Unmatched receivable and payable amounts remain separate by direction.

## Governed extensions and retained v1 behavior

The retained `w10-v1` behavior below remains unchanged for books configured with that exact policy.
The staged extensions select their rule version from the elimination book's exact accounting
policy; changing versions requires regenerated evidence and renewed review.

| Rule version | Declared scope |
| --- | --- |
| `w10-v1` | Exactly two direct wholly owned entities; same functional currency; canonical account names. |
| `w10-v2` | All direct wholly owned entities, at least two; same functional currency; stable chart-node roles and historical account aliases. |
| `w10-v3` | V2 with governed whole-trial-balance reporting translation, retained historical/opening evidence and a translation reserve. |
| `w10-v4` | V3 plus a separately enabled `consolidation.revenue-expense` policy rule and shared intercompany transaction identities. |

Current bindings distinguish chart path from ledger account name. Historical/archived source
identities still classify outstanding balances; output accounts must be currently eligible.
Source and account revisions are retained in the evidence. Reviewed legacy-attribution sidecars
supply agreement and transaction identity over exact immutable line digests without editing those
lines. Agreement-specific adoption of earlier pair-only overlays creates reviewed reclassification
adjustments rather than duplicating the net elimination.

Matching remains directed and separates agreements, matching currency and income transaction
identity. An agreement cannot have ambiguous original matching currencies. Balance mismatches use
original transaction amounts; reporting conversion differences are retained separately. Income
elimination requires both exact policy rules, with approval/evidence required and auto-posting off.
Same-role expense reallocations already net in gross expense and need no income elimination.
Their signed directional income residual rows may remain visible; the separate monetary schedule
and counterpart breaks determine due-to/due-from readiness.

V3/v4 pin approved immutable rate sets and book-owned translation policies. Assets/liabilities use
closing rates, P&L uses transaction dates or explicitly declared periods, and equity uses supported
historical/opening evidence. No inferred historical dates, future quotes or silent rate substitution.
The whole translated gross trial balance includes an explicit reserve, with economic translation
and rounding differences separately inspectable. Missing authority/rates blocks drafting and
incomplete translation does not return a partial consolidated balance as complete.

Posting retains the existing audit/ownership/policy guards and holds current chart/binding, FX and
legacy-attribution authority leases through commit. All source books plus the overlay book remain
in freshness evidence. Prior v1 records retain their original bytes; migration stales pending v1
drafts. An ownership change cannot silently alter a used book's reporting members. An explicit retained
transition preview compares old/new members and pins predecessor overlays plus the new source,
policy, role and FX evidence. Independent approval opens a new empty dedicated reporting book at
the cutover; its initial target rebuilds from new gross and never deducts predecessor overlays.
Old books remain historical before cutover and are blocked at/after it. Separate transition receipt
lineage is not correction lineage. Proven former-member exposures remain gross, explicitly marked
`FormerPerimeterExternal`, and are never eliminated; unknown outside parties still block drafting.
Indirect/partial ownership, minority interest and investment/equity elimination remain unsupported;
lifecycle state does not silently drop residual balances.

Shared browser and WPF entity/consolidation presentation consumes `ConsolidationViewDto` and the
same preview/draft routes. The legacy summed view remains a gross view. Windows desktop execution,
current PostgreSQL/endpoint/browser checks and hosted CI must be reported separately; source and
new test presence alone do not prove them. W10-CONSOL-001 remains `planned` pending that evidence.
See [agreements and lifecycle](intercompany-agreements-and-entity-lifecycle.md), the
[implementation plan](../engineering/plans/intercompany-entity-controls.md) and
[operator procedure](../operators/intercompany-entity-controls.md).

## Retained v1 supported scope and setup

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
  Policy resolution uses the book's exact ID and version even when another version is effective.
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

`ManageLedgerReports` can create consolidation drafts and open the existing manual-journal review
workbench, subject to the same fund ownership checks. `ViewLedgerReports` remains read-only and does
not grant access to pre-posting review. Preview responses carry a server-calculated draft capability;
read-only viewers retain the balance view with draft creation disabled. Ordinary manual journals cannot enter a book designated with
the consolidation policy, even when the caller omits consolidation tags.

## Freshness, reruns and corrections

Evidence binds both source books, the elimination book, exact source lines, authoritative ownership,
policy, period/date and expected draft lines. Review is checked at submit, approve, post and recovery.
The current intercompany chart configuration is rechecked at each review and posting transition;
archived, renamed, retyped or newly scoped accounts block the retained draft.
Draft headers retain only the authoritative group root; entity attribution stays on the reviewed
lines. Edited scope, entry type or treasury context requires repair and renewed review.
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

Deterministic scope and evidence keys include perimeter, period, date, reciprocal entities and rule version.
Overlapping periods have separate pending-draft views while posted overlays remain cumulative.
An unchanged rerun returns the same retained draft, and an already-posted target creates no new
elimination. Posted eliminations through the as-of date are included in the cumulative target;
subsequent dates propose only the remaining increment. Corrections create signed adjustments linked
to a prior posted journal. The append boundary requires correction intent, adjustment classification
and approved adjustment metadata as well as the source-journal link.
Initial eliminations require originating classification without correction metadata. Both initial
and correction postings must retain the exact reviewed consolidation rule ID and version.
Reductions reverse the excess through a newly reviewed adjustment;
no posted entry is replaced. Generic manual reversal/rebook is blocked for these drafts so that
corrections always use current consolidation evidence. Exact committed posting receipts can finish
recovery even if source balances have subsequently moved.

Posted elimination dates must be processed in order. An earlier date is blocked when the dedicated
book already contains a later-dated elimination; the append boundary enforces this again under the
audit lock. This first slice does not automatically recalculate subsequent overlays after backdating.
Rerunning repairs an editable generated draft whose lines were changed, using the current authoritative
calculation and requiring review again; posted journals remain immutable.

## Evidence and remaining roadmap scope

Implementation: `ConsolidationPerimeterResolver`, `ConsolidationService`,
`ConsolidationWorkbenchService`, the manual journal consolidation guard, and
`PostgresLedgerJournalStore.Consolidation.cs`. Automated coverage includes
`ConsolidationPerimeterResolverTests`, `ConsolidationServiceTests`,
`ConsolidationPostingGuardTests`, `ConsolidationSourcesPostgresTests`,
`ConsolidationManualWorkflow` tests and the browser consolidation panel tests.

The full roadmap item remains planned. Governed FX, separate translation breaks, additional
treatments and shared desktop presentation now have staged implementation paths; acceptance needs
the current change's concrete verification rather than the historical first-slice evidence alone.
New targeted coverage includes `ConsolidationGovernedServiceTests`, `ConsolidationFxTests`,
`LegacyIntercompanyAttributionTests`, account-binding rules/file/PostgreSQL tests, and the
entity/intercompany workstation tests. Their current results are not asserted by this document.

Validation covers actual policy, drafting, approval, posting and projection services, live PostgreSQL
freshness and concurrency checks, authorization, and browser preview/draft flows. Chromium exercised
source disclosure, draft creation and scope invalidation at 1440×900 and 1366×768 using intercepted
API fixtures. The canonical browser bundle uses the locked dependencies. Final repository test counts
and authoritative GitHub Actions results are recorded in the pull request.
