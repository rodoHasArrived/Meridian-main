import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { LedgerJournalTaxResults } from "@/types/ledger-tax-results";

export interface LedgerTaxResultScope {
  ledgerBookId: string;
  periodId: string;
  journalEntryId: string;
}

/** Reads retained results; this request never computes or changes disposal tax treatment. */
export async function getLedgerJournalEntryTaxResults(scope: LedgerTaxResultScope, options: ApiRequestOptions = {}) {
  const result = await apiGetJson<LedgerJournalTaxResults>(
    UI_API_ROUTES.LedgerJournalEntryTaxResults
      .replace("{periodId:guid}", encodeURIComponent(scope.periodId))
      .replace("{journalEntryId:guid}", encodeURIComponent(scope.journalEntryId)),
    { ...options, allowDevelopmentFallback: false }
  );
  const sameId = (actual: string, expected: string) => typeof actual === "string" && actual.toLowerCase() === expected.toLowerCase();
  if (!sameId(result.ledgerBookId, scope.ledgerBookId)
    || !sameId(result.periodId, scope.periodId)
    || !sameId(result.journalEntryId, scope.journalEntryId)
    || !Array.isArray(result.disposals)
    || result.disposals.some((disposal) => !sameId(disposal.journalEntryId, scope.journalEntryId))) {
    throw new Error("Retained tax results do not match the selected book, period and journal entry.");
  }
  const nullableDecimal = (value: unknown) => value === null || isTaxDecimalText(value);
  if (result.disposals.some((disposal) =>
    ![disposal.economicGainOrLoss, disposal.recognizedGainOrLoss, disposal.deferredLoss].every(nullableDecimal)
    || !Array.isArray(disposal.parcels)
    || disposal.parcels.some((parcel) =>
      ![parcel.quantity, parcel.proceeds, parcel.costBasis, parcel.economicGainOrLoss].every(isTaxDecimalText)
      || ![parcel.recognizedGainOrLoss, parcel.deferredLoss].every(nullableDecimal)))) {
    throw new Error("Retained tax amounts and quantities must be exact plain decimal text.");
  }
  return result;
}
