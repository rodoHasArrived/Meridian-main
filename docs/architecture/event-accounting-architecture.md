# Event Accounting Architecture

**Owner:** Accounting and Ledger
**Scope:** Event-backed accounting postings, durable journal writes, and audit evidence
**Last updated:** 2026-06-20

## Purpose

Meridian treats accounting impact as the result of reviewed operational events. Source systems,
Financial Operations, direct-lending workflows, and accounting workbenches may prepare journal
candidates, but durable accounting impact is represented by append-only ledger journal entries with
posting metadata, evidence references, and storage-owned validation.

## Current Contract

- `AccountingPostingCommandDto` in `src/Meridian.Contracts/Ledger` is the shared posting intent
  envelope. It carries command identity, aggregate and period identity, effective and posting dates,
  idempotency key, source/correlation/causation ids, reviewer state, treasury context, correction
  lineage, action origin, and retained evidence references.
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
- Ledger projections are rebuildable views over durable journal facts. They may consume report-pack
  outputs, approval metadata, and evidence references, but they do not redefine posting policy.

## Implementation Map

| Concern | Owning Surface |
| --- | --- |
| Posting command DTOs | `src/Meridian.Contracts/Ledger/AccountingPostingCommandDtos.cs` |
| Journal evidence metadata | `src/Meridian.Ledger/JournalEvidenceReference.cs`, `JournalEntryMetadata.cs` |
| Durable write validation | `src/Meridian.Storage/Ledger/AccountingPostingCommandValidator.cs`, `PostgresLedgerJournalStore.cs` |
| Source-backed journal draft commands | `src/Meridian.FinancialOperations/Ledger/AccountingJournalDraftService.cs` |
| Accounting-basis projection handoff | `src/Meridian.FinancialOperations/Ledger/AccountingPolicyService.cs` |
| Direct-lending event postings | `src/Meridian.Application/DirectLending/LoanAccountingProjector.cs` |
| Private-capital fund-event projection | `src/Meridian.Ledger/PrivateCapitalFundEventLedgerProjector.cs` |
| Operations Continuity candidate gate | `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs` |

## Validation

Focused coverage lives in:

- `LedgerJournalStoreTests.PostingCommand_ApprovedCommand_NormalizesWriteMetadataAndEvidence`
- `LedgerJournalStoreTests.PostingCommand_PendingReviewerState_RejectsBeforeAppend`
- `AccountingJournalDraftServiceTests.Scenario_AccountingClose_SourceBackedAccrualDraft_ReturnsGovernedWrite`
- `LedgerIntegrationTests.AutomatedJournalDraftProjector_PreservesEventAccountingMetadataAndTypedEvidence`

Use the narrow test filter for this slice before broader ledger or Financial Operations validation.
