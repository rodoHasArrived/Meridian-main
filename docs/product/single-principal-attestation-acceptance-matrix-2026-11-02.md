# Single-principal attestation: synthetic acceptance matrix

**Status:** acceptance design with bounded implementation evidence; full matrix not accepted
**Owner:** product owner; independent Security and Accounting review pending  
**Reviewed:** 2026-11-12
**Review scope:** historical November 2 baseline and November 12 bounded implementation reconciliation
**Policy:** [W14 decision proposal](single-principal-attestation-policy-proposal-2026-11-02.md)

## 1. Scope and evidence meaning

This matrix specifies required behavior; it does not itself grant authority, activate controls
or adopt a financial metric. Test names are source anchors, not execution results. Section 6
reconciles the current bounded implementation with the historical November 2 test design.
At the November 2 baseline, all proposed runtime rows had execution status
**not run — mechanism not implemented**. The owner's November 12 authorization to develop a
default-off B mechanism and subsequent option-2 C development choice are recorded in the
[dated policy disposition](single-principal-attestation-policy-proposal-2026-11-02.md#12-development-disposition--2026-11-12).
That choice does not retrospectively execute this matrix or authorize live activation.
Report actual implementation tests with their execution results and scope; do not infer full
matrix acceptance from a passing subset.

The final baseline is `main` at `a822baa3a46a031a4173be987a707972911cd07e`, which merged PR #3358
from `6144202102fbf6546cfc51c0aa60a1e7794e5192`. Approval/evidence source is unchanged from the
initial inspected main `9b77aa05b3c4c9739eaf4d30bb0df3d2f8dab738`. B means the proposed two
non-posting actions. C meant the then-undecided economic-action option. Section 6 records the
owner's later selected development class; other C treatments remain unselected.

Use deterministic fixtures with synthetic institutions, accounts, documents, projections, dates,
actors and amounts. Do not require private accounts, live providers, a remote reviewer or a new
identity service. Distinct fixture actors model contract inputs; they do not certify a supported
multi-user deployment or prove real human independence.

For each runtime row, retain the command/result, exact target and declaration versions, durable
before/after state, relevant audit/receipt records, and observable downstream effect. For denials,
assert no successful approval/attestation, financial effect, hidden workflow advancement or
misleading coverage contribution; an ordinary denial audit event is permitted. Repeat critical
authority cases through the shared service and each applicable persistent store, not only a UI
button. Browser/WPF cases consume the same retained result.

## 2. Common synthetic fixtures

| Fixture | Definition |
| --- | --- |
| Scope A | Synthetic tenant `tenant-alpha`, company `company-alpha`, one book, one open period and one functional currency; matches ADR-019's single-operator company/node boundary. |
| Foreign scopes | Separate fixture tenants/companies/books/periods for negative isolation tests; not an added supported deployment topology. |
| Human P | Authenticated preparer, bound to the declaration. Separately configure declaration-administration and each proposed domain attestation grant. No implicit Admin/Controller/posting grants. |
| Human Q / alias P2 | Q models a legitimately distinct reviewer in an ordinary contract test. P2 models a second account controlled by P; differing IDs alone never justify a new independent-review claim. |
| Automation S | Authenticated service/API-key origin or assistant-produced command; a human name in the payload cannot change trusted origin. |
| Target T7 | Exact preparer-owned review task/assertion or non-postable preview, stable scope and hash, bound retained source/version and complete support. External source creator/importer is separate. T8 changes a relevant operand; T7's receipt cannot cover T8. |
| Declaration D3 | Proposed enabled declaration for P/Scope A, adopted test policy revision and exact allowlist. D4 is a withdrawal; D5 is later re-enablement. These are fixture states, not current product records. |
| Policy B1 | Test version containing only `ATTEST-EVIDENCE-ACK` and `ATTEST-DRAFT-CHECK`; all excluded and unknown effects denied. This historical fixture is distinct from the actual versioned v1/v2 B policies recorded in Section 6. |
| Durable observations | Target state/version, journal/lot/approval counts, audit/receipt IDs, manifest bytes/hash, declaration revision and one frozen reporting population. |

## 3. Runtime acceptance scenarios

### A. Disabled-mode and ordinary-path regression

Run A01–A10 with declaration absent and disabled. Where applicable, repeat the ordinary route
with declaration enabled: a declaration must not alter that route's semantics. Missing/unreadable
new declaration state denies the new attestation path and must not invent a different ordinary
approval policy.

| ID | Setup and attempted action | Required result / observation | Existing regression seam |
| --- | --- | --- | --- |
| A01 | CA projection prepared by P; P requests ordinary approval, including trimmed/case variants | `MakerCheckerRequired`; no approval, revision advance or posting. | CA policy/service tests |
| A02 | CA posting without approval, with voided/stale approval, by a different poster than approver, or by the preparer | Existing typed refusal for each condition; no durable journal or lot mutation. | CA policy/service/PostgreSQL tests |
| A03 | Otherwise valid ordinary CA approval/posting with fixture Q, exact scope/current projection, separate grants and open period | Existing authorized path retains its result; declaration state neither grants nor removes ordinary authority. This is synthetic contract evidence only. | CA policy/service/PostgreSQL tests |
| A04 | P supplies P as approval actor for Waive or Supersede | `SelfApprovalNotAllowed`; no disposition change. Parameterize both actions. | Reconciliation repository tests |
| A05 | Nonmaterial waiver/supersession omits approval actor/reference, or generic status transition tries to bypass governed casework | Existing MissingApproval/governed-transition refusal, even at low or zero amount. | Reconciliation repository tests |
| A06 | P resolved case; P attempts SignOff without privilege or without reason, then with both trusted privilege and reason | Ordinary attempts return `ResolverSignerConflict`; the existing privileged case retains its existing permitted outcome. No fabricated W14 receipt or independent-review label. | Reconciliation repository tests |
| A07 | P stages a Security Master override and requests new approval as P, including case/whitespace variants | `OperatorOverrideSelfApprovalException` / existing endpoint refusal; pending values unchanged. | Override rule/store/endpoint tests |
| A08 | P rejects their own staged override; internal compensation restores a genuinely prior decision | Preserve existing permitted rejection/restoration behavior and audit. Neither asserts new review nor becomes W14 attestation; public requests cannot set internal restoration authority. | Override rule/store tests |
| A09 | Proposed attestation path has absent/disabled/malformed/unreadable declaration | New path denies authority and states the blocker; existing ordinary routes retain their exact behavior. No new fallback self-approval. | Future W14 service tests plus existing ordinary-route regressions |
| A10 | Otherwise valid legacy reconciliation command supplies different actor IDs and an opaque nonblank ApprovalReference | Opaque legacy text does not authenticate a second human; the November 2 baseline also lacked typed attestation refusal. Section 6 records the later connected-consumer boundary, without rewriting or upgrading historical opaque references. | Reconciliation adapter/repository source; future provenance tests |

### B. Enabled-mode authorization and eligible effects

| ID | Setup and attempted action | Required result / observation |
| --- | --- | --- |
| B01 | P has explicit domain grant; D3/B1 current; acknowledge exact retained evidence under `ATTEST-EVIDENCE-ACK` | Append one separate preparer receipt and audit event. Retain disclosure, scope, target/hash, actor, reason and versions; no ordinary source approval, break resolution or accounting effect. |
| B02 | Same authority; attest T7 under `ATTEST-DRAFT-CHECK` | Complete only the preparer-check task; case, controlled close task, posting and release states remain unchanged. An outstanding independent approval requirement remains visible. |
| B03 | Declaration exists but policy unadopted, action not allowlisted, or no domain attestation grant | Deny. No capability is inherited from declaration existence or ordinary generic Approve permission. |
| B04 | Technical Admin enables declaration but lacks domain/posting grant; Controller has ordinary approval but lacks the proposed attestation or posting grant | Only separately authorized operations are possible; enablement/Controller status cannot confer missing business authority. |
| B05 | Request crosses tenant, company, book, period, bound preparer or target identity; include bulk and indirect service paths | Deny before mutation. Server scope and exact subject prevail over client fields. |
| B06 | Client forges actor, human origin, privilege, independent-review type, low-risk class, source provenance, amount, authority flag or policy/declaration version | Recompute trusted facts or reject; no client value grants authority or upgrades assurance. |
| B07 | Automation S or an assistant submits a human-looking rationale/actor | Cannot declare or attest as a human. A separate authorized human confirmation is required; preserve assistance origin. |
| B08 | P2 has a different ID but is controlled by P; or ownership/independence evidence is unavailable | The new receipt/projection cannot claim independent human review merely from unequal IDs. Preserve the observed limitations of ordinary legacy routes; this test must not pretend they already authenticate a second human. |
| B09 | Target, evidence, scope, policy or declaration changes after preview (T7 to T8 or policy/declaration revision advances) | Reject stale confirmation; require fresh evaluation. No receipt over changed operands. |
| B10 | Required evidence absent, stale, conflicted, foreign, corrupt or unreadable; required audit/retention persistence unavailable | Deny successful attestation and effect. Show unavailable support; do not synthesize proof or advance because a rationale exists. |
| B11 | Unknown/new action ID, generic base-class approval or a mixed eligible/excluded bulk request | Default-deny; first-slice batch is atomic, so no member advances. Independent resubmission still evaluates each item and aggregate limits if C is adopted. |
| B12 | P owns a review task over a provider-created/system-imported artifact; repeat with another human's task | P may attest their own eligible task without changing external creator/importer provenance. The other task cannot be attested under P's declaration. |
| B13 | New typed W14 receipt is supplied to an ordinary authority consumer as ApprovalId, ApprovalActor/reference or Approved state | Reject the receipt as ordinary approval; unsupported consumers cannot be connected until this boundary is resolved. Section 6 links the later connected-consumer implementation; opaque legacy text remains unauthenticated. |

### C. Excluded activities remain unavailable through attestation

Run every C01–C09 case with D3 enabled, P possessing declaration-administration authority and a
valid B attestation grant, and otherwise complete evidence. The assertion is that **attestation
cannot authorize the excluded effect**. An independently authorized ordinary operation retains
its own existing controls; this matrix does not remove it.

| ID | Attempted excluded effect | Required result / observation |
| --- | --- | --- |
| C01 | CA or other journal posting, correction, reversal/rebook, lot mutation, economic-term override or voluntary election under B | Deny all such W14 effects; no journal, lot, override or approval state change. A non-postable draft may remain readable. |
| C02 | Material/high-risk/unknown-exposure action, unsupported treatment or uncertain source, including zero-dollar qualitative risk | Deny; a small amount or operator assertion cannot select an eligible class. |
| C03 | Waive/Supersede reconciliation, suppress an above-tolerance break or mark its downstream output ready | Deny attestation-dependent transition; no hidden removal of the blocker or increased Verified Coverage. |
| C04 | Period unlock/reopen, forced close, restatement authorization, tolerance change or accounting/mapping/policy override | Deny; original controls and period state remain intact. |
| C05 | Payment/transfer, beneficiary change, live order/amendment, strategy promotion, risk override or external posting | Deny through W14; no dispatch, external state change or authority envelope. |
| C06 | Role/scope/ownership grant, connection takeover, authentication recovery, credential/vault migration, rotation or rollback | Deny through W14; existing security-specific authority remains necessary. |
| C07 | Report approval/certification/publication/delivery, release signing, deployment or production certification | Deny through W14; preparer receipt is not a release/signoff credential. Read-only access remains separately authorized. |
| C08 | Delete/overwrite source, audit or receipt; shorten retention; remove legal hold; rewrite historical reviewer label | Deny; retained content and policy history remain unchanged. |
| C09 | Reuse `Privileged`, `RestoresPriorDecision`, Approved status or fake independent ApprovalId to invoke an excluded action | No new W14 authority. Existing internal compensation remains limited to its real prior decision; no new review is fabricated. |
| C10 | C-only future policy: two related actions of 600 and 500 with a hypothetical cumulative cap of 1,000; use mixed positive/negative signs and split command IDs | Aggregate absolute impact is 1,100 and is ineligible. This cap is test-only, not a proposed business threshold. Retries count once; splitting or netting cannot qualify. |
| C11 | C-only future policy: threshold/FX/valuation/related population missing, qualitative exclusion present, or amount at an undefined boundary | Deny until adopted policy defines all operands and inclusive/exclusive boundary. Do not invent a tolerance or default threshold. |

### D. Withdrawal, persistence and historical proof

| ID | Sequence | Required result / observation |
| --- | --- | --- |
| D01 | Preview under D3; D4 withdrawal commits; submit P's attestation for a pre-existing T7 | Refuse new transition; no successful receipt/effect. Record creation before withdrawal is irrelevant. |
| D02 | Two instances race withdrawal and attestation; test each commit order | Exactly the durable ordering applies: withdrawal first denies; attestation first preserves its receipt, then withdrawal prevents new effects. No half-committed receipt/state. |
| D03 | A command completes before withdrawal; identical retry after withdrawal/restart | Return original historical receipt under current read permission; no duplicate state/audit success/effect. Changed payload with same command ID conflicts. |
| D04 | D3 is withdrawn, then D5 enabled; replay an uncommitted D3 command | Refuse stale declaration revision; re-enablement does not resurrect old authority. |
| D05 | Fail/crash before or between receipt, audit and effect persistence; restart/recover | No authoritative success without required receipt/audit. Recovery settles one durable outcome, preserves idempotency and never authorizes from incomplete files. |
| D06 | T8 changes reviewed operands, support is corrupted, or P revokes an erroneous T7 attestation | Append supersession/revocation/correction evidence; old receipt retained, current applicability reevaluated. No in-place history edit or automatic financial reversal. |
| D07 | Reopen a frozen package after D4 or later independent approval of the exact revision | Original bytes/hash/attestation disclosure and dated coverage assessment reproduce. Later review is a separate dated decision, never a backdated independent label. |
| D08 | Use old proof to request a new downstream transition, package publication or delivery after D4 | Current ordinary authorization and dependency checks still apply; old evidence/reproduction cannot supply new W14 authority. |
| D09 | Withdrawal with retention/legal hold active, missing historical bytes, or revoked viewer access | Retention/hold survive. Missing bytes report unavailable proof; access denial still applies. Do not delete, reconstruct or redistribute based on past visibility. |
| D10 | After D4, the original attester with required record-modification permission (or explicitly granted same-scope governance actor) revokes a receipt; repeat with reader/wrong actor/foreign scope | Authorized reasoned revocation checks receipt revision and succeeds without re-enabling declaration; original receipt/package unchanged, current support excludes it. Unauthorized attempts fail; no new affirmative authority is granted. |

### E. Browser, WPF and export semantics

| ID | Surface condition | Required result / observation |
| --- | --- | --- |
| E01 | Same retained receipt in browser and WPF review queue, Number Passport/amount proof, trace and audit view | Same classification and disclosure, alongside evidence and workflow state. Both consume shared authority; no client-derived eligibility. |
| E02 | Mixed package containing ordinary-approved amounts and B evidence acknowledgments; optionally later adopted amount-level attestations | B summary says "contains preparer-attested evidence" with receipt/task counts, not attested amount values. Only an adopted amount-level class supplies attested leaf value/count. Preserve affected labels; no blanket independent-review claim. |
| E03 | Export/reopen historical package after withdrawal, revocation or later review | Preserve original disclosure, receipt/policy versions and snapshot. Current-status notice is visibly separate from frozen content. |
| E04 | Legacy Approved status, unknown decision type/version, unsupported old client or Ready evidence without approval | No inferred independence or authority. Display known facts plus unknown/legacy classification; unknown typed decisions cannot satisfy a new approval requirement. |
| E05 | Synthetic attested source shown with Ready support | Show both synthetic provenance and preparer-attestation disclosure. Neither is a live-verification or independent-review badge. |

### F. Reporting and coverage

| ID | Population or assessment | Required result / observation |
| --- | --- | --- |
| F01 | Hypothetical six-leaf policy oracle in Section 4, conditional on an adopted amount-level class under C or a separately reviewed definition | Verified value 50%; count 33.33%; separate supported-attested value 20.83%; count 33.33%. Not a B outcome; do not combine them as unchanged Verified Coverage. |
| F02 | Run those six rows as actual seeded demo/fixture-derived records | Real Verified Coverage and real supported-attested numerators are both zero because all six are synthetic. Any policy-oracle demonstration stays separately labeled; no provenance promotion. |
| F03 | Repeated report line, parent total, duplicate receipt or command retry | Count each canonical leaf once; denominator and amount unchanged. A receipt is not a financial assertion. |
| F04 | Negative liability or offsetting asset/liability | Use absolute amounts; include both sides. Show asset/liability breakdowns; no netting to inflate coverage. |
| F05 | Zero value denominator, zero-valued leaves, empty count population | Value is N/A for zero denominator; zero-valued leaves still count. Empty count denominator is N/A, never 100%. |
| F06 | Unknown valuation/FX or incomplete inventory | Relevant result provisional/unavailable, missing population visible; do not drop items or claim completeness. |
| F07 | Attestation exists but reconciliation fails, support is missing/stale/foreign or receipt was revoked/superseded | Record remains in history and denominator; it cannot increase verified or currently supported-attested numerator. Ordinary Ready alone is insufficient. |
| F08 | Genuine later ordinary approval satisfies every required approval dependency for an amount-level attested assertion | Current disjoint category moves from attested-only to ordinary-approved; one leaf, no double counting. Report-level approval alone cannot cure an upstream decision. Frozen prior package/metric unchanged. |
| F09 | Declaration withdrawn while existing receipt/target/support remain unchanged | New authority is unavailable, but historical support is preserved and disclosed. Withdrawal alone is not a revocation; current supplemental treatment follows the adopted prospective rule. |
| F10 | Proof-chain builder reports 100% layer coverage, or one dependency of a composite assertion is attested-only/unapproved | Layer coverage does not supply financial approval. The affected assertion cannot become verified without all its required predicates. |
| F11 | B evidence acknowledgment references one source used by two financial assertions | Record one task receipt; do not automatically classify either financial assertion as attested or count its amount in a supported-attested numerator. |

## 4. Coverage arithmetic oracle

This hypothetical economic calculation is conditional on the owner adopting an amount-level
attestation class under C or a separately reviewed definition. B's evidence/task receipts do not
qualify financial amounts for this calculation. It is expressed entirely with synthetic numbers. A–E model
non-simulated source classifications; F models simulated provenance. That modeling assumption
is useful for a unit-level predicate test, but it must never be applied to label actual demo or
fixture-derived records as non-simulated. In a real seeded acceptance run all six remain synthetic
and contribute **zero** to the real Verified Coverage and real supported-attested numerators (F02).

Amounts use one reporting currency, period and valuation basis. Each row is a distinct leaf;
all rows meet the other retained-support/reconciliation requirements unless noted.
For this fixture A, C, E and F are assets, and B and D are liabilities; the classification is
explicit rather than inferred from amount sign. Each what-if below starts independently from
this original snapshot, not from the outcome of the preceding what-if.

| Leaf | Signed amount | Modeled source class | Decision classification | Ordinary verified numerator | Supported-attested-only numerator |
| --- | ---: | --- | --- | ---: | ---: |
| A | 400 | Non-simulated, hypothetical | Valid ordinary approval under applicable policy | 400 | 0 |
| B | -200 | Non-simulated, hypothetical | Valid ordinary approval under applicable policy | 200 | 0 |
| C | 150 | Non-simulated, hypothetical | Valid preparer attestation; no ordinary approval | 0 | 150 |
| D | -100 | Non-simulated, hypothetical | Valid preparer attestation; no ordinary approval | 0 | 100 |
| E | 150 | Non-simulated, hypothetical | No approval or attestation | 0 | 0 |
| F | 200 | Simulated | Otherwise complete support | 0 | 0 |
| **Absolute-value total** | **1,200** | **6 leaves** | | **600** | **250** |

- Verified value: 600 / 1,200 = **50.00%**; verified count: 2 / 6 = **33.33%**.
- Supported-attested-only value: 250 / 1,200 = **20.83%**; count: 2 / 6 = **33.33%**.
- Assets: 400 / 900 = **44.44%** verified by value; 1 / 4 = **25.00%** by count.
- Liabilities: 200 / 300 = **66.67%** verified by value; 1 / 2 = **50.00%** by count.
- F and E remain in the denominator. Signed net 600 is not the denominator.
- A combined 850 / 1,200 = 70.83% is **not** the unchanged Verified Coverage headline.
- If D loses support, the current supported-attested numerator becomes 150 / 1,200 = **12.50%**
  and 1 / 6 = **16.67%**; its historical receipt remains visible.
- If C later has a valid ordinary approval and all other predicates remain satisfied, current
  verified value becomes 750 / 1,200 = **62.50%**, count 3 / 6 = **50.00%**; attested-only value
  becomes 100 / 1,200 = **8.33%**, count 1 / 6 = **16.67%**. The original snapshot stays unchanged.

These are expected calculations, not evidence of a running W11 metric. Rounding above is for
display; retain exact amounts/counts and policy-defined precision in any future implementation.

## 5. Existing regression anchors and implementation sequencing

| Existing test/source anchor | Coverage to retain or extend after policy adoption |
| --- | --- |
| [CorporateActionCaseAccountingPolicyTests](../../tests/Meridian.Tests/SecurityMaster/CorporateActionCaseAccountingPolicyTests.cs) | Same-preparer refusal, current/voided approval, poster identity, exact scope, projection, balance, policy and lot guards; positive ordinary path. |
| [CorporateActionCaseAccountingServiceTests](../../tests/Meridian.Tests/SecurityMaster/CorporateActionCaseAccountingServiceTests.cs) and [PostgreSQL round trips](../../tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.cs) | Application refusal before mutation and durable review/posting behavior. |
| [ReconciliationBreakQueueRepositoryTests](../../tests/Meridian.Tests/Strategies/ReconciliationBreakQueueRepositoryTests.cs) | Waive/Supersede approval including nonmaterial cases; same-signer denial and privileged-with-reason acceptance; generic-transition bypass refusal. |
| [OperatorOverrideReviewRulesTests](../../tests/Meridian.Tests/SecurityMaster/OperatorOverrideReviewRulesTests.cs), [PostgreSQL governance events](../../tests/Meridian.Tests/SecurityMaster/SecurityMasterGovernanceEventsPostgresTests.cs) and [endpoint tests](../../tests/Meridian.Tests/Ui/SecurityMasterOperatorOverrideDecisionEndpointsTests.cs) | Case-insensitive/trimmed self-approval denial; ordinary other-actor decision; self-rejection and real prior-decision restoration; retained actor/evidence. |
| [PostedLedgerAmountProvenanceTests](../../tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs) and [ReportLedgerAmountProvenanceTests](../../tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs) | Exact scope and retained bytes; missing/stale/ambiguous/foreign evidence; historical report population. |
| [WPF amount-proof interactions](../../tests/Meridian.Wpf.Tests/ViewModels/LedgerAmountProofInteractionTests.cs) and the browser proof-drawer/number-passport tests | Shared semantic label, scope, unknown classification and historical/current distinction; evidence readiness remains separate. |
| [ReportingSecureDistributionTests](../../tests/Meridian.Tests/Reporting/ReportingSecureDistributionTests.cs) | Exact released artifact bytes, corruption refusal, and current publication/delivery authority. |

The historical sequence called for policy decisions before implementation. The November 12
owner instruction supersedes that proposed development sequencing for the bounded choices in
Section 6. Preserve group A, then add the
smallest B receipt path and exclusions. Add withdrawal/restart/race tests at the existing persistent
store boundaries before accepting the mechanism, then exercise shared browser/WPF/export views.
Implement C-specific authority only if the owner selects it with reviewed accounting boundaries;
otherwise additional C classes stay conditional planning cases. C10–C11 retain their historical
hypothetical arithmetic; Section 6 gives the actual C2 thresholds and selected treatment.

No new general-purpose testing framework or CI lane is required. Use current test projects and
the narrowest applicable checks, then the repository's required hosted checks. Independent human
Security and Accounting review remains outstanding. Passing synthetic tests would demonstrate
the stated synthetic behavior, not live-provider validation, policy adoption, W14 completion,
production certification or permission to merge.


## 6. November 12 implementation reconciliation

The owner authorized implementation and selected **development option 2** in the same session:
regular mandatory USD dividend **recognition on source ex-date**, with a complete-related-event
gross cap of **$10,000** and selected-book/source-ex-date-calendar-month gross cap of
**$100,000**, both inclusive. Debit the explicitly bound existing dividend-receivable account;
credit the explicitly bound existing dividend-income revenue account. Do not book cash settlement.
The binding names existing chart paths/accounts and an already promoted rule/version; W14 does
not create accounts, mappings or policy. The
[dated policy disposition](single-principal-attestation-policy-proposal-2026-11-02.md#12-development-disposition--2026-11-12)
is the current closed development boundary.

This supersedes the earlier “C undecided” development posture, not the November 2 observation
record or the proposed financial arithmetic oracle. B remains non-posting. C is a separate
native lane with its own default-off host gate, declaration, business grants, full entitlement
and retained posting authority. No new class inherits eligibility.

### Source and regression anchors

| Boundary / matrix group | Implemented candidate and focused regression anchors | Evidence limit |
| --- | --- | --- |
| B acknowledgment; A/B/D | [Store tests](../../tests/Meridian.Tests/Ui/SinglePrincipalAttestationStoreTests.cs), [additional tests](../../tests/Meridian.Tests/Ui/SinglePrincipalAttestationAdditionalTests.cs), [endpoint tests](../../tests/Meridian.Tests/Ui/SinglePrincipalAttestationEndpointTests.cs) and [resolver tests](../../tests/Meridian.Tests/Ui/Evidence/EvidenceAttestationSubjectResolverTests.cs) | Exact source bytes, scope, human/grants, declarations, receipts, audit, withdrawal/revocation and retry; B supplies no accounting or independent-review authority. |
| B Security non-postable draft; B02/B05/B09 | [Draft policy tests](../../tests/Meridian.Tests/Ui/SinglePrincipalDraftAttestationTests.cs) and [retained-run binding tests](../../tests/Meridian.Tests/Ui/ContractualCashFlowAttestationBindingEndpointTests.cs) | New server-bound actual-preparer Security runs only; old/unbound runs and loans remain ineligible. Explicit v2 reaffirmation preserves old v1 state and bytes without automatically upgrading authority. |
| B13 and ordinary authority boundaries | [Reserved-reference tests](../../tests/Meridian.Tests/Ui/PreparerAttestationApprovalBoundaryTests.cs), [consumer tests](../../tests/Meridian.Tests/Ui/PreparerAttestationOrdinaryReferenceConsumerTests.cs) and [native posting contracts](../../tests/Meridian.Tests/FinancialOperations/Ledger/CashDividendPreparerPostingContractTests.cs) | Connected consumers reject preparer receipt authority. Opaque legacy text still does not authenticate a second human; different account IDs are not independent-review proof. |
| B proof/export/history; D/E/F11 | [Evidence projection tests](../../tests/Meridian.Tests/Ui/PreparerAttestationEvidenceProjectionTests.cs), [original-proof export](../../src/Meridian.Ui.Shared/Evidence/SinglePrincipalAttestationStore.Export.cs), [browser evidence component tests](../../src/Meridian.Ui/dashboard/src/components/meridian/preparer-attestation-evidence.test.tsx) | Exact visible source association and frozen original receipt/declaration/assessment; evidence-task counts are separate from financial amount/value counts and source readiness. This does not certify a released financial report package. |
| C case and complete-book eligibility | [C application tests](../../tests/Meridian.Tests/SecurityMaster/CorporateActionCaseAccountingServiceCashDividendTests.cs), [eligibility tests](../../tests/Meridian.Tests/SecurityMaster/CorporateActionCashDividendEligibilityResolverTests.cs), [C endpoints](../../tests/Meridian.Tests/Ui/CorporateActionCashDividendAttestationEndpointTests.cs) and [reconciliation support tests](../../tests/Meridian.Tests/Ui/CorporateActionCashDividendReconciliationSupportTests.cs) | Closed policy, trusted scope/origin/grants, exact original preparer and operands, account/rule binding, reconciliation support and qualitative exclusions. |
| C durable budgets/native posting | [PostgreSQL C round trips](../../tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.CashDividend.cs), [native contract tests](../../tests/Meridian.Tests/FinancialOperations/Ledger/CashDividendPreparerPostingContractTests.cs) and [migration 066](../../src/Meridian.Storage/SecurityMaster/Migrations/066_corporate_action_cash_dividend_attestations.sql) | Native transaction, exact journal/event spine, source-alias/event/month budget locking, retained reservations, replay and rollback require real PostgreSQL execution. A mocked service result alone is insufficient evidence for this row. |
| Native accounting amount and frozen report disclosure; E/F08/F10 | [Posted proof tests](../../tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs), [frozen report proof tests](../../tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs), [shared amount contract](../../src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs) | Typed immutable binding is validated against actual scope, event, journal gross, actor, USD currency, fingerprints and explicit account/rule context. Invalid/unknown classification cannot establish ordinary approval. Source readiness is unchanged. Frozen native disclosure is retained even when source support fails; no current database substitution or new financial denominator. |
| Browser interactions; E | [ACK tests](../../src/Meridian.Ui/dashboard/src/components/meridian/single-principal-attestation-panel.test.tsx), [draft tests](../../src/Meridian.Ui/dashboard/src/components/meridian/preparer-draft-check-panel.test.tsx), [C panel tests](../../src/Meridian.Ui/dashboard/src/components/meridian/cash-dividend-attestation-panel.test.tsx), Number Passport and proof drawer | Focused tests and isolated actual-component Chromium interaction checks use synthetic transport. They do not establish a full-backend browser workflow or live-provider compatibility. |
| WPF actions and proof; E | [ACK action tests](../../tests/Meridian.Wpf.Tests/ViewModels/EvidenceWorkbenchAttestationActionTests.cs), [draft view model](../../src/Meridian.Wpf/ViewModels/CashFlows/PreparerDraftCheckViewModel.cs), [C view-model tests](../../tests/Meridian.Wpf.Tests/ViewModels/CashDividendAttestationViewModelTests.cs), [amount presentation](../../src/Meridian.Wpf/Workstation/Models/AccountingPreparerAttestationPresentation.cs) | Shared ACK, retained Security draft and native C controls exist in source. View-model/cross-compilation evidence remains distinct from a native Windows runtime run; these links do not assert complete native Windows interactions. |

These links identify implementation and test source. They do not assign execution results to all
original matrix rows. Isolated actual-component browser checks have exercised canonical
disclosures, exact confirmation, uncertain retry, withdrawal/revocation and original B proof
bytes using synthetic transport. Current-candidate .NET, PostgreSQL, WPF, complete browser build
and hosted check results must be recorded by the execution that ran them; no passing counts,
reviewed SHA or full-matrix acceptance is inferred here.

For a bounded rerun, use the existing `Meridian.Tests` project with a `FullyQualifiedName`
filter selecting `SinglePrincipal`, `PreparerAttestation`, `CashDividend`,
`PostedLedgerAmountProvenanceTests` and `ReportLedgerAmountProvenanceTests`; run the
`CorporateActionAccountingPostgresRoundTripTests` against actual configured PostgreSQL for
native commit, budget, rollback and replay evidence. Run the named browser files through the
existing Vitest runner and TypeScript checks. Use the existing WPF test project and Windows
targeting build; record a native Windows UI run separately. These are validation directions,
not an execution record or a replacement for required hosted checks.

B grant regressions must distinguish ACK (`RecordPreparerAttestations` plus `ManageReporting`)
from Draft (`RecordPreparerAttestations` plus `ModifySecurityMaster`) and retain current read
authority. Authorized own corrective revocation still needs the applicable domain-modify grant,
but must survive loss of the fresh Record grant, declaration withdrawal or feature disablement.
It cannot remove or rewrite the original receipt.

### C2 acceptance cases for the selected development policy

The C10/C11 examples above preserve the earlier hypothetical arithmetic tests. The actual
development policy additionally requires the following closed-class observations:

| ID | Setup / attempted effect | Required result |
| --- | --- | --- |
| C2-01 | Host gate absent/off; declaration absent/withdrawn/stale; missing human/domain/posting grant | New C authority unavailable; no journal or hidden case advancement. Ordinary approval behavior retains its guards. |
| C2-02 | Confirmed regular USD declaration, selected USD book/open period, exact original preparer, current source/ex-date/projection and complete supported entitlement | Attest as preparer only, then separately post the exact ex-date receivable debit/revenue credit through the native boundary; no ordinary ApprovalId, independent-review claim, cash settlement or lot effect. |
| C2-03 | Complete related-event gross exactly $10,000, then above $10,000; positive and negative offsets or split account cases | The exact boundary may qualify only when every other check passes. Above-limit, netted, negative or incomplete entitlement cannot qualify. Related security/ex-date cases aggregate across the selected book. |
| C2-04 | Selected book's ex-date-calendar-month gross exactly $100,000, then above $100,000; concurrent attestations | Reservation and commit serialization enforce the inclusive cap. Posting date, another source alias or split command ID cannot move the event into a new bucket. |
| C2-05 | Exact command retry, uncertain response, restart, withdrawal, void or abandoned posting | Preserve receipt, original operands and reservation; exact retry counts once. Uncertain or voided decisions cannot silently free budget for another economic event. No new posting authority from historical proof. |
| C2-06 | Another tenant/company/book/period/principal; changed source identity/version/hash, ex-date, complete entitlement, policy, declaration, projection or Drafted fingerprints | Refuse stale/foreign/aliased operands before mutation. Require a fresh exact preview; no client amount or authority claim substitutes. |
| C2-07 | Missing/unaccepted source or reconciliation support; missing/incoherent/future record date; ambiguous dividend/special-dividend source peers; stale source; changed lot identities or quantities from the pre-ex cutoff through completed record date; unverified complete holdings, unknown valuation, short/negative entitlement, FX, election, correction or specialist treatment | Closed-class refusal even below the monetary cap; the receipt cannot cure support or accounting uncertainty or infer settlement/due-bill entitlement. |
| C2-08 | Missing or changed chart account/path/name/type, promoted rule/version, bound book or balanced two-line journal | Refuse. Existing account/rule identity must match the retained binding; no auto-created account, inferred role or alternate posting template. |
| C2-09 | Generic append or ordinary approval receives a B/C receipt, tagged `PreparerAttested` value or fabricated independent ApprovalId without native lease | Refuse ordinary/generic authority. Only the exact native C transaction may consume the valid C decision; no string label grants it. |
| C2-10 | Posting persistence/audit/spine failure, locked/stale period or changed native command after confirmation | Roll back the authoritative effect; no orphan successful journal/spine/case result. Preserve explicit uncertainty and retry identity where applicable. |
| C2-11 | Read posted amount/frozen report after source bytes disappear, declaration withdrawal or mutable current data changes | Preserve compact original native classification from retained journal metadata. Source readiness remains independently blocked/review-required; no replacement from mutable case/journal reads. |
| C2-12 | Corrupt/foreign/unknown typed binding, empty receipt ID, unsupported policy or conflicting duplicate receipt IDs | Show Unknown/unavailable classification without foreign receipt operands or an exception-derived success. Never infer ordinary/independent approval. |
| C2-13 | Generated report amount has any preparer-attested or unknown upstream dependency; report itself later receives ordinary approval | Keep upstream ordinary-approval satisfaction false. Report approval cannot cure that dependency; receipt union deduplicates exact native decisions and does not invent financial leaves or coverage arithmetic. |
| C2-14 | Compare browser/WPF/native proof and exports under the selected source/command revisions | Same canonical preparer disclosure and exact native identifiers; current notices remain separate from retained original proof. Record native Windows runtime evidence separately from mocked or compiled checks. |

W14 remains **in progress**, not accepted: complete matrix execution, native Windows runtime
evidence, full both-workstation validation, operational review and W11 financial-coverage adoption
are outstanding. All development demonstrations remain synthetic and all host gates default off.
