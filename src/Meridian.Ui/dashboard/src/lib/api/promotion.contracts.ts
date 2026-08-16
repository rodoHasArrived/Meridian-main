export interface ApprovePromotionRequest {
  runId: string;
  /** Informational only; the server replaces this value with the authenticated actor. */
  approvedBy?: string;
  approvalReason: string;
  approvalChecklist?: string[];
  evidenceReferences?: string[];
  reviewNotes?: string;
  manualOverrideId?: string;
}

export const PAPER_PROMOTION_APPROVAL_CHECKLIST = [
  "DK1_TRUST_PACKET_REVIEWED",
  "RUN_LINEAGE_REVIEWED",
  "PORTFOLIO_LEDGER_CONTINUITY_REVIEWED",
  "RISK_CONTROLS_REVIEWED"
] as const;

export interface RejectPromotionRequest {
  runId: string;
  reason: string;
  rejectedBy?: string;
  reviewNotes?: string;
  manualOverrideId?: string;
}
