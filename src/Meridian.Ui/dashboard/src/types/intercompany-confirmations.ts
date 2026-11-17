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
}

export interface IntercompanyConfirmationFinding { code: string; message: string; accountingArrangementId: string; confirmationId: string | null }

export interface IntercompanyConfirmationSummary {
  ledgerBookId: string; asOf: string; confirmations: IntercompanyBalanceConfirmation[]; findings: IntercompanyConfirmationFinding[];
}
