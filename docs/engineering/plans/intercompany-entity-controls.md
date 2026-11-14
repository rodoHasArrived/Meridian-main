# Intercompany contracts and entity controls: staged implementation

**Status:** implementation complete; development verification recorded 2026-11-14, acceptance pending

**Owner:** Accounting and Ledger

**Reviewed:** 2026-11-14

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
loan-interest mirroring, automatic economic novation, automatic posting, live provider FX sourcing
and external-GL posting are outside this implementation. Browser and WPF use shared contracts and
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

## Verification record (2026-11-14)

This records development verification for `W12-ICAGREE-001`, `W12-ENTITY-001`, `W12-ICPAIR-001`
and `W12-ICINCOME-001` in PR #3591. All fixtures are synthetic. It is not operator acceptance, a
live-provider result or a release decision.

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
| Shared workstation consumers | `IntercompanyAgreementWorkspaceTests`, `IntercompanyCounterpartQueueWorkspaceTests`, `WorkstationIntercompanyAgreementApiClientTests` | In-memory |
| | `IntercompanyNativeControlSmokeTests`, `EntityConsolidationViewModelTests`, `EntityConsolidationLifecycleViewModelTests` | Windows |

### Defect found and fixed

An accounting arrangement that the tenant registry no longer returned raised an unhandled
`KeyNotFoundException`. It aborted the whole claimed delivery batch and failed the aged-break
list. It now fails only that proposal and is classified `AgreementAuthorityUnavailable`.

### Limits of this verification

- Three- and ten-entity populations are proven for `w10-v2` in memory. Translated (`w10-v3`,
  `w10-v4`) and PostgreSQL-backed populations of that size are not.
- Inside the delivery service, repricing preview/apply and the aged-break list check the tenant
  of the book owner, not the company; the HTTP endpoints check both before calling it. Delivery
  and linking check both in the service.
- In-memory delivery tests use a recording stand-in for agreement journal validation; real
  ledger authority is exercised only by the PostgreSQL tests.
- Guarantee posting, indirect or partial ownership, investment/equity elimination, live FX
  sourcing and external-GL posting remain out of scope, as stated above.
