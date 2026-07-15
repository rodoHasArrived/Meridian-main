# Event Accounting Architecture

**Owner:** Accounting and Ledger
**Scope:** Event-backed accounting postings, durable journal writes, and audit evidence
**Last updated:** 2026-07-12

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

- Posted journal entries are immutable accounting facts. Corrections must append adjustment,
  reversal, rebook, or restatement records and preserve the original source event and evidence chain.
- Material posting commands require `HumanOperator` action origin. Reviewed automation may prepare
  draft support, but storage rejects material commands that claim assistant or automation origin.
- Posting commands require a durable idempotency key and retained evidence or explicit operator
  rationale. Duplicate command, source-event, and normalized idempotency keys are also protected by
  the Postgres journal indexes.
- Reversal, rebook, and restatement command intents require source journal lineage before append.
- Candidate journals remain unposted review artifacts. Projection events, position economic state,
  and balance snapshots remain rebuildable operational views; none is a second accounting truth.
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

The governed MBS proof follows one path:

```text
factor evidence
-> holder role / book position
-> factor-paydown economic projection
-> Rules Studio posting candidate
-> independent human approval
-> immutable JournalEntry
-> ledger and report evidence
```

For typed factor events, Financial Operations re-reads the persisted Asset Operations role,
position, economic state, and projection lineage and recalculates the principal before Rules Studio
drafting. A submitted amount, event identity, book, position version, evidence set, or lineage that
does not match server state blocks candidate creation. The compatibility Security Master bridge and
backtesting adjuster call the same calculator, but neither compatibility path establishes production
posting authority.

The Slice 1 semantic alignment is additive and transport-compatible. Slice 2 added only the planned
Asset Operations role, book-position, and append-only economic-state projection tables needed for
production candidate re-resolution. Slice 3 places a dedicated store contract over those tables:
effective-dated security/book lookup, book-position lookup, transactional expected-version writes,
append-only state history, and fail-closed role, overlap, cross-book, owner, and date validation.
The shared Asset Operations read service composes durable typed history with the existing
security-scoped projection and never treats it as a balance fact.

These slices do not migrate existing Security Master, direct-lending, portfolio, fund-account, or
asset-family records and do not create a parallel balance store or a projection-event-to-journal
link table. Shared read models query the immutable journal by ledger book plus indexed source event
and expose the same lineage to the active browser and WPF workstation lanes without moving policy
into either client.

## Implementation Map

| Concern | Owning Surface |
| --- | --- |
| Posting command DTOs | `src/Meridian.Contracts/Ledger/AccountingPostingCommandDtos.cs` |
| Accounting book and rule-pack references | `src/Meridian.Contracts/Ledger/AccountingBookContextDtos.cs` |
| Instrument role, position, economic-event, and projection lineage contracts | `src/Meridian.Contracts/AssetOperations/InstrumentPositionDtos.cs` |
| Instrument and Asset Operations projections | `src/Meridian.Instruments/AssetOperations` |
| MBS factor-paydown calculator | `src/Meridian.Instruments/AssetOperations/FactorPaydownProjectionService.cs` |
| Durable role, position, and economic-state projections | `src/Meridian.Storage/AssetOperations/IInstrumentPositionProjectionStore.cs`, `src/Meridian.Storage/AssetOperations/Migrations/002_instrument_position_projections.sql`, `003_instrument_position_projection_guards.sql` |
| Journal evidence metadata | `src/Meridian.Ledger/JournalEvidenceReference.cs`, `JournalEntryMetadata.cs` |
| Durable write validation | `src/Meridian.Storage/Ledger/AccountingPostingCommandValidator.cs`, `PostgresLedgerJournalStore.cs` |
| Source-backed journal draft commands | `src/Meridian.FinancialOperations/Ledger/AccountingJournalDraftService.cs` |
| Accounting-basis projection handoff | `src/Meridian.FinancialOperations/Ledger/AccountingPolicyService.cs` |
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
- `WorkstationEndpointsTests.MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldExposePassportOperationsAndReportUsage`

Use the narrow test filter for this slice before broader ledger or Financial Operations validation.
