# Single-principal attestation: security and accounting policy proposal

**Status:** draft decision proposal; not approved policy or implemented authority  
**Owner:** product owner, with independent Security and Accounting review pending  
**Reviewed:** 2026-11-02  
**Review scope:** source inspection and AI-assisted analysis only  
**Related direction:** `W14-ATTEST-001`, `DEC-SINGLE-PRINCIPAL-001`, `W11-COVER-001`, `W11-PASSPORT-001`

## 1. Decision requested

Adopt a closed, default-off attestation policy before implementing any single-principal exception.
An attestation records what the preparer asserts; it does not establish independent review or
grant another permission. The recommended first slice permits two named, non-posting evidence
actions. Economic posting remains a separate, explicit owner choice requiring a class-specific
accounting policy. This first slice alone would **not** complete W14's proposed corporate-action
posting example.

Every recommendation below is pending. Recording this proposal does not enable a declaration,
change an approval guard, authorize live operations, amend a metric formula, or satisfy a release
requirement. Until an owner decision and independent Security and Accounting review are recorded,
the eligible runtime allowlist is empty and current enforcement remains authoritative.

The associated [synthetic acceptance matrix](single-principal-attestation-acceptance-matrix-2026-11-02.md)
defines expected results and existing regression anchors. It is a test design, not a claim that
the proposed mechanism or its runtime tests exist.

### Evidence baseline

Source inspection began at `main` commit `9b77aa05b3c4c9739eaf4d30bb0df3d2f8dab738` and
[PR #3358](https://github.com/rodoHasArrived/Meridian-main/pull/3358) head
`6144202102fbf6546cfc51c0aa60a1e7794e5192`. During this review, #3358 merged into `main` at
`a822baa3a46a031a4173be987a707972911cd07e`. The comparison contains its documentation/validator
changes and no approval-guard or evidence-model source changes. This proposal is reconciled to
that merged baseline: W14 is registered as planned, and charter Version 1.7 is now on main.
Source references below are repository-relative. Recheck them if implementation starts on
another revision.

The [accepted ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md) governs the
deployment boundary: one operator, one company and one node, with local browser and WPF access.
Its Windows 11 x64/ARM64 support and PRD-000 release evidence obligations remain unchanged. The
charter's older deployment summary must not substitute for the accepted ADR. This policy
adds no remote reviewer access, identity provider, service, tenant topology, or release authority.

## 2. Current contracts and their limits

| Surface inspected | Current enforcement or evidence | Consequence for W14 |
| --- | --- | --- |
| [Corporate-action accounting policy](../../src/Meridian.Contracts/SecurityMaster/CorporateActionCaseAccountingContracts.cs), `EnsureIndependentOfPreparer` and `EnsureApprovalAuthorizesPosting` | Same preparer/approver is rejected with `MakerCheckerRequired`, using trimmed, case-insensitive actor IDs. Posting requires an active approval of the current projection; the poster must be its approver and must differ from the preparer. Scope, policy, journal balance and lot-resolution checks are separate. | Preserve the ordinary path. Do not manufacture an approval ID, substitute a different actor, or turn attestation into an ordinary approval record. |
| [Corporate-action application service](../../src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionCaseAccountingService.cs) and [HTTP adapters](../../src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs) | Approval/posting authority, actor, tenant and company are resolved server-side. Approval requires reason and retained hashed evidence; posting checks the current projection, approval and open period. | A declaration cannot supply `CanApproveAccounting` or `CanPostAccounting`; missing ordinary dependencies still block. |
| [Permissions](../../src/Meridian.Identity/Contracts/Auth/UserPermission.cs) and [role assignments](../../src/Meridian.Identity/Contracts/Auth/RolePermissions.cs) | `ApproveCorporateActionAccounting` and `PostCorporateActionAccounting` are distinct. Admin/Developer technical roles do not automatically receive corporate-action business authority; Controller approval does not automatically confer posting. | Declaration administration and domain attestation authority must be separate grants. Do not broaden existing roles through a toggle. |
| [Reconciliation casework](../../src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs), `ValidateCaseworkCommand` | `Waive` and `Supersede` require reason, evidence, approval actor/reference and different actor IDs, including nonmaterial cases; same-actor attempts return `SelfApprovalNotAllowed`. `SignOff` already permits resolver==signer with server-derived privilege and a reason; otherwise `ResolverSignerConflict` applies. | Disabled-mode parity means preserving these exact rules, including the existing privileged exception. Success in that exception is neither a new W14 attestation nor proof of independence. |
| [Reconciliation HTTP adapter](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.ReconciliationCasework.cs) | Actor/origin/privilege are trusted server values, but approval actor/reference are normalized caller-supplied retained inputs. This adapter does not authenticate a second human approval. | Identifier inequality alone is not evidence of independent human control. Record this limitation; do not silently repair or reclassify historical casework in this policy PR. |
| [Security Master override review rules](../../src/Meridian.Storage/SecurityMaster/IOperatorOverridesStore.cs) and [PostgreSQL implementation](../../src/Meridian.Storage/SecurityMaster/PostgresOperatorOverridesStore.cs) | A new self-approval throws `OperatorOverrideSelfApprovalException`; same-actor rejection remains permitted. Internal `RestoresPriorDecision` restores an earlier review and asserts no new one. Store-side review checks also apply. | Never use rejection, compensation, or `RestoresPriorDecision` as an attestation bypass. Economic-term overrides remain excluded from the proposed first slice. |
| [Endpoint identity/origin](../../src/Meridian.Ui.Shared/Endpoints/EndpointAuthorization.cs) | Actor comes from authenticated context; service/API-key origin is distinguished from a human operator. | An API key, assistant, client body or actor string cannot make a human attestation. |
| [Evidence contracts](../../src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs) | `EvidenceStatusDto` describes readiness. Document review has actor/time fields. `EvidenceDocumentAuthorityDto` cannot approve, post, certify or release. Frozen manifests, hashes, retention and legal holds are modeled separately. | Keep review classification separate from evidence readiness and downstream authority. |
| [Amount-proof contract](../../src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs), [source-support evaluation](../../src/Meridian.Ui.Shared/Services/PostedLedgerAmountProvenanceService.cs) and [report delivery models](../../src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs) | Shared proof binds scope and retained support. Accepted/Approved source-support review is not proof of independent accounting approval. Delivery models retain approval chains, publication and restatement history but have no W14 attestation type. | Add an explicit, versioned decision classification in a later implementation; do not overload existing reviewer strings or `Approved`. |
| [Proof-chain builder](../../src/Meridian.Ui.Shared/Evidence/EvidenceProofChainBuilder.cs) | Its `CoveragePercent` counts evidence-bearing proof layers; per-layer coverage is also an evidence-completeness measure. | Neither percentage is the planned financial Verified Coverage metric. |

The [accounting context](../ai/context/accounting-context.md) and
[operational evidence context](../ai/context/operational-evidence-context.md) remain applicable:
balanced journals, immutable postings, reversal/rebook corrections, exact scope, period locks,
idempotency, retained provenance and review-bound revisions cannot be waived by attestation.

## 3. Policy options and recommended boundary

| Option | Eligible result | Benefit | Limitation and disposition |
| --- | --- | --- | --- |
| A. Retain current guards; ordinary preparer notes only | Existing note/evidence functions, with no new attestation-dependent transition | No new authority; available fallback while decisions remain open | Does not resolve blocked governed workflows. Retain until policy is adopted. |
| B. Named non-posting attestation actions | A distinct preparer assertion about exact retained evidence or a non-postable draft, with no downstream approval effect | Tests identity, receipt, withdrawal and display semantics within a small scope | Recommended first slice. It leaves posting and required independent review outstanding and cannot complete W14 by itself. |
| C. Named routine economic actions | Only specifically adopted deterministic actions, with their own limits and unchanged non-review controls | Could make the charter's solo posting workflow useful | Defensible later option, but owner must choose exact action classes and accounting thresholds before implementation. Not implicitly enabled by B. |
| D. Broad self-approval with an attestation label | Any action unless a developer adds it to a denylist | Low interaction cost | Reject. Disclosure cannot replace authorization or prevent new/unclassified actions from inheriting a bypass. |

### Proposed B allowlist

These are **proposed policy identifiers**, not existing APIs, permission names or enum values.
Both require the common checks in Sections 4 and 5, an explicit human confirmation, complete
retained evidence, and a reason. A receipt may describe an error or concern, but an incomplete,
conflicted or unsupported item cannot be advanced as a successfully attested review.

| Policy identifier | Exact permitted effect | Effects it cannot authorize |
| --- | --- | --- |
| `ATTEST-EVIDENCE-ACK` | Append an assertion to the preparer's evidence-review task that they inspected a specific retained source artifact/version and its listed checks. | Cannot set the source's ordinary reviewer status to Approved/Accepted, resolve a break, satisfy accounting approval, or change source bytes or creator/importer provenance. |
| `ATTEST-DRAFT-CHECK` | Append a preparer-check receipt to an exact non-postable calculation or accounting preview revision and mark only that preparer-check task complete. | Cannot complete a controlled close task, set a case to Resolved/SignedOff, change an approved mapping/policy, post a journal, or clear a downstream blocker. |

No other operation inherits eligibility. Classification is by exact action and effect, not a
screen name, dollar amount, base class, role or generic Approve button. The initial demonstration
uses synthetic data and isolated test storage with real execution disabled. Synthetic provenance
is retained independently from the attestation label.

### Conditional C decision

If the owner requires an economic workflow in the first implementation, choose C explicitly
instead of claiming that B delivers it. Accounting must name a currently supported event and
posting treatment, its permitted accounts/currency/economic consequences, required source and
reconciliation evidence, and correction treatment. A routine mandatory cash corporate action is
an investigation candidate, **not an approved class**. Unsupported elections, reorganizations,
estimates, disputed rights and specialist treatments cannot become eligible by analogy.

Any monetary policy must define the reporting currency and FX source/date, absolute gross impact,
exposure basis, entity/book/period and event/account aggregation, qualitative exclusions and
cumulative caps. Related split transactions count together; retries count once. Zero value is
not proof of low risk. Missing thresholds, unknown valuation, absent FX, netting, stale inputs or
an incomplete related population prevent eligibility. No threshold is invented by this proposal.

## 4. Excluded activities

The following are excluded from B regardless of declaration status or administrative role.
If C is adopted, only expressly named routine economic actions may be removed from the first row;
the other exclusions remain. A change to an exclusion needs a new reviewed policy version and
explicit owner decision, not a local toggle or free-text exception.

| Excluded class | Examples and boundary |
| --- | --- |
| Economic mutations outside an adopted class | All posting under B; journal corrections, reversal/rebook, lot quantity/basis changes, elections and economic Security Master changes. Merely retaining a non-authoritative draft is different from executing it. |
| Material, high-risk, ambiguous or unclassified actions | Above-limit or cumulatively material impact; unknown exposure; disputed source; estimation or tax/accounting treatment outside the approved policy; newly introduced action IDs. A small amount cannot defeat a qualitative exclusion. |
| Exception and control overrides | Reconciliation Waive/Supersede, material break suppression, unsupported source substitution, tolerance increases, validation/mapping/policy overrides, period unlock/reopen, restatement authorization and forced close. Existing ordinary privileged sign-off behavior is preserved, not extended by W14. |
| Real-money or execution authority | Payments/transfers, beneficiary changes, live order submission/amendment, strategy promotion, risk-limit overrides and external posting. Existing permissions and release gates remain separately required. |
| Identity, secrets or ownership | Role/permission grants, scope/tenant/connection ownership changes, authentication recovery, vault migration/rotation/rollback or credential binding changes. Declaration administration is a separately authorized administrative action, not self-attested approval of these activities. |
| Certification and release | Product/production certification, signing, deployment, governed report-package approval/certification/publication/delivery or external assurance assertions. Viewing an already retained package does not authorize republishing it. |
| Destructive or historical changes | Source/audit deletion, retention shortening, legal-hold removal, overwritten receipts, changing historical review labels or destructive recovery. |
| Delegated or automated human claims | AI approval, service/API-key attestation, a second account controlled by the preparer presented as independent review, or a client-supplied classification used as authority. |

An excluded request may leave a denial audit event under ordinary logging rules; it must not leave
a successful attestation receipt that downstream readers could mistake for permission. Mixed
batches are refused atomically for the first slice; eligible items can be submitted separately
without weakening each item's checks or any cumulative materiality rule.

## 5. Authorization and declaration contract

The declaration is scoped to the authenticated tenant and company, optionally narrowed to declared
books or periods. This scope binding prevents leakage; it does not expand ADR-019 into a multi-user
or multi-company supported deployment. Never infer authority from a global process flag.

An enable, amend or withdraw action requires an authenticated interactive human, a specifically
granted declaration-administration capability, the current declaration revision, a reason, the
adopted policy version and retained audit evidence. A technical administrator may administer that
declaration only if granted this capability; they still need the separate domain grant to attest.
No role receives a new grant merely because the declaration exists. Proposed capability names and
their role assignments remain an implementation decision after policy adoption.

An attestation requires all of the following at the authoritative commit boundary:

1. The authenticated human is the bound principal and preparer of the exact review task/assertion
   or non-postable draft revision. For evidence acknowledgment, that task binds the external
   artifact/version; the source's original creator/importer is retained separately and need not be
   the preparer. Actor/origin/scope are server-derived. A client cannot replace them or designate
   an independent reviewer. Another human's task requires its ordinary workflow and evidence.
2. The declaration is enabled, current and within scope; the adopted policy version explicitly
   lists the operation and permitted effect. Unknown, missing, expired, corrupt or unreadable
   policy/declaration state supplies no attestation authority.
3. The human has a specific domain attestation grant and the existing permissions for the exact
   effect. Declaration administration, ordinary approval and posting are separate capabilities.
4. The target, evidence, policy and declaration revisions match those confirmed by the human.
   The server classifies the operation and risk, verifies evidence and rejects exclusions.
5. All ordinary non-review invariants pass. An eligible label cannot cure a stale projection,
   scope mismatch, missing evidence, locked period, imbalance, unknown source, or broken retention.
6. The receipt, audit record and permitted state change commit consistently, with a stable command
   identity. A failed evidence/audit write cannot leave a successful authoritative effect.

Before introducing W14 receipts, the relevant typed authority consumers must reject them as
ordinary approval. This is a **future integration requirement**, not an existing guarantee of
every route: reconciliation currently accepts a nonblank caller-supplied `ApprovalReference`
with actor-ID separation and does not resolve a second authenticated decision. Keep that legacy
limitation explicit; successful command execution cannot establish independent-review provenance.
Do not connect a new attestation to an authority consumer that cannot distinguish its receipt
type; resolving that specific boundary is a prerequisite for the affected implementation slice.
Do not set `Approved`, fabricate an independent `ApprovalId`, grant
`Authority.CanApproveAccounting`, or reuse `Privileged`/`RestoresPriorDecision` to route around
guards. An older reader that does not understand the decision type/version cannot treat it as
ordinary approval or grant new authority. This proposal changes no legacy endpoint behavior.

The declaration records that one human controls the scope. Distinct account IDs, names or logins
do not establish independent human control. There is no supported second-approver access path
added here. Where reliable independent-review evidence is absent, show that limitation rather
than infer independence. Strengthening legacy approval authentication is a separate scope decision.

## 6. Minimum audit and evidence receipt

Use existing identity, audit, evidence-retention and domain-persistence seams. A later implementation
may extend their shared contracts; this proposal requires no general policy engine, new service,
external reviewer portal, or duplicate client-side rules.

| Record | Required retained information |
| --- | --- |
| Declaration event | Stable declaration ID and monotonic revision; exact tenant/company and narrower scope; bound human; old/new state; effective and recorded times; authenticated actor; reason; adopted policy version/hash; preceding event/reference. Enable, amend and withdraw are append-only events. |
| Attestation receipt | Stable receipt and idempotent command IDs; typed `preparer attestation` classification and schema version; exact preparer-owned task/assertion or draft ID/revision and payload hash; separately bound source artifact/version and creator/importer provenance; preparer/attester ID and trusted origin; scope; action class and permitted effect; declaration ID/revision; policy ID/version/hash; decision time; reason and the exact confirmation text accepted. |
| Supporting evidence | Retained artifact IDs/versions/hashes, provenance and synthetic/live classification, evaluated checks and their versions/results, eligibility/exclusion evaluation, related population and exposure calculation if C is chosen. References must resolve in the same authorized scope. |
| Lifecycle and dependencies | Links to any earlier receipt, later revocation/supersession, target correction, independent review, affected journal/report/package and frozen coverage snapshot. Store why an earlier receipt ceased to apply. |
| Published or exported proof | Original classification/label, scope, target and receipt references, policy/declaration versions, snapshot time, content hash, retention/legal-hold context and exact affected leaf assertions. |

These are minimum policy facts, not a prescribed database schema. Do not retain secrets or extra
personal data. Hashes identify exact bytes; a hash alone is not a signature, authorization grant
or proof of independent review. Preserve original human assertions, later corrections and system
decisions separately. An AI-generated rationale remains a draft until the authorized human
explicitly accepts responsibility; its origin must not be concealed.

## 7. Withdrawal, concurrency and historical proof

Withdrawal removes authority for **every new attestation-dependent transition**, including items
prepared before withdrawal, queued commands and previews already open in either workstation.
Checking only a record's creation time or a cached UI toggle is insufficient. The target, policy
and declaration revisions must be revalidated at the same durable decision boundary as the effect.
Use the existing store's concurrency/transaction semantics; an instance-local lock alone is not
adequate evidence of cross-instance exclusion.

| Situation | Required behavior |
| --- | --- |
| Withdrawal commits before the attestation | Refuse the stale command with no successful receipt/effect; show the ordinary review requirement or current blocker. |
| Attestation commits before withdrawal | Preserve its original receipt and permitted completed effect. Withdrawal does not retroactively invalidate truthful historical evidence. |
| Exact retry of a completed command after withdrawal | Return the original historical result, subject to current read permission; do not perform another effect or claim current authority. A changed payload under the same command ID is a conflict. |
| Declaration is later enabled again | Create a new revision. Old previews or uncommitted commands do not regain authority; require a fresh evaluation and confirmation. |
| Target/evidence changes, or the assertion was erroneous | Append revocation/supersession/correction evidence and recompute current applicability. Do not edit the original receipt. Existing accounting correction rules still govern any financial correction. |
| Later genuine independent review becomes available | Record a new decision bound to the exact reviewed revision and its actual review evidence. Reevaluate current status under applicable policy; never backdate or upgrade an earlier package. This does not create an access path for that reviewer. |
| Reopen an old delivered package | Reproduce retained bytes, classification, label and dated metric snapshot. Current UI may add a separate current-status notice; it must not rewrite the original package. |
| Create or deliver a new package | Recheck ordinary publication/delivery authority and current dependencies. Historic read/reproduction permission is not new publication authority. |

Withdrawal is prospective; revocation of a particular assertion is a different event. An unchanged,
unrevoked historical receipt may still appear in a supplemental support measure after declaration
withdrawal, labeled with its original declaration state/date. It cannot authorize another step.
The original bound attester with the required record-modification permission, or an explicitly
authorized governance actor in the same scope, may append a reasoned revocation against the
current receipt revision even after declaration withdrawal. This corrective permission does not
require a newly enabled declaration and grants no new attestation authority. Readers, other
preparers and foreign-scope actors cannot revoke it; technical administration alone supplies no
implicit grant. Corrected affirmative assertions require their own current authority and receipt.
Revoked, stale, superseded, foreign or corrupted support is excluded from current supported-attested
measures. Retention, legal holds and access restrictions survive either event. If retained bytes
are missing, report unavailable proof; do not reconstruct a supposedly identical historical package.

## 8. Shared browser/WPF presentation

Use one shared server classification and eligibility projection. Both clients and exports must
show this canonical disclosure beside the decision and relevant amount/status:

**Attested by preparer — not independently reviewed**

Keep four independent dimensions visible: evidence readiness; review classification; workflow
authority/state; and source provenance. `Ready` can coexist with preparer attestation and synthetic
data. None of those labels means Approved, Posted, Certified or Released.

| Surface | Required display |
| --- | --- |
| Action/review queue | Explicit Attest action for an eligible operation, its limited effect and outstanding review requirements. No generic Approve relabeling. A denial names the blocking class/reason. |
| Number Passport, amount proof and decision trace | Disclosure next to the decision/amount; details expose actor, time, exact revision, receipt, policy, declaration and evidence. Do not put the only disclosure in a tooltip. |
| Package summary and affected lines | Under B, disclose "contains preparer-attested evidence" with task/receipt counts and affected proof links; do not label the whole financial amount attested. If a later amount-level class is adopted, show its attested leaf value/count within scope. Per-line classifications remain intact; no blanket independent-review badge. |
| Audit and retained export | Original disclosure and version-bound evidence remain present after withdrawal, revocation or later review; current status is a separately dated annotation. |
| Legacy or unsupported classification | Show classification unavailable/legacy evidence with its known facts. Neither `Approved`, unequal actor IDs nor an unknown enum value permits an inferred independent-review label. |

The [browser proof drawer](../../src/Meridian.Ui/dashboard/src/components/meridian/proof-drawer.tsx)
and [WPF amount-proof view model](../../src/Meridian.Wpf/ViewModels/LedgerAmountProofDrawerViewModel.cs)
already consume shared proof data. Extend that seam in a later implementation; do not fork policy
evaluation or compute attestation eligibility in the clients. No new root workspace is needed.

## 9. Verified Coverage and separate attestation reporting

The [charter](meridian-design-document.md), Sections 1.5–1.6, and `W11-COVER-001` define a planned
metric. It requires Ready evidence, completed period reconciliation with no above-tolerance open
break, recorded approval and retained supporting evidence. The current wording does not assert
that every legitimate ordinary approval path is an independent-human review. Preserve those
domain semantics; do not retroactively relabel legacy approvals or the existing privileged path.

**Recommended W14 adoption rule:** a preparer-attestation receipt does not satisfy the ordinary
recorded-approval predicate. Keep the existing Verified Coverage headline and count measure;
show preparer attestations separately with the same frozen population. This is a proposed
`W11-COVER-001` policy clarification, not an implemented formula change.

| Reporting option | Treatment | Decision |
| --- | --- | --- |
| Preserve headline and show a separate attestation measure | Ordinary approval predicate remains necessary. Attested-only items remain in the denominator and outside the verified numerator. Show supported-attested value/count separately. | Recommended. A low solo-operator verified percentage remains an honest limitation. |
| Broaden a combined measure | Combine approved and supported-attested items in a newly named/versioned operational-support measure, with components displayed. | Possible separate product choice. Requires explicit charter/registry adoption and comparable-history rules; cannot silently keep the old headline name/meaning. |
| Show only attestation counts alongside the headline | No combined percentage; inventory drill-through distinguishes support and review. | Simple first UI, but value must still be inspectable so count does not conceal concentration. |

For the recommended option, use distinct leaf asset/liability assertions keyed by exact scope,
financial object, book, basis and as-of date. Repeated lines, parent totals and receipts are not
new denominator items. Freeze the inventory first. Missing evidence, unmatched items and simulated
inputs stay in the denominator; simulated/sample/fixture/demo items never enter the real verified
numerator. Show synthetic demonstration metrics in a separate, explicitly labeled scope.

Value coverage uses absolute amounts in a declared reporting currency and valuation basis; do
not net assets and liabilities. Show value and count together, with asset/liability breakdowns.
Unknown valuations/FX or incomplete inventories make the relevant result provisional/unavailable;
do not omit affected items to improve coverage. Zero value denominator is N/A, while zero-valued
leaf assertions still participate in count coverage.

Under B, receipts support evidence-acknowledgment and preparer-task reporting only. Reading one
artifact or checking a non-postable draft does not attest every financial assertion that uses it,
and does not satisfy a required amount-review decision. Dependency-graph reachability alone cannot
propagate attestation. A financial supported-attested measure requires an expressly adopted
amount-level class and complete assertion-level applicability under C or a separately reviewed
definition. The economic arithmetic oracle is conditional on that policy choice, not a B outcome.

Supplemental reporting distinguishes **attestation recorded** from **currently supported
preparer-attested**. The latter requires a receipt applicable to the exact assertion revision and the other
evidence/reconciliation/retention conditions. Recorded, unsupported, revoked and superseded
attestations remain inspectable but do not inflate that numerator. For a disjoint headline
breakdown, assign an otherwise eligible item to ordinary-approved first, then attested-only,
then unapproved; keep all receipt history. Ordinary-approved requires every applicable approval
dependency for that exact leaf/scope/revision to satisfy ordinary policy. Report-level approval
cannot cure a missing ordinary approval of an underlying required accounting decision. Later
approval must not double-count one assertion.
A mixed item's unresolved dependency prevents verification of the affected assertion even if
another input is approved. Simulated/sample/fixture/demo assertions also remain outside the real
supported-attested numerator; record their attestations and demonstration calculations in an
explicitly synthetic scope. Proof-layer `CoveragePercent` is never substituted for this metric.

Publish scope, leaf inventory, source classifications, policy/definition versions, assessment time,
amount/FX basis and receipt links with every result. Historical released assessments remain frozen.
The [worked oracle](single-principal-attestation-acceptance-matrix-2026-11-02.md#4-coverage-arithmetic-oracle)
shows the denominator and classifications explicitly. It is synthetic reasoning evidence, not a
live Verified Coverage claim.

## 10. Reconciliation with registered W14 and charter Version 1.7

PR #3358's merged registry and charter remain the planning sources; this draft does not duplicate
W14 registration or rewrite the November 1 historical review. The charter and W14 evidence list
link this proposal and matrix as draft planning input. The roadmap wording is qualified to reflect
actual actor-separation guards and the existing privileged sign-off exception. No policy option,
metric treatment or first-slice scope is adopted by those links. After the owner chooses, reconcile
the corresponding acceptance text and policy decision in a focused update. Keep `status: planned`,
planned evidence and unranked status until the registered criteria actually have implementation
and acceptance evidence.

| Registered W14 criterion / charter text | Coverage here | Required reconciliation |
| --- | --- | --- |
| Audited declaration; off preserves current refusal behavior | Sections 2, 5 and matrix group A | Describe actual guarded actions; qualify the existing privileged reconciliation sign-off exception. Do not claim universal independent review. |
| On creates distinct attestations; high-risk classes blocked | Sections 3–6 and matrix groups B–C | Owner chooses B or C, exact allowlist and exclusions before coding. Link the adopted policy from W14 evidence without claiming implementation. |
| Browser/WPF passports, traces, packages and audit label attestations; historical labels survive | Sections 6–8 and matrix groups D–E | Use the same semantic label and retained versioned classification across both clients and exported proof. |
| Separate coverage; headline choice under W11 | Section 9 and matrix group F | Record the owner's headline decision in W11/charter; this draft recommends exclusion from the ordinary approval predicate. |
| Declaration on/off audited; later items require independent approval | Sections 5 and 7 | Strengthen the boundary to every new attestation-dependent transition, including pre-existing drafts. Preserve ordinary legitimate approval semantics rather than fabricate reviewer access. |
| Implementation paths, excluded policy and both-client evidence before advancing status | This document and the matrix are planning references only | Neither a policy document nor a synthetic test plan completes the row. Retain the independent Security and Accounting review requirement for this exception. |
| Charter Section 25.3.12 example permits eligible corporate-action posting | Option B is narrower; C is undecided | Do not describe B as satisfying this example. Owner must narrow the first slice explicitly or choose a concrete economic class under C. |

If the owner selects B, the following is the proposed replacement **first-slice** acceptance text,
not an adopted change to the charter:

> With the declaration disabled, existing guarded approvals and their documented exceptions behave
> as before. With it enabled, the authorized preparer can retain a separate attestation for the
> named non-posting evidence action; browser, WPF and retained proof state that it was not
> independently reviewed. Posting, waivers, overrides and release remain subject to their ordinary
> controls. Withdrawal blocks new attestation-dependent transitions, including pending drafts;
> historical packages reproduce their original labels. This slice proves the attestation contract
> and does not complete the economic-posting example or W14 as a whole.

If C is selected, replace the vague word "eligible" in the posting example with the approved
event, treatment, limits, prerequisites and excluded cases. Do not add an ADR-019 expansion or
change PRD-000 sign-off as a way to satisfy this example. `DEC-SINGLE-PRINCIPAL-001` remains a
planning-direction decision; any accepted policy outcome must be recorded as a dated subsequent
decision, preserving its original meaning.

## 11. Outstanding owner decisions and review record

Every entry is **open**. The recommendations are supplied to make the decision concrete; silence,
AI review, a document merge or a declaration toggle is not policy adoption or live activation.

| Decision | Proposed answer | Owner/review needed |
| --- | --- | --- |
| D1. Initial scope and useful outcome | B with the two named non-posting actions; explicitly acknowledge that C is needed for economic posting. Alternatively choose a concrete C class before implementation. | Product owner; independent Security and Accounting review of the selected boundary. |
| D2. Excluded classes and materiality | Adopt Section 4 exclusions. No economic thresholds for B; if C is chosen, Accounting defines currency/exposure/aggregation/qualitative rules and exact supported treatments. | Owner and Accounting; Security reviews bypass and classification risks. |
| D3. Declaration and domain grants | Separate administrative enable/withdraw authority from bound human/domain attestation authority; default off and empty until adopted. Resolve typed receipt validation before connecting new decisions to legacy string-reference authority consumers. | Owner and Security; Accounting confirms business-authority assignments. |
| D4. Receipt and labels | Adopt Sections 6 and 8, including explicit legacy/unknown handling and shared browser/WPF/export semantics. | Owner, Security and Accounting. |
| D5. Withdrawal and retention | Prospective commit-boundary withdrawal; scoped revocation authority remains usable afterward; append-only correction and immutable historical labels/bytes; no new authority from replay. | Owner, Security and Accounting; persistence design reviewed during implementation. |
| D6. Coverage | Attestation does not satisfy ordinary approval; same denominator, separate value/count, no combined unchanged headline. Decide treatment of legacy approval evidence explicitly. | Owner and Accounting under W11, with Security review of classification trust. |
| D7. Conditions to begin implementation | Record D1–D6 and the policy revision with independent Security and Accounting review; then implement one synthetic slice with this matrix. Real operation remains separately authorized. | Owner records adoption/disposition and review references. |

Independent review is a requirement for this proposed exception, not a claim that Meridian has
a staffed review organization or a new gate for unrelated development. The owner may obtain the
Security and Accounting judgments from suitably qualified independent reviewer(s); AI agents do
not satisfy either judgment. A person reviewing their own policy or using a second login does
not create independent review. Record actual names/roles, scope, version, conclusions, conflicts
and unresolved concerns when reviews occur; none have been supplied by this proposal.

Implementation can reuse the existing local identity, audit, evidence and storage boundaries.
Validate with deterministic synthetic inputs and the existing application/store/UI test seams;
no private accounts, live provider credentials or new infrastructure are prerequisites. Preserve
the [solo-developer workflow](../ai/solo-developer-workflow.md), ordinary required checks and the
owner's merge authority. This documentation change neither requests nor performs a merge.
