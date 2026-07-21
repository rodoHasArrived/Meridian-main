# Event Accounting Architecture

**Owner:** Accounting and Ledger
**Scope:** Event-backed accounting postings, durable journal writes, and audit evidence
**Last updated:** 2026-07-21

## Purpose

Meridian treats accounting impact as the result of reviewed operational events. Source systems,
Financial Operations, direct-lending workflows, and accounting workbenches may prepare journal
candidates, but durable accounting impact is represented by append-only ledger journal entries with
posting metadata, evidence references, and storage-owned validation. `JournalEntry` remains the
accounting aggregate; `Transaction` continues to describe broader business and operational events
and is not an alias for a posted journal.

## Current Contract

- `AccountingPostingCommandDto` in `src/Meridian.Contracts/Ledger` is the shared posting intent
  envelope. It carries command identity, aggregate and period identity, effective and posting dates,
  idempotency key, source/correlation/causation ids, reviewer state, treasury context, correction
  lineage, action origin, and retained evidence references.
- `AccountingBookContextDto` is the additive shared snapshot of the ledger-book, period, basis,
  policy, currency, owner scope, and dimensions asserted by a posting workflow. Services resolve
  authoritative book state through the ledger-book service and reject mismatches; clients do not
  establish accounting authority by supplying the snapshot.
- `AccountingRulePackReferenceDto` identifies the existing accounting policy rule pack and selected
  rule/version used to prepare a candidate. It does not introduce another rule engine or parallel
  rule-pack authority.
- Asset Operations contracts carry additive `InstrumentRoleDto`, `BookPositionDto`,
  `PositionEconomicStateDto`, `EconomicEventReferenceDto`, and `ProjectionLineageDto` records.
  Posting candidates may reference this typed lineage while retaining legacy source-event fields for
  compatibility; when both shapes are present, they must agree.
- `AssetAccountingEventSpineDto` is the canonical lifecycle envelope for `Acquisition`,
  `Capitalization`, `Valuation`, `Income`, `CorporateAction`, `Impairment`,
  `DepreciationAmortization`, and `Disposal`. It binds Security Master identity, an authoritative
  versioned book position, ledger book, period, accounting basis, economic event, projection
  lineage, retained evidence, projected effects, durable journal impact, lot-mutation batch, and
  correction lineage without making a projection a balance fact.
- `Expected` and `Projected` are separate durable spine versions. `Drafted`, `Approved`, and
  `Posted` likewise each append exactly one next canonical stage in their own durable version;
  lifecycle appends cannot collapse, skip, remove, or rewrite stages. `Reconciled` and `Reported`
  continue the same one-stage-per-version rule. Every stage carries its own actor, timestamp,
  reference, and typed retained evidence; stage names cannot be inferred from endpoint availability
  or UI labels.
- Approval evidence uses the fully scoped `AssetAccountingPostingApproval` subject identity. It
  binds the economic event id and version, fund, ledger book, period, accounting basis, approval id,
  canonical Drafted-candidate fingerprint, and optional tenant/company scope in one retained
  artifact. Legacy event/fund/book-only subject keys remain migration/diagnostic input and do not
  satisfy the canonical approval gate.
- `FactorPaydownProjectionService` in Instruments is the single calculator for factor-driven
  principal economics. It requires retained factor evidence, validates held face, factors,
  currency rounding, and position version, and derives a deterministic source event from Security
  Master identity, book position, effective date, factors, and source-content hash.
- `LedgerJournalEntryWrite.PostingCommand` is optional for legacy-compatible callers, but when it is
  supplied `AccountingPostingCommandValidator` normalizes and validates the write before it reaches
  `PostgresLedgerJournalStore`.
- `JournalEntryMetadata.EvidenceReferences` is the ledger-native typed evidence list. Projection code
  should prefer it over parsing tag strings, while retaining tag fallbacks for older journal facts.
- Financial Operations `AccountingJournalDraftService` builds posting commands for source-backed
  drafts. Approval-required drafts remain pending and cannot be appended until an approved or
  not-required reviewer state is supplied to the durable write path.
- Direct-lending journal projections now attach deterministic idempotency, typed source evidence, and
  a posting command before handing the write to storage.
- Operations Continuity remains a governed candidate path: it validates durable command id,
  idempotency key, period, duplicate candidate posture, Security Master provenance, and line mapping
  evidence before creating a journal write. Its append path remains compatible with legacy writes
  while storage command validation applies to typed posting-command callers.

## Invariants

- Posted journal entries are immutable accounting facts. A correction uses a new economic-event id
  and its own currently open target period. It appends an adjustment, reversal, rebook, or
  restatement while typed correction lineage preserves the original event/version, immutable posted
  journal, original accounting period, ledger book, accounting basis, and mutation batch when
  applicable; it never rewrites the original fact.
- Material posting commands require `HumanOperator` action origin. Reviewed automation may prepare
  draft support, but storage rejects material commands that claim assistant or automation origin.
- Posting commands require a durable idempotency key. Canonical `AssetAccounting.*` commands require
  complete typed retained evidence identity, content hash, source reference, accepted reviewer,
  review time, effective date, evidence version, retention metadata, and subject scope; operator
  rationale and string links cannot substitute for that evidence. Duplicate command, source-event,
  and normalized idempotency keys are also protected by the Postgres journal indexes.
- Reversal, rebook, and restatement command intents require source journal lineage before append.
- Candidate journals remain unposted review artifacts. Projection events, position economic state,
  and balance snapshots remain rebuildable operational views; none is a second accounting truth.
- `PostedJournalImpactDto` may exist only after a durable journal can be re-read. It must identify the
  immutable journal, ledger book, period, accounting basis, posted status, currency, balanced debit
  and credit totals, and the underlying lines; candidate previews and approval state never satisfy
  this contract.
- Acquisition lot creation and disposal of explicitly selected, expected-version lots share one
  serializable transaction with the journal append. The storage-owned mutation batch retains the
  canonical fingerprint, before/after lot snapshots, selected-lot evidence, relief method and policy
  revision, correction lineage, and exact-replay result; any stale period or lot CAS rolls back both
  journal and lot consequences.
- Atomic disposal does not trust caller-selected lot order or policy labels. Storage resolves the
  effective persisted account policy and the exact open-lot set for book, account, Security Master
  id, book position, currency, and effective date under the posting transaction, then requires the
  submitted selections to equal the authoritative FIFO, LIFO, HIFO, or SpecificId relief plan.
  `AverageCost` remains available to other ledger-engine workflows but is unsupported by this
  lot-discrete atomic asset-posting path and fails closed.
- Ledger projections are rebuildable views over durable journal facts. They may consume report-pack
  outputs, approval metadata, and evidence references, but they do not redefine posting policy or
  survive as authority when they disagree with the immutable journal.

## Instrument-to-Journal Boundary

The semantic alignment follows this dependency and evidence flow:

```text
Reference Data / Security Master
-> Instruments / Asset Operations
-> Financial Operations
-> Ledger / Storage
```

- Security Master owns canonical security identity, identifiers, classifications, and retained
  reference-data evidence. Instruments and Asset Operations consume `SecurityId`; they do not own a
  replacement instrument master or make Security Master subordinate to one.
- Instruments and Asset Operations own instrument roles, book-position context, economic-state
  projections, and projection lineage. These records explain economic impact without writing ledger
  facts.
- Financial Operations resolves authoritative book context, applies the existing promoted accounting
  policy/rule pack, builds the posting candidate, and preserves the approval and evidence workflow.
- Ledger and Storage validate and append balanced `JournalEntry` aggregates with child
  `LedgerEntry` records. The immutable journal is authoritative after posting.

The shared Asset Accounting Event Spine follows one path for all eight event kinds:

```text
retained source evidence
-> Security Master identity / current retained versioned book position
-> durable Expected asset accounting event
-> durable Projected asset accounting event
-> durable Drafted Rules Studio posting candidate
-> durable independent human Approved attestation
-> durable Posted attestation plus immutable JournalEntry and atomic lot mutation when applicable
-> Reconciled evidence
-> Reported lineage
```

`AssetAccountingEventSpineService` re-reads the immutable projected event, the authoritative book
position, ledger book, period version, accounting policy, and promoted rule pack before Rules Studio
drafting. A submitted event kind, amount, currency, event identity/version, book, position version,
period version, evidence set, or lineage that does not match server state blocks candidate creation.
The resolved current `BookPositionDto` must retain the exact economic-event identity, version,
source, content hash, effective dates, Security Master id, and position id plus every exact typed
evidence identity asserted by the event. That position/economic-state snapshot remains a rebuildable
pre-ledger view, not journal truth. This boundary validates retained evidence identities embedded in
the persisted snapshot; it does not claim to resolve physical Evidence Vault objects or bytes.
The Drafted transition retains the complete candidate request and generated result together with
canonical fingerprints and any lot instruction. Posting must reproduce that exact authority; a
same-source journal, changed dimension, changed rule outcome, changed approval artifact, or changed
lot selection is an idempotency collision rather than a replay.
The existing factor-paydown calculator remains one source-specific projection producer; it no longer
defines the boundary for other asset accounting event kinds. Compatibility bridges and backtesting
adjusters may call projection calculators, but they do not establish production posting authority.

The Slice 1 semantic alignment is additive and transport-compatible. Slice 2 added only the planned
Asset Operations role, book-position, and append-only economic-state projection tables needed for
production candidate re-resolution. Slice 3 places a dedicated store contract over those tables:
effective-dated security/book lookup, book-position lookup, transactional expected-version writes,
append-only state history, and fail-closed role, overlap, cross-book, owner, and date validation.
The shared Asset Operations read service composes durable typed history with the existing
security-scoped projection and never treats it as a balance fact.

The event-spine slice adds append-only projection versions and a journal-backed posted-impact link;
it does not migrate existing Security Master, direct-lending, portfolio, fund-account, or
asset-family records and does not create a parallel balance store. Shared read models resolve the
typed spine and immutable journal by ledger book plus source event, and expose the same truthful
lineage to the active browser and WPF workstation lanes without moving policy into either client.
The projection store independently resolves the asserted Posted impact from the durable journal
store before accepting it. Acquisition and disposal use one serializable journal-plus-lot
transaction. Tax lots and mutation evidence retain immutable Security Master and book-position
identity; disposal compare-and-swap rechecks lot version, open quantity, unit cost, selected cost
basis, asset account, journal relief, policy revision, and evidence before commit. Corrections name
the exact prior event version, posted journal, and mutation batch when applicable.

This bounded slice fails closed when asset transaction currency differs from the ledger-book
functional currency. Foreign-currency asset accounting requires an explicit FX rate identity and
retained conversion evidence before it can enter this spine; clients must not infer a conversion.

## Implementation Map

| Concern | Owning Surface |
| --- | --- |
| Posting command DTOs | `src/Meridian.Contracts/Ledger/AccountingPostingCommandDtos.cs` |
| Accounting book and rule-pack references | `src/Meridian.Contracts/Ledger/AccountingBookContextDtos.cs` |
| Instrument role, position, economic-event, and projection lineage contracts | `src/Meridian.Contracts/AssetOperations/InstrumentPositionDtos.cs` |
| Canonical asset accounting event, lifecycle, posted-impact, and lot-intent contracts | `src/Meridian.Contracts/AssetOperations/AssetAccountingEventDtos.cs` |
| Complete retained evidence identity and fail-closed validator | `src/Meridian.Contracts/AssetOperations/RetainedEvidenceIdentityDto.cs` |
| Instrument and Asset Operations projections | `src/Meridian.Instruments/AssetOperations` |
| MBS factor-paydown calculator | `src/Meridian.Instruments/AssetOperations/FactorPaydownProjectionService.cs` |
| Durable role, position, and economic-state projections | `src/Meridian.Storage/AssetOperations/IInstrumentPositionProjectionStore.cs`, `src/Meridian.Storage/AssetOperations/Migrations/002_instrument_position_projections.sql`, `003_instrument_position_projection_guards.sql` |
| Append-only Asset Accounting Event Spine versions | `src/Meridian.Storage/AssetOperations/IAssetAccountingEventProjectionStore.cs`, `src/Meridian.Storage/AssetOperations/Migrations/004_asset_accounting_event_spine.sql` |
| Journal evidence metadata | `src/Meridian.Ledger/JournalEvidenceReference.cs`, `JournalEntryMetadata.cs` |
| Durable write validation | `src/Meridian.Storage/Ledger/AccountingPostingCommandValidator.cs`, `PostgresLedgerJournalStore.cs` |
| Source-backed journal draft commands | `src/Meridian.FinancialOperations/Ledger/AccountingJournalDraftService.cs` |
| Accounting-basis projection handoff | `src/Meridian.FinancialOperations/Ledger/AccountingPolicyService.cs` |
| Authoritative event-to-candidate spine | `src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.cs` |
| Atomic journal and tax-lot persistence | `src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AtomicTaxLots.cs`, `src/Meridian.Storage/Ledger/Migrations/V_ledger_027__atomic_tax_lot_posting.sql` |
| Direct-lending event postings | `src/Meridian.Application/DirectLending/LoanAccountingProjector.cs` |
| Private-capital fund-event projection | `src/Meridian.Ledger/PrivateCapitalFundEventLedgerProjector.cs` |
| Operations Continuity candidate gate | `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs` |
| Shared instrument-to-journal proof | `src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs` |

## Validation

Focused coverage lives in:

- `LedgerJournalStoreTests.PostingCommand_ApprovedCommand_NormalizesWriteMetadataAndEvidence`
- `LedgerJournalStoreTests.PostingCommand_PendingReviewerState_RejectsBeforeAppend`
- `AccountingJournalDraftServiceTests.Scenario_AccountingClose_SourceBackedAccrualDraft_ReturnsGovernedWrite`
- `LedgerIntegrationTests.AutomatedJournalDraftProjector_PreservesEventAccountingMetadataAndTypedEvidence`
- `FactorPaydownProjectionServiceTests.Project_ShouldCalculateGoldenMbsPrincipalAndTypedLineage`
- `AccountingPostingCandidateServiceTests.BuildCandidateAsync_MbsFactorPaydown_RecalculatesPersistedProjectionBeforeDrafting`
- `AssetAccountingEventSpineServiceTests`
- `AssetAccountingPostingEvidenceValidatorTests`
- `AtomicTaxLotJournalStoreTests`
- `WorkstationEndpointsTests.MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldExposePassportOperationsAndReportUsage`

Use the narrow test filter for this slice before broader ledger or Financial Operations validation.
