# Single-principal attestation: synthetic acceptance matrix

**Status:** draft acceptance design; runtime scenarios not implemented or executed  
**Owner:** product owner; independent Security and Accounting review pending  
**Reviewed:** 2026-11-02  
**Review scope:** source/test inspection and policy arithmetic only  
**Policy:** [W14 decision proposal](single-principal-attestation-policy-proposal-2026-11-02.md)

## 1. Scope and evidence meaning

This matrix specifies what a later implementation must demonstrate. It does not introduce an
attestation mechanism, new approval authority, deployed controls or a changed metric. Existing
test names below are source anchors, not claims of execution in this documentation change.
All proposed runtime rows have execution status **not run — mechanism not implemented**.

The final baseline is `main` at `a822baa3a46a031a4173be987a707972911cd07e`, which merged PR #3358
from `6144202102fbf6546cfc51c0aa60a1e7794e5192`. Approval/evidence source is unchanged from the
initial inspected main `9b77aa05b3c4c9739eaf4d30bb0df3d2f8dab738`. B means the proposed two
non-posting actions. C means the still-undecided economic-action option. C-only scenarios do not
establish an adopted allowlist or authorize implementation before the owner decides.

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
| Policy B1 | Test version containing only `ATTEST-EVIDENCE-ACK` and `ATTEST-DRAFT-CHECK`; all excluded and unknown effects denied. Policy names are proposal identifiers, not existing code. |
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
| A10 | Otherwise valid legacy reconciliation command supplies different actor IDs and an opaque nonblank ApprovalReference | Document current reference-validation limit: the route does not authenticate a second human or reject an attestation reference by type. Do not claim this guard already supplies either guarantee; future classification cannot infer independence from success. | Reconciliation adapter/repository source; future provenance tests |

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
| B13 | New typed W14 receipt is supplied to an ordinary authority consumer as ApprovalId, ApprovalActor/reference or Approved state | Future integration must reject the receipt as ordinary approval; unsupported/legacy consumers cannot be connected until this boundary is resolved. This is a new obligation, not a claim that current string-reference validation does it. |

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

Implement only after the policy decisions are recorded. First preserve group A, then add the
smallest B receipt path and exclusions. Add withdrawal/restart/race tests at the existing persistent
store boundaries before accepting the mechanism, then exercise shared browser/WPF/export views.
Implement C-specific authority only if the owner selects it with reviewed accounting boundaries;
otherwise C10–C11 stay conditional planning cases.

No new general-purpose testing framework or CI lane is required. Use current test projects and
the narrowest applicable checks, then the repository's required hosted checks. Independent human
Security and Accounting review remains outstanding. Passing synthetic tests would demonstrate
the stated synthetic behavior, not live-provider validation, policy adoption, W14 completion,
production certification or permission to merge.
