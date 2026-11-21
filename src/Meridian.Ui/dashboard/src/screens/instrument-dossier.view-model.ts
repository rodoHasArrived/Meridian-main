import type { BrokerageHouseholdPortfolio, ReconciliationBreakQueueItem, TradingPosition } from "@/types";
import type { AmountPassportReference } from "@/types/amount-passport";

export const INSTRUMENT_DOSSIER_TABS = ["reference", "positions", "ledger", "cases"] as const;
export type InstrumentDossierTab = typeof INSTRUMENT_DOSSIER_TABS[number];

export function readInstrumentDossierTab(value: string | null): InstrumentDossierTab {
  return INSTRUMENT_DOSSIER_TABS.includes(value as InstrumentDossierTab) ? value as InstrumentDossierTab : "positions";
}

export function sameInstrument(left: string | null | undefined, right: string) {
  return Boolean(left?.trim()) && left?.trim().toLowerCase() === right.trim().toLowerCase();
}

/** The ledger's InstrumentId is the retained Security Master Guid, not a ticker lookup. */
export function isLedgerInstrumentId(value: string) {
  return /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export interface InstrumentPositionRow {
  id: string;
  account: string;
  symbol: string;
  quantity: string | number | null;
  price: string | number | null;
  value: string | number | null;
  currency?: string;
  markStatus: string;
  markReason: string;
  quantityPassport?: AmountPassportReference;
  pricePassport?: AmountPassportReference;
  valuePassport?: AmountPassportReference;
}

/** Stable source identities only: equal ticker text cannot establish record lineage. */
export function instrumentPositions(
  instrumentId: string,
  accountId: string | null,
  brokerage: BrokerageHouseholdPortfolio | null | undefined,
  positions: TradingPosition[]
): { rows: InstrumentPositionRow[]; unlinkedCount: number } {
  if (brokerage) {
    const scoped = brokerage.positions.filter((row) => !accountId || sameInstrument(row.fundAccountId, accountId));
    return {
      rows: scoped.filter((row) => sameInstrument(row.security?.securityId, instrumentId)).map((row, index) => ({
        id: `${row.providerId}:${row.fundAccountId}:${row.positionId ?? index}`,
        account: row.externalAccountId || row.fundAccountId,
        symbol: row.symbol,
        quantity: row.amountPassports?.quantity?.origin === "unavailable" ? null : row.amountPassports?.quantity?.exactValue ?? row.quantity,
        price: row.amountPassports?.mark?.origin === "unavailable" ? null : row.amountPassports?.mark?.exactValue ?? row.marketPrice,
        value: row.amountPassports?.["market-value"]?.origin === "unavailable" ? null : row.amountPassports?.["market-value"]?.exactValue ?? row.marketValue,
        quantityPassport: row.amountPassports?.quantity,
        pricePassport: row.amountPassports?.mark,
        valuePassport: row.amountPassports?.["market-value"],
        currency: row.currency ?? undefined,
        markStatus: row.markFreshness?.status ?? "Unavailable",
        markReason: sourceMarkSummary(row.markFreshness)
      })),
      unlinkedCount: scoped.filter((row) => !row.security?.securityId).length
    };
  }
  const scoped = positions.filter((row) => !accountId || sameInstrument(row.markFreshness?.financialAccountId, accountId));
  return {
    rows: scoped.filter((row) => sameInstrument(row.markFreshness?.securityId, instrumentId)).map((row, index) => ({
      id: row.positionKey ?? `${row.markFreshness?.financialAccountId}:${index}`,
      account: row.markFreshness?.financialAccountId ?? "Not recorded",
      symbol: row.symbol,
      quantity: row.quantity,
      price: row.markPriceExact ?? row.markPrice,
      value: row.exposureExact ?? row.exposure,
      currency: row.currency ?? undefined,
      markStatus: row.markFreshness?.status ?? "Unavailable",
      markReason: sourceMarkSummary(row.markFreshness)
    })),
    unlinkedCount: positions.filter((row) => !row.markFreshness?.securityId).length
  };
}

export function instrumentCases(instrumentId: string, rows: ReconciliationBreakQueueItem[], accountId: string | null, periodId: string | null, ledgerBookId: string | null = null) {
  return rows.filter((row) => sameInstrument(row.instrumentId, instrumentId)
    && (!accountId || sameInstrument(row.fundAccountId, accountId))
    && (!periodId || sameInstrument(row.accountingPeriodId, periodId))
    && (!ledgerBookId || sameInstrument(row.ledgerBookId, ledgerBookId)));
}


function sourceMarkSummary(assessment: TradingPosition["markFreshness"]): string {
  if (!assessment) return "Source mark assessment is unavailable.";
  if (assessment.blockReason) return assessment.blockReason;
  return `Policy ${assessment.policyVersion || "not recorded"} · observed ${assessment.observedOn || "not recorded"} · valuation ${assessment.valuationDate || "not recorded"}`;
}
