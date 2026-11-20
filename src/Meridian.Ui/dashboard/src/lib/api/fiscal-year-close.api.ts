import { apiGetJson, apiPostJson } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { routeWithParam } from "@/lib/workstation-endpoints";
import type { AccountingBasisKind, LedgerDimensionSet, LedgerPeriodStatus } from "@/types";

export interface FiscalYearCloseBalance {
  accountName: string;
  accountType: string;
  symbol: string | null;
  financialAccountId: string | null;
  dimensions: LedgerDimensionSet | null;
  debitTotal: string;
  creditTotal: string;
  balance: string;
}

export interface FiscalYearClosePlan {
  ledgerBookId: string;
  fiscalYear: number;
  fiscalYearStart: string | null;
  fiscalYearEnd: string | null;
  book?: { functionalCurrency: string; accountingBasis: AccountingBasisKind; accountingPolicyId: string; accountingPolicyVersion: string } | null;
  isReady: boolean;
  canFinalizeFiscalYearClose?: boolean;
  blockers: { code: string; message: string; periodId: string | null }[];
  periods: {
    periodId: string;
    label: string;
    startDate: string;
    endDate: string;
    status: LedgerPeriodStatus;
    snapshotVersion: number | null;
    snapshotHashSha256: string | null;
  }[];
  preClosingTrialBalance: FiscalYearCloseBalance[];
  postedClosingTransfers: FiscalYearCloseBalance[];
  proposedClosingTransfers: FiscalYearCloseBalance[];
  postClosingTrialBalance: FiscalYearCloseBalance[];
  nextYearOpeningBalances: FiscalYearCloseBalance[];
  reportedNetIncome: string;
  openingEquity: string;
  preClosingEquity: string;
  postClosingEquity: string;
  planHashSha256: string | null;
}

export interface FiscalYearCloseReceipt {
  receiptId: string;
  supersedesReceiptId: string | null;
  recordedAt: string;
  authorizedBy: string;
  controllerRole: string;
  reason: string;
  plan: FiscalYearClosePlan;
  functionalCurrency: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  closingJournalEntryIds: string[];
}

export interface FiscalYearCloseScope {
  ledgerBookId: string;
  fiscalYear: number;
}

export interface FinalizeFiscalYearCloseRequest {
  commandId: string;
  expectedPlanHashSha256: string;
  reason: string;
  supersedesReceiptId: string | null;
}

function scopedRoute(route: string, scope: FiscalYearCloseScope) {
  return routeWithParam(routeWithParam(route, "ledgerBookId", scope.ledgerBookId), "fiscalYear", String(scope.fiscalYear));
}

function assertPlanScope(plan: FiscalYearClosePlan, scope: FiscalYearCloseScope) {
  if (plan?.ledgerBookId?.toLowerCase() !== scope.ledgerBookId.toLowerCase() || plan.fiscalYear !== scope.fiscalYear) {
    throw new Error("The fiscal-year plan does not match the selected book and year.");
  }
  if (plan.isReady && (!plan.planHashSha256 || plan.blockers.length > 0)) {
    throw new Error("The fiscal-year plan is missing verification evidence.");
  }
}

function assertReceipt(receipt: FiscalYearCloseReceipt, scope: FiscalYearCloseScope, id?: string) {
  assertPlanScope(receipt?.plan, scope);
  if (!receipt.plan.isReady || !receipt.receiptId || (id && receipt.receiptId.toLowerCase() !== id.toLowerCase())) {
    throw new Error("The retained receipt does not match the requested close.");
  }
}

export async function getFiscalYearClosePlan(scope: FiscalYearCloseScope, signal?: AbortSignal) {
  const plan = await apiGetJson<FiscalYearClosePlan>(scopedRoute(UI_API_ROUTES.LedgerFiscalYearClosePlan, scope), {
    signal,
    allowDevelopmentFallback: false
  });
  assertPlanScope(plan, scope);
  return plan;
}

export async function getFiscalYearCloseReceipt(scope: FiscalYearCloseScope, signal?: AbortSignal, receiptId?: string) {
  const route = scopedRoute(UI_API_ROUTES.LedgerFiscalYearCloseReceipts, scope);
  try {
    const receipt = await apiGetJson<FiscalYearCloseReceipt>(
      receiptId ? `${route}?${new URLSearchParams({ receiptId })}` : route,
      { signal, allowDevelopmentFallback: false }
    );
    assertReceipt(receipt, scope, receiptId);
    return receipt;
  } catch (error) {
    if (!receiptId && isApiError(error) && error.status === 404) return null;
    throw error;
  }
}

export async function finalizeFiscalYearClose(scope: FiscalYearCloseScope, request: FinalizeFiscalYearCloseRequest, signal?: AbortSignal) {
  const receipt = await apiPostJson<FiscalYearCloseReceipt>(
    scopedRoute(UI_API_ROUTES.LedgerFiscalYearCloseReceipts, scope),
    request,
    { signal, allowDevelopmentFallback: false }
  );
  assertReceipt(receipt, scope, request.commandId);
  if (receipt.plan.planHashSha256 !== request.expectedPlanHashSha256 ||
      (receipt.supersedesReceiptId ?? null) !== request.supersedesReceiptId) {
    throw new Error("The retained receipt does not match the reviewed plan and predecessor.");
  }
  return receipt;
}
