/** Exact decimal text is the transport boundary; financial arithmetic remains server-owned. */
export interface AssetPositionMarketValuation {
  securityId: string;
  positionId: string;
  ledgerBookId: string;
  accountId: string;
  valuationDate: string;
  economicStateId: string;
  economicStateVersion: number;
  status: "Ready" | "Partial" | "Blocked";
  fundProfileId: string | null;
  positionVersion: number | null;
  currency: string;
  accountingBasis: string | null;
  positionSide: string | null;
  modelKey: string;
  modelVersion: string;
  calculationBasis: string;
  unrealizedPnlBasis: string;
  quantity: string | null;
  carryingAmount: string | null;
  price: string | null;
  priceUnit: "Unspecified" | "CurrencyPerUnit" | "PercentOfPar";
  marketValue: string | null;
  unrealizedPnl: string | null;
  observedAsOf: string | null;
  knowledgeAsOf: string | null;
  freshnessStatus: string;
  isStaleFallback: boolean | null;
  daysStale: number | null;
  selectedSource: string | null;
  selectionReceiptId: string | null;
  inputs: Array<{
    kind: string;
    reference: string;
    id?: string | null;
    version?: number | null;
    contentSha256?: string | null;
    asOf?: string | null;
    recordedAt?: string | null;
  }>;
  diagnostics: string[];
  blockers: Array<{ code: string; reason: string; metric: "All" | "UnrealizedPnl" | "MarketValue" }>;
}
