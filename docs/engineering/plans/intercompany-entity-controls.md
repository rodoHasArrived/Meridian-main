# Intercompany contracts and entity controls: staged implementation

**Status:** implementation complete; development verification recorded 2026-11-14; four W12 items accepted by the owner 2026-11-15

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-17

The adopted scope is P1–P5: an agreement registry, effective entity controls, stable account roles
and N-entity consolidation, reviewed counterpart proposals, and full reporting-currency
translation with income elimination and shared workstation presentation. The canonical roadmap
retains status; this plan records implementation decisions and verification boundaries.

| Stage | Decision and acceptance boundary |
| --- | --- |
| P1 registry | Reuse the canonical native legal registry for terms, evidence and independent review. Immutable accounting profiles pin its exact ID/version/hash and own only templates, party books, account mappings and bounded validity. Configuration owner approval never grants legal approval or novation. Guarantees have no posting treatment. |
| P4 lifecycle | Effective-dated history; enforce immediately on new books, warn on existing books until audited activation. Winding-down supports evidenced controlled close, with no new commitments. Keep residual balances visible. |
| P2 `w10-v2` | All direct 100%-owned legal entities, at least two, in one functional currency. Stable chart-node bindings with historical aliases; distinct chart paths and ledger names. Preserve v1 readers and posted overlays. |
| P3 proposals | Atomic source-commit retention, durable retry, source-identity dedupe, explicit existing-counterpart linking, balanced destination drafts and destination review. Aging names its basis. Never auto-post. |
| P5 `w10-v3` | Whole-trial-balance translation using approved immutable rate sets and policy/configuration revisions. Closing, transaction-date/declared-period and historical conventions; retained opening facts; translation reserve; separate economic FX, rounding and intercompany breaks. |
| P5 `w10-v4` | The policy explicitly enables both `consolidation.receivable-payable` and `consolidation.revenue-expense`. Income pairs require a shared transaction identity. Both rules' exact versions remain in the reviewed evidence. |

P1 and P4 share no implementation dependency. P2 follows their contract seams; P3 consumes
approved legal revisions and accounting profiles; v3/v4 follow role identity and governed FX authority. These
stages are policy capabilities, not automatic activation milestones. An unchanged v1 book
continues to use its original rule; changing a book's version requires fresh review.

Migration inventories lifecycle history, chart-role bindings, source currency, agreements and
unresolved legacy lines. Independently reviewed legacy attribution pins each exact source digest
without rewriting journals. When pair-only v1 overlays must become agreement-specific, v2 creates
a linked reviewed reclassification: reverse the old attribution and add the new attribution,
with no duplicate net elimination. Missing or ambiguous history blocks activation. Never infer
historical eligibility from the current status.

Posting holds lifecycle, canonical legal, accounting-profile/account and applicable consolidation authority through the
owned transaction commit. Consolidation keeps source/overlay version checks, authoritative
ownership, ordered processing, stale-review refusal, deterministic reruns and linked corrections.
Source-map, lifecycle, agreement, rate or policy changes require a new calculation and review.
Migration controls do not weaken existing financial or authorization invariants.

The integrated native registry uses one graph fence, sorted lifecycle authority and a proof-aware
legal command transaction. Posting batches validate all native references under one sorted legal
lease before operational profile authority; they never reacquire the same shared graph or legal
fence behind a waiting writer. The bounded native preparer-attested dividend path retains these
entity controls through its outer journal, spine and case commit. Ordinary manual review uses
the same combined controls in a rollback-only validation transaction.

The bounded perimeter excludes indirect/partial ownership, minority interests and investment/equity
elimination. A reviewed reporting transition pins the prior book's retained overlays and the new member/source,
account, policy and FX population. Independent approval opens a new empty dedicated book at a
strictly later cutover, rebuilding the new target without importing old overlays. The old book
remains historical before cutover and is blocked afterward. Unknown or unapproved in-place member
changes still block. Former-member external exposures stay in gross without elimination. Guarantee posting,
automatic economic novation, automatic posting, live provider FX sourcing and external-GL posting are
outside this implementation. Loan-interest mirroring was outside the original P1–P5 scope and is now
covered by the governed accrual extension below. Browser and WPF use shared contracts and
APIs; WPF runtime proof requires Windows.

Verification must cover old v1 evidence, three/ten-entity populations, account rename/retirement,
source and mapping races, legacy attribution/reclassification, equal-amount independent events,
retry/restart, reciprocal directions, duplicate manual counterpart candidates, destination authority,
new versus legacy book controls, effective lifecycle boundaries, controlled close, missing/stale FX,
period/historical conventions, translation reserve and rounding, and income-rule gating. Added tests
and source paths are inspectable; their results are in the [verification record](#verification-record-2026-11-14).
No live-provider success is implied here.

## Structured terms and obligation-monitor extension

The subsequent monitoring/structured-terms extension reuses this foundation rather than repeating
its original stage numbering or creating another legal registry. Optional native structured terms
retain rates, schedules, bilateral allocation declarations and guarantee disclosure; pure date
previews reuse the existing kernels and do not calculate charge amounts. External-review attestation
adds a separately labeled, evidence/hash-bound sole-operator path alongside ordinary independent
legal review. Explicit operator-reviewed legacy attribution retains exact source and authority pins.

The obligation monitor consumes the actual balance schedule and canonical legal/lifecycle sources,
with a 30-calendar-day default expiry horizon, version/freshness evidence and a nonblocking close
advisory companion. Existing schedule blockers, posting controls, reporting transitions and exact
`w10-v1` through `w10-v4` behavior remain intact. Typed terms persist in the existing PostgreSQL
JSONB snapshots without schema replacement or historical hash migration. Acceptance evidence for
this extension is separate from full W10/W12 completion; roadmap statuses are not promoted merely
because the source exists. See the [domain extension](../../domain/intercompany-agreements-and-entity-lifecycle.md#obligation-monitoring-and-structured-legal-terms).

## Governed loan-interest accrual extension

`DEC-IC-ACCRUAL-001` and [ADR-028](../../adr/028-governed-intercompany-interest-accrual.md) bring one
calculation into scope; roadmap row `W12-ICACCRUAL-001` carries its status. It depends on the P1
registry and profile stages and on the P3 proposal pipeline, and on the governed FX authority for
cross-currency loans.

| Piece | Decision and boundary |
| --- | --- |
| Calculator | `IntercompanyAccrualPreviewService` is pure. It reuses the date preview's schedule mapping and the shared day-count kernel, and refuses anything that would need an inferred term. |
| Proration | Rule `intercompany-accrual-v1`: each day accrues under the approved revision applicable on that day; the window is cut at profile-revision, schedule-period and principal boundaries; slices are rounded once. |
| Principal | Read from posted journals of the approved `Loan` profile of the same legal agreement in the source book. It is never entered by the operator. |
| Profile | `LoanInterest` is a new reviewed template for loan agreements. Existing `Loan` profiles keep their principal-only mirror. |
| Routing | The source side is a draft in ordinary journal review. The destination side is a counterpart proposal draft after the source posts. Nothing posts automatically. |
| Evidence | Agreement version and hash, profile revision, principal journals, calendar and kernel identity and FX conversion are retained with a digest on the draft and the posted journal. |

Fees, floating rates, compounding and scheduled runs are not part of this increment. Unit-level
tests cover the calculation, refusals, evidence retention and the counterpart mirror.

### Operator surfaces and service-backed evidence (2026-11-16)

This records development verification of the two exit criteria that were open. All fixtures are
synthetic; it is not a release decision and `W12-ICACCRUAL-001` stays `in_progress` until the
repository owner accepts it.

| Piece | What exists | Verification |
| --- | --- | --- |
| Browser | "Accrue interest" on an approved `LoanInterest` profile in Accounting's accounting-profile panel (`accounting-screen.entity-interest-accrual.tsx`). The operator picks the loan principal profile of the same legal agreement and the window; the rate, principal and amount come from the server. Changing the selection withdraws the preview. | `accounting-screen.entity-interest-accrual.test.tsx` (Linux) |
| Desktop | Interest accrual tab on the Fund Ledger page (`IntercompanyInterestAccrualControl.xaml` over `IntercompanyInterestAccrualWorkspace`), using the same `/api/ledger/intercompany/accruals/preview` and `/drafts` routes. The book and both profiles are chosen from lists over `/api/ledger/books` and the book-scoped profile read (2026-11-17); the counterpart queue picks its source and existing-counterpart books the same way. | `IntercompanyInterestAccrualWorkspaceTests`, `IntercompanyBookChoiceTests`, `WorkstationIntercompanyAgreementApiClientTests` (Linux); `IntercompanyNativeControlSmokeTests.InterestAccrualControl_*` builds and runs only on Windows (it compiles on Linux only with `/p:EnableFullWpfBuild=true`) |
| Post and deliver | `IntercompanyAccrualPostAndDeliverPostgresTests`: a posted drawdown, the accrual drafted from approved terms, ordinary review and posting in the lender's book, one counterpart draft delivered to the borrower, and the borrower's own review and posting. No second proposal is mirrored back and the same days cannot be accrued again. A second case dissolves the borrower in its lifecycle history before the accrual date; the production lifecycle control then refuses the borrower's counterpart and its book stays empty. | PostgreSQL |

In the PostgreSQL run every store the accrual touches is durable (updated 2026-11-17):

- The ledger, proposal outbox, account bindings, chart and book ownership are in the ledger schema.
- The native legal agreement registry, accounting-profile history, legal-entity graph and entity
  lifecycle history are in a fund-structure schema of the same database. The loan is created,
  submitted and approved by different people through `IntercompanyAgreementService`, and both
  profiles pin that exact approved revision.
- Journal drafts are in `PostgresManualJournalEntryDraftStore` (ledger migration 077), the store
  a host with a ledger database registers since 2026-11-18. The run reopens the draft table to show
  the reviewed source and delivered counterpart, with their server-held proposal identity and
  digest, survive a restart. Hosts without a ledger database, and the desktop app, keep
  `FileManualJournalEntryDraftStore`.

`IntercompanyLedgerPostingAuthority`, `PostgresIntercompanyAgreementPostingAuthority` and
`LifecycleLedgerPostingAuthority` (over `PostgresFundStructureService` and the graph store's
consolidation lease) are the production classes. Only the agreement access scope, the evidence
vault, the accounting-profile scope authority and the fund-account read service are synthetic.

**Defect found and fixed.** Every generated counterpart draft carries the treasury label
`IntercompanyMirror` as its fund event type. Both the journal workbench and the durable ledger guard
read any fund event type as a private-capital fund event that also needs a fund event identity and
a capital account, which a counterpart does not have. A delivered counterpart could therefore be
saved but never submitted or posted through journal review; earlier PostgreSQL tests posted
counterparts by direct append and did not reach this. The workbench now treats that label on a
retained counterpart as a classification: it is kept as the posting's source event type and is not
written as treasury fund-event metadata. The exemption needs the server-retained proposal identity
and applies only when no fund event identity, capital account or investor is stated; the ledger
guard is unchanged. `IntercompanyProposalDeliveryRetryTests` covers this in memory, and
`IntercompanyAccrualPostAndDeliverPostgresTests` covers it over PostgreSQL drafts.

### PostgreSQL journal draft store (2026-11-18)

Manual journal drafts now have a PostgreSQL store, like every other governed store in this area.

| Part | Behaviour | Evidence |
| --- | --- | --- |
| Draft table | `V_ledger_077` keys each draft by fund, tenant, company and journal ID, compared case-insensitively, exactly as the file store does. Each row retains the complete draft, so server-held fields such as the intercompany proposal identity and source digest round-trip unchanged. Save and batch save are one transaction. | `ManualJournalEntryDraftStoreContract`, run against the file store and PostgreSQL |
| Cross-host serialization | `PostgresManualJournalMutationRecoveryStore` holds one PostgreSQL session advisory lock per ledger schema for each whole journal command and retains command receipts in the same migration. The database refuses to rewrite or remove a completed receipt. | `PostgresManualJournalEntryDraftStoreTests`; `AccountingConfigurationServiceTests.PostgresDrafts_ConcurrentEditsFromTwoHosts_OneWinsAndTheOtherIsRefusedAsStale` |
| Registration | `AddIntercompanyEntityServices` registers the PostgreSQL draft store when `MERIDIAN_LEDGER_CONNECTION_STRING` is configured; the workstation pairs it with the PostgreSQL receipt store. Otherwise the file stores remain. The desktop app is unchanged. | Composition code |

Completed PostgreSQL receipts are retained in place; unlike the file store they are not archived
or pruned. Optimistic draft version checks stay in the workbench service.

### Counterpart journal picker and desktop journal link (2026-11-18)

| Piece | What exists | Verification |
| --- | --- | --- |
| Shared read | `GET /api/ledger/intercompany/proposals/{id}/counterpart-candidates?targetBookId=` lists posted journals in an approved destination book that carry the source transaction's identity and pass the same check the link uses (`IntercompanyProposalDeliveryService.ListCounterpartCandidatesAsync`). It requires ledger-report view permission and access to both books. It is a convenience: the link still re-checks the chosen journal under the journal mutation lease. | `IntercompanyProposalResolutionTests.CounterpartCandidates_*` (Linux) |
| Browser | The counterpart journal is chosen from that list once a counterpart book is selected; changing the book clears the choice. | `accounting-screen.entity-operations.test.tsx` (Linux) |
| Desktop | The Counterpart proposals tab picks the journal from the same list. "Open journal" on a delivery record opens the Fund Ledger Journal tab for the book's fund profile, through a new optional `JournalEntryId` on `FundOperationsNavigationContext`, and selects the entry or says it is not in that fund's loaded view. | `IntercompanyCounterpartQueueWorkspaceTests`, `WorkstationIntercompanyAgreementApiClientTests` (Linux); `IntercompanyNativeControlSmokeTests.CounterpartProposalsControl_*` and `FundLedgerViewModelTests.LoadAsync_WithJournalDeepLink_*` build on Linux and run only on Windows |

### Fixed management-fee extension (2026-11-17)

`DEC-IC-FEE-001` and [ADR-029](../../adr/029-governed-intercompany-fixed-management-fees.md) add rule
`intercompany-fee-v1`; roadmap row `W12-ICFEE-001` carries its status. Development verification only, over
synthetic fixtures.

| Piece | Decision and boundary | Verification |
| --- | --- | --- |
| Terms | Fixed management fees declare `FixedFeeBasis` (per period, or per annum with a day count). Null-omitted, so earlier revisions keep their review hashes. | `IntercompanyFeeAccrualPreviewServiceTests` |
| Calculation | Per-period fees prorate by covered days over period days; per-annum fees by the declared day count; cut at revision and period boundaries; rounded once. Percentage fees and an undeclared basis are refused. | `IntercompanyFeeAccrualPreviewServiceTests` (hand-derived amounts) |
| Direction and draft | The declared service provider books receivable against revenue; the recipient expense against payable, under the existing `ManagementFee` profile. | `IntercompanyFeeAccrualDraftServiceTests`, `IntercompanyFeeAccrualSourceBuilderTests` |
| Evidence and controls | Fee evidence shares the accrual-evidence fields under its own rule; the guard dispatches on it. The workbench refuses overlapping posted fees and re-resolves each slice's authority before review or posting. | `AccountingConfigurationServiceTests.IntercompanyFee`, `IntercompanyFeeAccrualDraftServiceTests` |
| Counterpart | The existing mirror delivers the recipient's expense and payable after the source posts. | `IntercompanyFeeAccrualSourceBuilderTests` |
| Surfaces | Browser "Accrue fee" and basis field; WPF Management fee tab and basis field, over `/api/ledger/intercompany/fees/preview` and `/drafts`. | `IntercompanyManagementFeeWorkspaceTests`, browser tests; WPF smoke test builds and runs only on Windows |

Percentage fees, loan commitment or utilisation fees and scheduled runs remain out of scope.

### Floating-rate extension (2026-11-17)

`DEC-IC-FLOAT-001` and the ADR-028 extension add rule `intercompany-accrual-v2`; roadmap row
`W12-ICFLOAT-001` carries its status. Development verification only, over synthetic fixtures.

| Piece | Decision and boundary | Verification |
| --- | --- | --- |
| Terms | Floating loans declare `FixingLagBusinessDays` (0-10). Null-omitted, so earlier revisions keep their review hashes. | `IntercompanyStructuredTermsTests` |
| Fixings | `IntercompanyRateFixingService` with append-only, tenant-scoped versions: propose, approve by someone else, reject with a reason, correct by a new approved value. PostgreSQL store in fund-structure migration `009`, or an atomic file store. | `IntercompanyRateFixingServiceTests`; `IntercompanyRateFixingStoreTests` (file and PostgreSQL) |
| Calculation | Each period uses exactly the approved fixing observed the lag before its start, plus the spread. A missing fixing refuses the window. All-fixed windows stay v1. | `IntercompanyFloatingAccrualPreviewTests` (hand-derived amounts) |
| Evidence and staleness | Floating slices pin fixing identity, rate, spread, lag and value digest. The evidence guard refuses a rate that differs from its pin; the posting authority re-reads each pin before review or posting. | `IntercompanyAccrualDraftServiceTests.Floating` |
| Routes | `/api/ledger/intercompany/rate-fixings` list, propose, decide. Manage-ledger permission and tenant scope for mutations; exact decimal strings. | `IntercompanyRateFixingEndpointTests` |
| Surfaces | Browser Rate fixings tab and fixing column; WPF Rate fixings tab, fixing column and the lag field in both agreement editors. | `IntercompanyRateFixingWorkspaceTests`, browser tests; WPF smoke test builds and runs only on Windows |

Fees, compounding, in-arrears or averaged rates, floors, caps and scheduled runs remain out of scope.

## Reciprocal balance confirmation extension (2026-11-17)

`DEC-IC-CONFIRM-001` brings the advisory slice of reciprocal balance confirmation into scope; roadmap
row `W12-ICCONFIRM-001` carries its status. Semantics are in the
[domain document](../../domain/intercompany-agreements-and-entity-lifecycle.md#reciprocal-balance-confirmation).

| Piece | What exists | Verification |
| --- | --- | --- |
| Rules | `IntercompanyBalanceConfirmationRules`: proposal, confirm, dispute, resolve and advisory findings over a pinned balance digest. | `IntercompanyBalanceConfirmationRulesTests` |
| Storage | `V_ledger_073__intercompany_balance_confirmations.sql` (append-only, one confirmation per tenant, company, profile and date) behind `PostgresIntercompanyBalanceConfirmationStore`; `FileIntercompanyBalanceConfirmationStore` for hosts without a ledger database. | `IntercompanyBalanceConfirmationStoreTests` (file and PostgreSQL) |
| Service and routes | `IntercompanyBalanceConfirmationService` reads each side from the balance schedule and checks each side's book; `/api/ledger/intercompany/confirmations` (summary, propose, actions) sends exact decimal strings. | `IntercompanyBalanceConfirmationServiceTests`, `IntercompanyBalanceConfirmationEndpointTests` |
| Close readiness | Findings join the existing intercompany close advisory as `intercompany.confirmation.*` warnings, after eligibility is decided. A book's approved confirmation close policy is the separate path that can block close (next section). | `IntercompanyCloseAdvisoryAuthorizationTests` |
| Browser | "Balance confirmation" tab in Accounting's entity operations (`accounting-screen.entity-confirmations.tsx`). | `accounting-screen.entity-confirmations.test.tsx` |
| Desktop | Balance confirmation tab on the Fund Ledger page (`IntercompanyBalanceConfirmationControl.xaml` over `IntercompanyBalanceConfirmationWorkspace`). The ledger book is a typed ID, as on the other desktop intercompany tabs; profiles are chosen from a list. | `IntercompanyBalanceConfirmationWorkspaceTests`, `WorkstationIntercompanyAgreementApiClientTests` (Linux); `IntercompanyNativeControlSmokeTests.BalanceConfirmationControl_*` runs only on Windows |

Not in this first slice: pairs spanning two companies of one tenant, and adjustments from a dispute.
Opt-in close blocking was added on 2026-11-18, and both of those on 2026-11-25; see the next sections.

### Opt-in confirmation close policy (2026-11-18)

ADR-032 and decision `DEC-IC-CONFIRM-002` (accepted by the owner on 2026-11-25) let one ledger book
adopt a reviewed confirmation close policy. Books without one keep advisory-only confirmation.

| Piece | What exists | Verification |
| --- | --- | --- |
| Rules and storage | `IntercompanyConfirmationClosePolicyRules` (propose, approve by someone else, reject or disable with a reason; evaluation that fails closed on unreadable balances). `V_ledger_078__intercompany_confirmation_close_policies.sql` (append-only, update/delete/truncate refused) behind `PostgresIntercompanyConfirmationClosePolicyStore`; `FileIntercompanyConfirmationClosePolicyStore` otherwise. | `IntercompanyConfirmationClosePolicyRulesTests`, `IntercompanyConfirmationClosePolicyStoreContract` (file and PostgreSQL) |
| Close readiness | `IntercompanyConfirmationCloseGate` adds the `intercompany-confirmation` contributor only for a book with an active policy: `Blocked` for a listed finding at the period end, `Ready` otherwise, `Unavailable` when the period or confirmation cannot be read. The advisory list is unchanged. | `IntercompanyConfirmationCloseGateTests`, `FinancialOperationsCommandCenterReadServiceTests.ConfirmationClosePolicy_*`, `IntercompanyCloseAdvisoryTests` (unchanged) |
| Routes | `/api/ledger/intercompany/confirmation-close-policies`: list, propose and decide, scoped to a book the caller's tenant and company own. | `IntercompanyConfirmationClosePolicyEndpointTests` |
| Browser | Close-policy panel on the balance-confirmation screen (`accounting-screen.entity-confirmation-close-policy.tsx`). | `accounting-screen.entity-confirmations.test.tsx` |
| Desktop | Close-policy section on the Balance confirmation tab (`IntercompanyBalanceConfirmationWorkspace.ClosePolicy.cs`). | `IntercompanyBalanceConfirmationWorkspaceTests` (Linux); `IntercompanyNativeControlSmokeTests.BalanceConfirmationControl_*` builds on Linux and runs only on Windows |

### Cross-company pairs and adjustment drafts (2026-11-25)

[ADR-033](../../adr/033-intercompany-confirmation-cross-company-and-adjustments.md) and decision
`DEC-IC-CONFIRM-003` (accepted by the owner on 2026-11-25) close the two remaining confirmation gaps. Same-company
confirmations are unchanged.

| Piece | What exists | Verification |
| --- | --- | --- |
| Cross-company scope | The approved, tenant-scoped profile links a book in each of two companies. Each side is read in its owning company's scope; the confirmation records both companies and is visible to either; each company acts only on its own side, and a cross-company dispute needs a resolution from each side. `V_ledger_083__intercompany_confirmation_party_companies.sql` adds nullable party company columns and indexes; the advisory lock is keyed without the company. | `IntercompanyBalanceConfirmationCrossCompanyTests`, `IntercompanyBalanceConfirmationStoreContract` (file and PostgreSQL) |
| Adjustment drafts | `IntercompanyConfirmationAdjustmentDraftService` pre-fills one balanced ordinary draft per side and resolution for a `Resolved` confirmation: the side's mapped intercompany account with the approved profile's posting identities, and a non-intercompany offset. It never submits or posts. `POST /api/ledger/intercompany/confirmations/{id}/adjustment-drafts` reads exact decimal strings. | `IntercompanyConfirmationAdjustmentDraftServiceTests`, `IntercompanyBalanceConfirmationEndpointTests` |
| Browser | Company labels, "Record this book's resolution" for cross-company pairs, and the correction fields on `accounting-screen.entity-confirmations.tsx`. | `accounting-screen.entity-confirmations.test.tsx` |
| Desktop | The same on the Balance confirmation tab (`IntercompanyBalanceConfirmationWorkspace.Adjustments.cs`). | `IntercompanyBalanceConfirmationWorkspaceTests`, `WorkstationIntercompanyAgreementApiClientTests` (Linux); the XAML compiles only on Windows CI |

Not in this slice: cross-currency adjustments (prepared in the journal workbench) and multilateral pairs.

### Periodic compounding extension (2026-11-17)

`DEC-IC-COMPOUND-001` and [ADR-030](../../adr/030-intercompany-interest-compounding.md) add rule
`intercompany-accrual-v3`; roadmap row `W12-ICCOMPOUND-001` carries its status. Development verification only.

| Piece | Decision and boundary | Verification |
| --- | --- | --- |
| Base | Posted principal plus the posted accrued-interest balance of the loan-interest profile at each period start; settlements and reversals reduce it. | `IntercompanyCompoundingAccrualPreviewTests`, `IntercompanyCompoundingSourceTests` (real journals, reversal) |
| Coverage | Posted, unreversed accruals must cover every earlier day; one request stays in one period; frequency must equal the schedule. | `IntercompanyCompoundingAccrualPreviewTests` |
| Evidence | The balance, posted windows and first principal date are retained; the guard refuses v3 without them; the workbench re-reads them before review or posting. | `IntercompanyAccrualDraftServiceTests.Compounding` |
| Surfaces | Browser and WPF slice tables show compounded interest. | browser accrual test, `IntercompanyCompoundingSliceRowTests` |

Daily compounding and compounding that differs from the schedule frequency remain out of scope.

### Opt-in accrual schedules (2026-11-17)

`DEC-IC-SCHEDULE-001` and [ADR-031](../../adr/031-intercompany-accrual-schedules.md); roadmap row
`W12-ICSCHEDULE-001`. Development verification only.

| Piece | Decision and boundary | Verification |
| --- | --- | --- |
| Schedule | Book, loan-interest profile and principal profile; maker-checker; reject or disable with a reason; one open schedule per book and profile. | `IntercompanyAccrualScheduleRulesTests`, `IntercompanyAccrualScheduleServiceTests` |
| Storage | Append-only versions in ledger migration `076` or an atomic file store. | `IntercompanyAccrualScheduleStoreTests` (file and PostgreSQL) |
| Runs | Completed periods ending on or after approval; drafts through the operator path; existing drafts skipped; refusals reported; 24 periods per run. | `IntercompanyAccrualScheduleServiceTests` |
| Runner | Every six hours after a one-minute start-up delay; disabled schedules ignored; one failure never stops the rest. | `IntercompanyAccrualScheduleServiceTests` |
| Routes and surfaces | `/api/ledger/intercompany/accrual-schedules`, due preview and run now; browser and WPF tabs. | `IntercompanyAccrualScheduleEndpointTests`, workspace and browser tests |

Management-fee schedules follow once governed fee drafting is composed on main.

## Verification record (2026-11-14)

This records development verification for `W12-ICAGREE-001`, `W12-ENTITY-001`, `W12-ICPAIR-001`
and `W12-ICINCOME-001` in PR #3591. All fixtures are synthetic. It is not a live-provider result
or a release decision. The repository owner accepted the four items on this evidence on
2026-11-15; the limits below still apply.

### Hosted runs

All runs are on commit `8dc309d3bf706f3c5230e15800cc464a16b4c9c8`.

| Run | Scope | Result |
| --- | --- | --- |
| [43640326142](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640326142) | Meridian CI: `verify-dotnet`, `verify-browser`, `verify-docs`, `verify-workflows` (the `quality-gate` lanes) | All four lanes passed |
| [43640326142](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640326142) | Meridian CI: service-backed PostgreSQL integration (`Category=Integration`), the `integration-gate` evidence. The job rejects missing, failed, skipped or empty test evidence | Passed |
| [43640331964](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640331964) | Targeted Test, `Meridian.Tests`, non-integration tests matching Intercompany, Consolidation, EntityLifecycle, EntityPosting, EntityTimeline, ManualJournalAgreementReview | 651 passed, 0 failed, 0 not executed |
| [43640335479](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640335479) | Targeted Test, `Meridian.FundStructure.Tests`, Intercompany and EntityLifecycle | 169 passed, 0 failed, 0 not executed |
| [43640338279](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640338279) | Targeted Test, `Meridian.Ui.Tests`, Intercompany | 67 passed, 0 failed, 0 not executed |
| [43640341178](https://github.com/rodoHasArrived/Meridian-main/actions/runs/43640341178) | Targeted Test on `windows-latest` with the full WPF build, `IntercompanyNativeControlSmokeTests` and the EntityConsolidation view-model tests | 13 passed, 0 failed, 0 not executed |

The Targeted Test workflow provisions no database, so PostgreSQL-backed tests are evidenced only
by `integration-gate`.

### Cases and their tests

| Case | Tests | Lane |
| --- | --- | --- |
| Old v1 evidence | `ConsolidationGovernedServiceTests.V2_MigrationRecognizesV1PostedOverlayThroughHistoricalAliases`; v1 `ConsolidationServiceTests` | In-memory |
| Three- and ten-entity populations | `ConsolidationGovernedPopulationTests` (triangle, ten-entity mesh against an independent oracle, reordered-store rerun determinism) | In-memory, `w10-v2` |
| Account rename and retirement | `ConsolidationGovernedServiceTests.V2_ArchivedSourceBindingStillClassifiesOutstandingImmutableBalances`; `ConsolidationGovernedPopulationTests.ThreeEntityPopulation_SourceAccountRenameSuccessorAfterCalculation_IsRefusedAsStaleButStillClassifiesBalances` | In-memory |
| Source and mapping races | `ConsolidationGovernedPopulationTests.ThreeEntityPopulation_AmountNeutralJournalInAnyMemberBookAfterCalculation_IsRefusedAsStale`; `ConsolidationServiceTests.GovernedWorkbench_ChangeBetweenApprovalAndPost_RefusesPostingAndRequiresFreshReview` (source journal, source mapping, output mapping; v2 and v4) | In-memory |
| | `ConsolidationSourcesPostgresTests`; `IntercompanyAccountBindingPostgresTests` | PostgreSQL |
| Legacy attribution and reclassification | `LegacyIntercompanyAttributionTests`; `ConsolidationGovernedServiceTests.V2_AttributingLegacyBalancesReclassifiesPriorPairOverlayWithoutDoubleElimination` | In-memory |
| | `LegacyIntercompanyAttributionPostgresTests` (digest pin, replay, successor and revocation revisions, immutability, tenant and book scope, concurrency, authority lease) | PostgreSQL |
| Equal-amount independent events; duplicate manual candidates | `IntercompanyProposalDraftBuilderTests`; `IntercompanyProposalDeliveryRetryTests.PostedManualCounterpart_BlocksGenerationOnlyForTheSameAgreementAndTransactionIdentity` | In-memory |
| | `IntercompanyProposalPostgresTests.EqualAmountSources_CannotReuseOneCounterpartJournalToClearBoth` | PostgreSQL |
| Retry and restart | `IntercompanyProposalDeliveryRetryTests` (restart after draft saved, duplicate claim, different source digest); `IntercompanyProposalMissingArrangementTests` | In-memory |
| | `IntercompanyProposalPostgresTests` (expired lease reclaim, exact replay with concurrent retry, concurrent first commit) | PostgreSQL |
| Reciprocal directions | `ConsolidationGovernedPopulationTests` reverse pairs; `IntercompanyProposalDraftBuilderTests.MonetaryDirection_ProducesBalancedReciprocalDraft` | In-memory |
| Destination authority | `IntercompanyProposalDestinationAuthorityTests` (link, repricing preview and apply, breaks); `IntercompanyProposalDeliveryRetryTests.DestinationBookOutsideSourceTenantOrCompany_FailsDeliveryWithoutCreatingDraft` | In-memory |
| Aging basis and break classification | `IntercompanyProposalBreakAgingTests` | In-memory |
| New versus legacy book controls | `LedgerBookControlsPostgresTests` (migration warns, new books enforce, activation after a passing preflight with a retained receipt, stale preflight refused) | PostgreSQL |
| Effective lifecycle boundaries; controlled close | `EntityLifecycleHistoryTests`; `EntityPostingAuthorityTests`; `EntityLifecyclePostgresTests`; `EntityPostingReviewPostgresTests` | In-memory and PostgreSQL |
| Lifecycle read model | `EntityTimelineServiceTests` (revisions, ownership, successor, books, legal agreements of a merged entity) | In-memory |
| No legal novation through a profile | `IntercompanyAccountingArrangementTests` (`LegalNovation` partial) | In-memory |
| Missing or stale FX; period and historical conventions; reserve and rounding | `ConsolidationFxTests`; `ConsolidationGovernedServiceTests.V3_MissingFxAuthorityCannotProduceEliminatedFigures` | In-memory |
| Income-rule gating | `ConsolidationGovernedServiceTests.V4_IncomeEliminationRequiresItsExplicitTypedPolicyRule`, `V4_WorkbenchPostsBothGovernedTreatmentsAndLaterCorrectionOnlyMovesRemainingDelta` | In-memory |
| Shared workstation consumers | `IntercompanyAgreementWorkspaceTests`, `IntercompanyCounterpartQueueWorkspaceTests`, `IntercompanyInterestAccrualWorkspaceTests`, `WorkstationIntercompanyAgreementApiClientTests` | In-memory |
| | `IntercompanyNativeControlSmokeTests`, `EntityConsolidationViewModelTests`, `EntityConsolidationLifecycleViewModelTests` | Windows |

### Defect found and fixed

An accounting arrangement that the tenant registry no longer returned raised an unhandled
`KeyNotFoundException`. It aborted the whole claimed delivery batch and failed the aged-break
list. It now fails only that proposal and is classified `AgreementAuthorityUnavailable`.

### Limits of this verification

- Three- and ten-entity populations are proven for `w10-v2` in memory. Translated (`w10-v3`,
  `w10-v4`) and PostgreSQL-backed populations of that size are not.
- The delivery service checks both tenant and company on every path: delivery, linking, repricing
  preview/apply and the aged-break list. A company-bound book owner must match the caller's
  company, the same rule the HTTP endpoints apply; a book with no company binding is open to any
  company of its tenant. Covered by `IntercompanyProposalDestinationAuthorityTests`.
- In-memory delivery tests use a recording stand-in for agreement journal validation; real
  ledger authority is exercised only by the PostgreSQL tests.
- Guarantee posting, indirect or partial ownership, investment/equity elimination, live FX
  sourcing and external-GL posting remain out of scope, as stated above.
