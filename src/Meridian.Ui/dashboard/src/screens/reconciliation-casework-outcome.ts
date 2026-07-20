import type {
  InvestmentAccountingTransactionLabPreview,
  InvestmentAccountingTransactionLabRequest,
  LedgerTrialBalanceLine,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ReconciliationCaseworkOperationResult,
  ResolveReconciliationBreakRequest,
  ReviewReconciliationBreakRequest,
  StatementRunSummary
} from "@/types";

export interface AccountingReconciliationServices {
  getBreakQueue: () => Promise<ReconciliationBreakQueueItem[]>;
  reviewBreak: (request: ReviewReconciliationBreakRequest) => Promise<ReconciliationCaseworkOperationResult>;
  resolveBreak: (request: ResolveReconciliationBreakRequest) => Promise<ReconciliationCaseworkOperationResult>;
  getTrialBalance: (runId: string) => Promise<LedgerTrialBalanceLine[]>;
  getCalibrationSummary: () => Promise<ReconciliationCalibrationSummary>;
  getStatementRuns: () => Promise<StatementRunSummary[]>;
  getStatementRun: (runId: string) => Promise<StatementRunSummary>;
  previewTransactionLab: (request: InvestmentAccountingTransactionLabRequest) => Promise<InvestmentAccountingTransactionLabPreview>;
}

export function requireSuccessfulReconciliationCasework(
  operation: ReconciliationCaseworkOperationResult
): ReconciliationBreakQueueItem {
  const succeeded = operation.outcome.state === "Succeeded"
    || operation.outcome.state === "CompletedWithWarnings";
  if (!succeeded || !operation.item) {
    throw new Error(
      operation.error
      ?? operation.outcome.issues[0]?.message
      ?? `Reconciliation casework ended in ${operation.outcome.state}.`
    );
  }

  return operation.item;
}
