/** Reciprocal intercompany balance confirmation. Amounts arrive as exact decimal strings and are never recomputed here. */
export type IntercompanyConfirmationStatus = "Proposed" | "ConfirmedByPartyOne" | "ConfirmedByPartyTwo" | "Confirmed" | "Disputed" | "Resolved";
export type IntercompanyConfirmationAction = "Propose" | "Confirm" | "Dispute" | "Resolve";
export type IntercompanyConfirmationParty = "PartyOne" | "PartyTwo";

export interface IntercompanyConfirmationSide {
  entityId: string; ledgerBookId: string; transactionCurrency: string; transactionSignedBalance: string;
  functionalCurrency: string; functionalSignedBalance: string; scheduleContentHash: string; journalEntryIds: string[];
}

export interface IntercompanyConfirmationEvent {
  eventId: string; requestId: string; action: IntercompanyConfirmationAction; party: IntercompanyConfirmationParty | null;
  actor: string; recordedAt: string; balanceDigest: string; reason: string | null;
}

export interface IntercompanyBalanceConfirmation {
  confirmationId: string; tenantId: string; companyId: string | null; accountingArrangementId: string; intercompanyAgreementId: string;
  asOf: string; version: number; status: IntercompanyConfirmationStatus; partyOne: IntercompanyConfirmationSide; partyTwo: IntercompanyConfirmationSide;
  netTransactionBalance: string; balanceDigest: string; events: IntercompanyConfirmationEvent[];
  /** Set only when the two books belong to different companies of the tenant; each company acts on its own side. */
  partyOneCompanyId?: string | null; partyTwoCompanyId?: string | null;
}

/**
 * Pre-fills an ordinary journal-workbench draft correcting this book's side after a resolved dispute. The amount is an
 * exact decimal string in the agreement currency: positive debits the intercompany account, negative credits it.
 */
export interface IntercompanyConfirmationAdjustmentDraftRequest {
  requestId: string; party: IntercompanyConfirmationParty; expectedVersion: number; expectedBalanceDigest: string;
  role: "Receivable" | "Payable"; adjustmentAmount: string; offsetAccountNodeId: string; accountingDate: string; memo?: string | null;
}

/** The retained draft as returned by the server; only what this screen shows. */
export interface IntercompanyConfirmationAdjustmentDraft { journalEntryId: string; status: string; ledgerBookId: string | null; accountingDate: string }

export interface IntercompanyConfirmationFinding { code: string; message: string; accountingArrangementId: string; confirmationId: string | null }

export interface IntercompanyConfirmationSummary {
  ledgerBookId: string; asOf: string; confirmations: IntercompanyBalanceConfirmation[]; findings: IntercompanyConfirmationFinding[];
}

export type IntercompanyConfirmationClosePolicyStatus = "Proposed" | "Active" | "Rejected" | "Disabled";
export type IntercompanyConfirmationClosePolicyAction = "Propose" | "Approve" | "Reject" | "Disable";

/** A reviewed, opt-in close policy (ADR-032): while active, the listed confirmation findings block close for the book. */
export interface IntercompanyConfirmationClosePolicy {
  policyId: string; tenantId: string; companyId?: string | null; ledgerBookId: string; blockingCodes: string[];
  version: number; status: IntercompanyConfirmationClosePolicyStatus; activeFrom?: string | null;
  events: { eventId: string; requestId: string; action: IntercompanyConfirmationClosePolicyAction; actor: string; recordedAt: string; reason?: string | null }[];
}
