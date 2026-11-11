/** Shared run-ledger dimension and economic-time query contracts. */
export interface LedgerDimensionQueryOptions {
  fundId?: string;
  entityId?: string;
  sleeveId?: string;
  strategyId?: string;
  portfolioId?: string;
  accountId?: string;
  investorId?: string;
  capitalAccountId?: string;
  instrumentId?: string;
  taxLotId?: string;
  costCenterId?: string;
  counterpartyId?: string;
  externalGlDimensions?: Record<string, string | null | undefined>;
}

export interface LedgerJournalQueryOptions extends LedgerDimensionQueryOptions {
  asOf?: string;
  from?: string;
  to?: string;
}

export interface LedgerTrialBalanceQueryOptions extends LedgerDimensionQueryOptions {
  asOf?: string;
  accountType?: string;
}
