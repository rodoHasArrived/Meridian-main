import { apiGetJson, apiPostJson, type ApiRequestOptions } from "./http-client";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { BookReceiptRun } from "@/types/income-receipt-queue.types";

const decimalFields = new Set(["amount", "expectedAmount", "receivedAmount", "postedAmount", "remainingAmount", "quantity", "journalCashAmount"]);

function validateDecimals(value: unknown): void {
  if (Array.isArray(value)) { value.forEach(validateDecimals); return; }
  if (value === null || typeof value !== "object") return;
  for (const [key, entry] of Object.entries(value)) {
    if (decimalFields.has(key) && !(key === "journalCashAmount" && entry === null) && !isTaxDecimalText(entry)) {
      throw new Error("Receipt evidence contains a non-exact amount. Reload authoritative evidence before continuing.");
    }
    validateDecimals(entry);
  }
}

function checkedRun(value: BookReceiptRun, ledgerBookId: string, runId?: string, asOfDate?: string): BookReceiptRun {
  if (!value || value.scope?.ledgerBookId !== ledgerBookId || !value.runId || !Array.isArray(value.items)
      || !Array.isArray(value.coverage) || !value.sourceSnapshot || !/^\d{4}-\d{2}-\d{2}$/.test(value.asOfDate)
      || (runId !== undefined && value.runId !== runId) || (asOfDate !== undefined && value.asOfDate !== asOfDate)) {
    throw new Error("Receipt evidence does not match the selected book or retained run.");
  }
  validateDecimals(value);
  return value;
}

export async function getBookReceiptRuns(ledgerBookId: string, options: ApiRequestOptions = {}): Promise<BookReceiptRun[]> {
  const runs = await apiGetJson<BookReceiptRun[]>(`${UI_API_ROUTES.WorkstationBookReceiptRuns}?${new URLSearchParams({ ledgerBookId })}`, { ...options, allowDevelopmentFallback: false });
  if (!Array.isArray(runs)) throw new Error("Retained receipt runs are unavailable.");
  return runs.map(run => checkedRun(run, ledgerBookId));
}

export async function getBookReceiptRun(ledgerBookId: string, runId: string, options: ApiRequestOptions = {}): Promise<BookReceiptRun> {
  const run = await apiGetJson<BookReceiptRun>(`${UI_API_ROUTES.WorkstationBookReceiptRuns}/${encodeURIComponent(runId)}?${new URLSearchParams({ ledgerBookId })}`, { ...options, allowDevelopmentFallback: false });
  return checkedRun(run, ledgerBookId, runId);
}

export async function runBookReceiptQueue(ledgerBookId: string, asOfDate: string, options: ApiRequestOptions = {}): Promise<BookReceiptRun> {
  const run = await apiPostJson<BookReceiptRun>(UI_API_ROUTES.WorkstationBookReceiptRuns, { ledgerBookId, asOfDate }, { ...options, allowDevelopmentFallback: false });
  return checkedRun(run, ledgerBookId, undefined, asOfDate);
}
