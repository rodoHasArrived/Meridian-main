# P1: Intercompany Agreement Registry

**Status:** active
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-12
**Audience:** implementers of entity management, financial operations, ledger storage and operator workstations

Give intercompany activity an approved source of terms and evidence. An operator should be able to
open a journal line, identify the two legal entities, inspect the exact agreement revision cited at
posting, and see later amendments without changing the meaning of that journal.

The original design was grounded in source inspection at commit `3fb526b73e`. The P1 implementation
now adds the registry, approval workflow, journal guards and shared operator surfaces described
below. The [domain guide](../../domain/intercompany-agreements.md) defines the current vocabulary
and configuration. The [blueprints register](README.md) indexes this design; the
[roadmap registry](../../roadmap/README.md) continues to own delivery status. P1 is the proposal
label, not a new roadmap item ID. Source implementation is separate from executed validation and
native workstation acceptance.

### Implementation record — 2026-11-12

- `IntercompanyAgreementService` and its shared FundStructure contracts implement draft snapshots,
  independent review, retained evidence, exact command replay, effective chronology and historical
  knowledge queries. Access covers all legal entities disclosed by retained history, including
  parties changed during pre-approval drafts.
- `PostgresIntercompanyAgreementStore` uses fund-structure migration `006`, immutable full command
  snapshots and separate approved revision/audit/receipt tables. A mutable head advances once per
  command under entity/head authority locks. Command IDs are unique within a tenant.
- Optional journal ID/version dimensions flow through mappings, JSON, hashes and structural replay.
  Manual review and the durable PostgreSQL append boundary validate supplied references; standalone
  append holds registry authority through commit. Unsupported external transactions refuse them.
- Shared routes include advisory journal-line drill-through with exact transaction/functional
  amounts. Browser Accounting Configure and the desktop Fund Operations Agreements tab use shared
  contracts and server capabilities. Coverage does not alter consolidation matching or balances.
- `MERIDIAN_INTERCOMPANY_REFERENCE_AUTHORING_ENABLED` defaults off and gates browser reference
  authoring after compatible writers are deployed. It never disables validation of supplied refs.

Validation record: the isolated real-PostgreSQL migration apply/reapply smoke passed. Targeted .NET
runs passed 43 contract tests, 14 application tests, 27 shared desktop workspace/transport tests and
332 backend tests, including real PostgreSQL approval-to-posting, concurrency, reversal/replay,
authorization and consolidation regressions; none were skipped. The Windows-targeted WPF build
passed with zero errors. Browser validation passed the canonical build, 195 regression tests and
six focused tests after the final receipt-recovery change; Chromium exercised synthetic API
workflows at 1440×900 and 1366×768. These are local results, not hosted CI or production
certification. Full CI and native WPF interaction tests were not run; Windows is required for that
runtime acceptance. The test matrix in section 8 remains the behavior contract.

## 1. Outcome and boundaries

P1 introduces a tenant-scoped, bilateral agreement registry with approved revisions, effective-date
queries, retained evidence, journal provenance and shared operator read models. It supports loans,
management fees, cost sharing, cash sweeps, guarantees and other documented arrangements. Agreements
do not require a fund root or a particular ownership percentage; eligibility for consolidation is a
separate decision.

Two defaults were confirmed for this design:

- An intercompany journal without an agreement reference may still post. Report missing agreement
  coverage as a warning. An explicitly supplied invalid reference blocks the posting.
- An amendment may take effect before approval, with a mandatory retrospective rationale. It changes
  authority for new postings on affected dates, never the references on already posted journals.

P1 does not generate accruals (the later operator-requested loan-interest accrual is recorded in
[ADR-028](../../adr/028-governed-intercompany-interest-accrual.md)), calculate allocation amounts, post paired entries, settle cash,
implement FX translation, expand the consolidation perimeter or enforce legal-entity lifecycle
posting restrictions. It does not add a document-upload platform, filing calendar or general entity
master expansion. Those remain separate work, including proposals P2–P5.

## 2. Existing seams and required additions

| Existing source | Reuse and limitation |
| --- | --- |
| [Fund-structure DTOs](../../../src/Meridian.Contracts/FundStructure/FundStructureDtos.cs) and [commands](../../../src/Meridian.Contracts/FundStructure/FundStructureCommands.cs) | Resolve parties to `LegalEntitySummaryDto`, including effective intervals. Generic graph nodes are insufficient. Neither file supplies an intercompany agreement. |
| [PostgreSQL fund-structure service](../../../src/Meridian.Application/FundStructure/PostgresFundStructureService.cs) and [store](../../../src/Meridian.Storage/FundStructure/PostgresFundStructureStore.cs) | Reuse composition and entity lookup conventions. `ReplaceOwnershipLinkAsync` changes the predecessor's end date; snapshot persistence performs separate upserts. This is not the required immutable, atomic revision store. |
| [Scoped structure authorization](../../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ScopeAuthorization.cs) | Authorize every disclosed entity, including both agreement parties. A filter on one entity does not authorize disclosure of the other. |
| [Evidence Vault contracts](../../../src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs), [canonical references](../../../src/Meridian.Contracts/Workstation/EvidenceVaultReference.cs) and [vault access](../../../src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.VaultAccess.cs) | Resolve scoped retained artifacts and pin their identities and hashes. A caller-supplied URL or hash is not evidence authority. |
| [Ledger dimension DTO](../../../src/Meridian.Contracts/Ledger/AccountingConfigurationDtos.cs) and [domain dimensions](../../../src/Meridian.Ledger/LedgerLineDimensionSet.cs) | Add agreement ID and approved version together. Existing counterparty attribution remains required when a reference is supplied. |
| [Consolidation design](../../domain/intercompany-consolidation.md) and [fund-structure authority lease](../../../src/Meridian.Storage/FundStructure/PostgresFundStructureStore.Consolidation.cs) | Follow the existing authority-lease lifetime pattern. Structure and ledger databases can differ; do not assume a shared transaction or cross-database foreign key. |

The existing journal and operations approval flows are subject-specific. P1 adds an agreement-owned
workflow following their evidence, distinct-reviewer and concurrency conventions; it is not a
configuration-only addition to a generic agreement-capable approval service.

## 3. Contracts and business meaning

Wire types live in `Meridian.Contracts.FundStructure`, with a separate
`IIntercompanyAgreementService` in `Meridian.Contracts.Services`. Contracts remains a leaf project;
do not reference Ledger or Storage domain types from its DTOs. Register all requests, results, enums
and collections in `FundStructureContractsJsonContext`.

### Agreement and revision identity

`IntercompanyAgreementDetailDto` exposes the stable agreement ID, display code/name, two parties, type,
currency, aggregate concurrency version, latest approved version and any pending proposal summary.
Detail responses also expose the revision applicable to the requested date. The latest approval is
not necessarily the revision applicable today.

`IntercompanyAgreementRevisionDto` is an immutable approved snapshot. It contains:

- `AgreementId` and positive integer `AgreementVersion`, assigned in approval order.
- Party IDs and roles, agreement type and currency, copied into the snapshot.
- For activation/amendment, `EffectiveFrom` and optional exclusive `EffectiveTo`, expressed as
  `DateOnly` business dates; termination instead carries a `TerminationEffectiveDate` cutoff.
- Descriptive terms, retained evidence references and the canonical reviewed payload hash.
- Predecessor approval version, semantic `BaseAgreementVersion` (absent for activation, required for
  amendment/termination), proposal identity, authenticated author/submitter/approver, recorded UTC
  timestamps, rationale and revision action (`Activate`, `Amend` or `Terminate`).

`AggregateVersion` is a separate concurrency token advanced by each successful state-changing
command, including draft edits and review decisions. It is not the journal's approved version.
Approved version numbers advance only when a revision is approved. No approved payload is updated
or deleted; a rebuildable head/projection may change without becoming historical authority.

### Parties and terms

Exactly two different, tenant-attributed legal entities participate in v1. Roles are explicit:

| Type | Party roles |
| --- | --- |
| Loan | Lender and borrower |
| Management fee | Service provider and recipient |
| Cost share | Two participants; the allocation key explains their shares |
| Cash sweep | Sweep source and destination |
| Guarantee | Guarantor and supported entity; external creditor details remain in the evidence |
| Other | Two parties with mandatory role descriptions |

After initial approval, party IDs, roles, type and currency cannot change. A novation or replacement
arrangement requires a new agreement, optionally citing its predecessor. A guarantee record does not
constitute a model of all third-party obligations or generate guarantee accounting.

`IntercompanyAgreementTermsDto` contains optional fixed annual rate as a decimal fraction (`0.05`
means 5%), rate description, schedule description, allocation key/description, settlement days and
notes. Negative rates are representable. Floating rates require descriptive terms rather than an
invented fixing. Settlement days are nonnegative calendar days in v1; business-day calculation is
outside this registry. Activation requires a terms explanation or the relevant populated fields;
cost sharing requires an allocation description and `Other` requires a purpose description.

These are reviewed terms, not executable instructions or a claim that the ledger should equal a
calculated contractual balance. A later accrual consumer must explicitly interpret and validate
them, using the existing [contractual schedule kernel](../../domain/contractual-schedule.md) where
appropriate. P1 does not introduce a second schedule or transfer-pricing engine.

At submit and approval, activation/amendment validates both legal entities, currency, nonempty
effective interval and compatibility with the entities' retained existence intervals. Compare
business-date bounds at UTC midnight against the existing timestamp intervals, with no tenant-local
timezone inference. Open-ended agreement terms require open-ended party existence intervals.
Termination uses the cutoff-specific validation below rather than copying an open-ended terms
interval. Do not conflate entity existence with the separate P3 rules for posting to a particular
lifecycle status.

### Effective-date selection and retrospective changes

Keep approval time and effective date separate. Queries accept an accounting/business `asOf` date
and optionally `knownAtUtc` for reconstructing what had been approved at a historical instant.
Posting always uses current approved knowledge, evaluated at the journal's accounting date.

Among approved activation/amendment revisions known at the query instant whose start is on or before
`asOf`, select the greatest `EffectiveFrom`; break same-date ties by greatest approved version.
Then check its exclusive end and any approved termination cutoff. Do not fall back to an older
revision when the selected one has expired. A later effective start derives the predecessor's
supersession boundary without changing the predecessor's originally approved payload.

For example, v1 starts January 1 and v2 starts April 1. A subsequently approved v3 starting February 1
governs February through March; v2 still governs from April 1. Approval order does not silently erase
an already approved future boundary. The review preview must show this timeline, including same-date
replacement and any gaps. Replacing a later boundary requires another explicit proposal at that date.

A termination proposal supplies only `TerminationEffectiveDate`, rationale and evidence, not a new
terms interval. The approved decision retains party identity and its base revision. It applies
across all revisions from the cutoff and cannot precede initial commencement. Validate the resulting
historical agreement segments through that cutoff against party existence; an originally open-ended
agreement can be closed after an entity's recorded end becomes finite. No copied open-ended interval
must still be valid. Termination does not revive older terms or permit scheduled reactivation.
A termination is final in v1; later amendments may correct terms before its cutoff but cannot extend
it, and their validation clips their effective segments at that retained cutoff. A new arrangement
after termination needs a new agreement. Retrospective amendment/termination requires rationale and
a preview of affected dates and posted references, without changing those journals or closed reports.

Effective status is derived as `Unapproved`, `Scheduled`, `Active`, `Expired` or `Terminated` for the
query date. Proposal state is separate. “Amended” is a history event, not a status that makes an
active agreement unusable. An approved agreement can remain active while an amendment is in review.

## 4. Commands, review and evidence

Use immutable proposal identity plus versioned saved snapshots. Permit one open proposal per
agreement in v1. Proposal state is `Draft`, `Submitted`, `Approved`, `Rejected` or `Withdrawn`.
Create starts an activation draft. Amendment dates cannot precede initial commencement. Amendments
record `BaseAgreementVersion` from the effective-date selection at the proposed start; a May
amendment in the example starts from April's v2, not the latest-approved February v3. An explicit
extension of expired terms uses that date's selected, expired predecessor and previews the extension.
Termination uses the revision immediately before its cutoff as its semantic base (the initial
activation if terminating at commencement). The latest approval head and `ExpectedAggregateVersion`
are separate concurrency guards. Freeze and recheck both the semantic base and concurrency guards
at submission/approval. Editing a draft retains its prior snapshot in audit history. A submitted
proposal cannot be edited; withdraw and create a replacement for changed terms or evidence.

| Transition | Authority and retained result |
| --- | --- |
| Create/save draft | Validate shape and party access; retain authenticated editor, expected aggregate version and payload snapshot. Incomplete business terms may be saved but cannot be submitted. |
| Submit | Validate complete terms, timeline, entity intervals and resolved evidence; freeze the exact snapshot/hash and retain submitter and time. |
| Approve | Recheck submitted hash, evidence, entity authority, expected head and temporal impact. The reviewer must differ from the submitter and every author of the submitted proposal. Atomically retain the approved revision, decision, audit event and receipt. |
| Reject/withdraw | Retain actor and rationale; do not alter approved terms. Rejection requires an independent reviewer; an authorized author may withdraw. |

Use existing `ManageFundStructure` permission with `LegalEntity` scope checks on both parties for
all agreement commands. Reads follow the existing fund-structure route policy
(`ManageFundStructure`, `ManageDirectLending` or `AdminMaintenance`) plus both entity-scope checks.
Do not invent an existing `ApproveFundStructure` permission. Independent review
is an additional authenticated-identity check, not a client-provided role flag. No automatic approval
or posting is introduced. A single operator can draft, but cannot approve their own proposal.

Each command carries `CommandId`, `ExpectedAggregateVersion` and its action payload. Actors,
timestamps and authoritative tenant ownership come from server context. Exact retries by the same
authorized identity return the original receipt before fresh-state conflict checks; the same command
ID with different content or action conflicts. Approval binds proposal identity, submitted hash,
predecessor version and decision rationale. A stale submission must be replaced and reviewed again.

Require at least one scoped retained agreement/evidence artifact for submission and approval. Store
canonical vault identity, artifact identity and server-resolved content hash, not filesystem paths
or arbitrary web links. A review manifest bound to agreement/proposal identity references those
artifacts and the submitted terms hash. Do not relabel pre-existing source artifacts to manufacture
their provenance. Verify unchanged evidence at approval; unavailable or changed evidence blocks the
transition. Evidence reads retain their own tenant/scope checks after agreement access is granted.

## 5. Application, persistence and HTTP interfaces

`IntercompanyAgreementService` lives in the application layer with
`IIntercompanyAgreementStore` / `PostgresIntercompanyAgreementStore` storage support. Keep agreement
state out of whole-graph snapshot upserts. Shared endpoints and read models serve both workstations;
clients do not calculate authority or effective status independently.

The fund-structure database owns tenant-qualified agreement heads, proposal snapshots, immutable
approved revisions, audit decisions and command receipts. Use composite tenant/agreement keys,
unique approved version and command identity constraints, and local foreign keys where appropriate.
Tenant identity is server-owned and cannot be reassigned by a mutation. Unknown and inaccessible
agreement/party IDs must have indistinguishable lookup outcomes; list filters, counts, history and
selectors must not disclose the second party to a caller authorized for only one.

The implementation uses `IIntercompanyAgreementStore.ExecuteAsync` and a transaction-scoped `Save`
instead of a dedicated approval-only append method. In one local PostgreSQL transaction, stabilize
entity authority, lock the tenant-qualified agreement head, validate the receipt and expected version,
recheck the submitted proposal, append the immutable snapshot/revision/decision and advance the head
with its receipt. Concurrent amendments cannot both advance the same expected head. Draft and other
transitions also retain their audit/receipt with their head change. Application semaphores alone are
insufficient across service instances.

`FundStructureMigrationRunner` applies additive migration `006_intercompany_agreements.sql` using
its configured schema and `__SCHEMA__` substitution. Earlier applied scripts are unchanged. Database
guards refuse updates/deletes/truncation of retained snapshots, approved revisions, decisions and
receipts; their payloads do not use `ON CONFLICT UPDATE`. Journal dimensions use the existing JSON
storage shape and need no new ledger columns. No foreign key crosses into a separately configured
ledger database. Future rate columns follow the repository's shared numeric precision convention.

The `/intercompany-agreements` routes use the existing `/api/fund-structure` prefix:

| Method and suffix | Behavior |
| --- | --- |
| `GET /` | Authorized list, filtered by entity, type, effective status and `asOf`; stable cursor pagination. |
| `GET /{id}` | Detail, selected effective revision, latest approval, pending proposal and server-computed capabilities. |
| `GET /{id}/versions/{version}` | Exact immutable approved revision. |
| `GET /{id}/history` | Ordered proposal snapshots, review decisions and approved history, with effective and recorded times. |
| `GET /{id}/journal-lines` | Authorized entity-book source lines with exact amounts, pinned references and advisory coverage; no agreement matching or totals. |
| `POST /` | Create an activation draft. |
| `POST /{id}/proposals` | Create an amendment or termination proposal. |
| `PUT /{id}/proposals/{proposalId}` | Save a draft with its expected aggregate version. |
| `POST /{id}/proposals/{proposalId}/submit` | Freeze and submit the reviewed snapshot. |
| `POST /{id}/proposals/{proposalId}/approve` | Apply an independent approval atomically. |
| `POST /{id}/proposals/{proposalId}/reject` or `/withdraw` | Retain the decision without changing approved terms. |

All commands return detail plus a retained command receipt/new aggregate version. Use existing API
error conventions: validation errors identify correctable fields, concurrency conflicts return
`409`, unauthenticated requests return `401`, and undisclosable identities return `404`. Capability
denial on an otherwise visible record returns `403`. Server capabilities and blocking reasons,
rather than UI guesses, determine available review actions.

## 6. Journal provenance and append authority

Nullable init properties `Guid? IntercompanyAgreementId` and
`int? IntercompanyAgreementVersion` extend `LedgerDimensionSetDto` and `LedgerLineDimensionSet`, avoiding
changes to existing positional constructors. Both must be absent or present together. A supplied
version must be positive. Do not silently infer or substitute a version from the agreement ID.

Every new reference-bearing posting must resolve the currently applicable approved revision and
match the supplied revision. Check the authoritative book entity against the line entity, the two
agreement parties against that entity and its reciprocal `CounterpartyId`, the accounting date
against current effective authority, and agreement currency against transaction currency. Where
explicit line currency detail is absent, use authoritative book currency; do not confuse functional
currency with transaction currency or weaken existing FX validation.

Validate during journal preparation/review and recheck at the append boundary. A retrospective
approval can make an unposted draft stale; show the changed revision and require repair and renewed
journal review. A malformed, inaccessible, unapproved or incompatible supplied reference blocks.
No reference remains permitted and produces coverage warnings only for the currently supported
intercompany receivable/payable account classification. Do not infer an agreement from similar
names, matching amounts, counterparties or transfers. Unsupported account treatments are not
silently classified as covered.

Define a Ledger-facing `IIntercompanyAgreementPostingAuthority` abstraction, implemented through
the authoritative store and wired at composition. For reference-bearing appends, acquire shared
agreement-head locks in sorted tenant/agreement-ID order plus required entity authority locks;
read and validate approved authority under those locks. Acquire authority leases before the ledger
audit lock, retain them through the journal store's owned commit, and release them afterward. An
agreement mutation takes the corresponding exclusive head lock. Preserve the existing consolidation
lock ordering when both authorities are required.

This follows the existing consolidation lease pattern across separate database connections, not a
distributed transaction. Missing providers, lease failure or validation failure stops the append;
rollback and retry use retained idempotency receipts. Caller-owned transaction/atomic append paths
may accept references only if they retain and validate the same leases through their actual commit;
otherwise reject the reference-bearing write explicitly. Unreferenced legacy writes remain supported.

Already committed exact posting receipts can complete recovery after a subsequent amendment.
Historical reads resolve pinned revisions without requiring that they remain current. To reverse a
superseded or terminated agreement's journal, verify an exact inverse against the immutable source
in the authorized tenant/book: one-to-one source-line mapping, unchanged ledger accounts and all
accounting dimensions, swapped debit/credit amounts, and original currency and FX facts. New journal
and line IDs and the permitted correction date may differ. A source-journal ID alone is insufficient. Only
that verified reversal may retain provenance outside current effective authority; ordinary rebooks
and non-inverse adjustments use current-as-of validation. Existing period, policy, approval and
specialized correction restrictions still apply.

### Compatibility and advisory reporting

Carry the dimensions through normalization, manual journal mapping/tags, PostgreSQL JSON, shared
read models, aggregation keys, report hashes and retained-posting equivalence. Add deterministic
tagged signature suffixes only when a reference is populated, following the `PositionId` precedent.
Absent fields must preserve legacy signature/hash bytes; distinct agreement IDs or approved versions
must remain distinguishable. Keep historical JSON readable and omit null fields where serializers
previously did so. Version any changed retained evidence format; never regenerate an old evidence
digest with a new shape. Extend
[dimension compatibility tests](../../../tests/Meridian.Tests/Ledger/DimensionSignatureBackwardCompatibilityTests.cs)
and retained replay coverage before enabling populated references.

Expose a separate advisory coverage projection per agreement, entity direction and currency, with
source journal/line drill-through, missing references and retrospective revision differences. Reads
of old journals continue to show the original approved version. Registry unavailability is reported
as unknown coverage, never as complete coverage or a zero balance.

Do not change consolidation's reciprocal entity matching, amounts, existing source fingerprints,
approval freshness, idempotency or gross/posted/preview distinctions in P1. Agreement coverage is
neither a match result nor proof that balances agree under an agreement. Agreement-aware elimination
requires a separately reviewed rule; it must not silently offset different agreements. This registry
also does not establish settled amounts or authoritative ageing without P4 allocation semantics.

## 7. Operator workflow and shared surfaces

Within Accounting's existing entity context, provide an Agreements list and detail inspector rather
than a new root navigation category. The list shows both parties/roles, type, currency, applicable
version, effective interval, effective status, pending review and evidence posture. Filters retain
the selected entity and date. Distinguish no results, unavailable registry and permission denial.

Detail exposes terms, evidence, effective timeline, revision comparison, decisions and journal
references. Show effective time beside recorded approval time and label retrospective changes.
Create/amend/terminate actions open a draft; explicit save/discard protects unsaved work. Submission
previews timeline and provenance impact. Review presents the frozen terms/evidence and server-owned
eligibility. Approval never displays a journal as posted or a balance as settled.

Handle loading, empty, read-only, incomplete, submitted, stale/conflict, rejected, approved and error
states. A conflict retains the operator's input and offers comparison/reload rather than overwriting
the server. Use accessible tables/forms, keyboard-operable commands, visible focus and textual status
labels. Keep evidence and history available through progressive disclosure.

Browser and WPF use the same identifiers, filters, lifecycle meanings, capabilities and service/read
models. P1 implementation acceptance includes both surfaces; this documentation change modifies
neither. New recurring-journal agreement bindings are deferred: when added, they must resolve an
approved version for each generated accounting date rather than silently use a mutable latest ID.

## 8. Implementation sequence and acceptance evidence

Implement in reviewable slices, retaining the nullable compatibility boundary:

1. Contracts, timeline selection and scoped in-memory service; contract serialization and domain
   invariants first. Add the durable domain vocabulary to its owning domain guide with implementation.
2. Additive PostgreSQL registry migration, atomic commands, immutable history, approval/evidence
   adapters and shared endpoints. Test concurrent service instances against PostgreSQL.
3. Ledger dimensions, every append path's authority guard, retained replay and signature compatibility.
   Do not expose populated-reference authoring before these guards are deployed everywhere.
4. Shared coverage read model, browser and WPF workflow, evidence/history navigation and permission
   states. Deploy backend/read support before enabling workstation reference authoring.

Old clients may omit references; posted history is not backfilled or relabelled. Gate reference
authoring on compatible capability across every reachable writer, including workers/importers.
Capability-aware ingress/routing must refuse populated-reference traffic to older binaries: an old
writer cannot reject an unknown JSON field that it silently ignores. Once references exist, rollback
may hide authoring but must retain compatible writers, readers, validators, schema and historical
evidence; it cannot route that traffic to old writers. Do not roll back by dropping tables or nulling
posted provenance. Log agreement
transition/append refusals with scoped IDs and reason codes, without document contents; expose stale
authority, evidence failures and unknown coverage to operators.

The following are required feature scenarios. Test sources now cover these areas; the implementation
verification report records the executed subset and any remaining platform limitations:

| Area | Acceptance cases |
| --- | --- |
| Terms and timeline | Same-party/invalid-node rejection; role/currency rules; empty intervals; party existence; exact start/end boundaries; future approval; retrospective rationale; same-date replacement; February amendment before an approved April revision; May amendment uses April's base; expiration without fallback; termination after shortening party existence; termination masks future revisions and cannot reactivate. |
| Review and evidence | Incomplete draft saves but cannot submit; frozen snapshot; changed/missing/unauthorized evidence; author/submitter cannot approve; rejected/withdrawn history; approval-time entity recheck; proposal edits require fresh submission. |
| Persistence | Two processes amend the same expected head; only one commits; transaction rollback; exact retry after later changes; changed-payload command-ID conflict; immutable historical payload/hash; separate configured structure and ledger databases. |
| Isolation | Foreign tenant IDs look unknown; one-party access discloses no second-party detail/count; evidence requires scoped authority; actor/tenant spoofing fails; idempotent receipt access remains authorized. |
| Ledger | Missing reference warns; half reference/unknown version/unapproved version blocks; wrong book entity/counterparty/transaction currency/date blocks; retrospective approval makes drafts stale; authority failure rolls back; lease blocks concurrent mutation through commit; unsupported transaction paths reject references. |
| Recovery and compatibility | Legacy JSON/signatures/report hashes remain exact; agreement/version differences affect grouping and equivalence; original committed receipt recovers after amendment; verified inverse preserves original provenance; forged source ID, altered account/dimensional scope or non-inverse reversal fails; rebook uses current authority; historical evidence stays readable; incompatible routing/rollback cannot drop references. |
| Reporting and UI | Missing/unknown coverage is visible; agreement drill-through retains original version; consolidation figures and freshness keys remain unchanged; keyboard/evidence/history paths work; stale review cannot approve; both workstations display the same shared state. |

Use synthetic entities and retained synthetic evidence. Run targeted contract, application and ledger
tests plus PostgreSQL concurrency/integration tests for the implementation; exercise browser and WPF
workflows when those surfaces land. Existing consolidation tests remain regression evidence.
Required hosted checks retain their normal authority; source inspection or an AI review does not
constitute executed feature validation or production certification.

Validate documentation structure, internal links, DocFX navigation and whitespace alongside the
focused implementation checks. Separate agent reviews inspect temporal, ledger, persistence and
authorization invariants; they are not independent human approval. Record actual command outcomes
and unresolved platform limits in the implementation report. Live-provider access is not required
for this synthetic registry and accounting verification.
