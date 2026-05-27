export type CloseWorkflowState = 'open' | 'validating' | 'blocked' | 'closed';

export interface TrialBalanceRow {
  accountCode: string;
  debit: number;
  credit: number;
  net: number;
}

export interface RollForwardRow {
  accountCode: string;
  openingBalance: number;
  activity: number;
  translationAdjustment: number;
  closingBalance: number;
}

export interface AuditDrillThroughRow {
  journalEntryId: string;
  sourceEventId: string;
  approvalId: string;
}
