import type { AccountingBasisKind, JournalEntryLifecycleActionRequest } from "@/types";

export interface AssetApprovalReceipt {
  approvalId: string;
  approver: string;
  approvedAtUtc: string;
  candidateFingerprint: string;
  subjectId: string;
  canonicalContentBase64: string;
  contentHashSha256: string;
}

/** Camel-case ApprovalDecision bytes written with JsonSerializerDefaults.Web by AssetApproval.cs. */
export interface AssetApprovalDecision {
  protocolVersion: number;
  decision: string;
  journalEntryId: string;
  approvalId: string;
  actor: string;
  approvedAtUtc: string;
  tenantId: string | null;
  companyId: string | null;
  fundProfileId: string;
  ledgerBookId: string;
  periodId: string;
  accountingBasis: AccountingBasisKind;
  eventId: string;
  eventVersion: number;
  candidateFingerprint: string;
  subjectId: string;
  notes: string | null;
}

export interface AssetPostedJournalReceipt {
  journalEntryId: string;
  ledgerBookId: string;
  accountingBasis: AccountingBasisKind;
  periodId: string;
  aggregateId: string;
  commandId?: string | null;
  sourceEventId?: string | null;
  correlationId?: string | null;
  globalSequence?: number | null;
  postedAtUtc?: string | null;
  idempotencyKey?: string | null;
}

export interface AssetWorkflowScope {
  journalEntryId: string;
  fundProfileId: string;
  ledgerBookId: string;
  periodId: string;
  draftVersion: number;
  /** Local assertion for canonical posting receipts; not a capability query parameter. */
  accountingBasis?: AccountingBasisKind;
}

/** Authenticated server projection; absent or unsupported protocols grant no command capability. */
export interface AssetAccountingDraftCapabilities extends AssetWorkflowScope {
  protocolVersion: number;
  authenticatedActor?: string | null;
  candidateFingerprint: string;
  canApprove: boolean;
  canPost: boolean;
  approveBlockedReason: string | null;
  postBlockedReason: string | null;
  approval: AssetApprovalReceipt | null;
  /** Derived only after receipt bytes, metadata and scope have been verified by the API client. */
  approvalDecision?: AssetApprovalDecision | null;
  hasPendingPost: boolean;
  pendingPostCommand: JournalEntryLifecycleActionRequest | null;
  postedJournal: AssetPostedJournalReceipt | null;
}
