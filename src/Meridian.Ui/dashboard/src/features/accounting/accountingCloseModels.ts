export type CloseWorkflowState = 'open' | 'validating' | 'blocked' | 'closed';

export interface TrialBalanceRow {
  accountCode: string;
  debit: number;
  credit: number;
  net: number;
  sourceEventIds?: string[];
  approvalIds?: string[];
}

export interface RollForwardRow {
  accountCode: string;
  openingBalance: number;
  activity: number;
  translationAdjustment: number;
  closingBalance: number;
  sourceEventIds?: string[];
  approvalIds?: string[];
}

export interface AuditDrillThroughRow {
  journalEntryId: string;
  sourceEventId: string;
  approvalId: string;
  accountingPeriod?: string | null;
  description?: string | null;
  accountCodes?: string[];
}

export interface CloseEvidenceCheck {
  checkId: string;
  label: string;
  required: boolean;
  passed: boolean;
  sourceEventId: string;
  approvalId: string;
  detail: string;
}

export interface ClosePeriodProjection {
  ledgerId: string;
  period: string;
  state: CloseWorkflowState;
  blockers: string[];
  trialBalanceBalanced: boolean;
  evidenceChecks: CloseEvidenceCheck[];
}
