import type { AssetPositionMarketValuation } from "@/types/asset-market-valuation";
export const marketValuationQuery = {
  positionId: "position-1",
  ledgerBookId: "book-1",
  accountId: "account-1",
  valuationDate: "2026-11-20",
  economicStateId: "state-1",
  economicStateVersion: 2
};
export const marketValuationFixture: AssetPositionMarketValuation = {
  securityId: "sec-aapl",
  ...marketValuationQuery,
  status: "Ready",
  fundProfileId: "fund-1",
  positionVersion: 1,
  currency: "USD",
  accountingBasis: "Gaap",
  positionSide: "Long",
  modelKey: "quoted-price",
  modelVersion: "1",
  calculationBasis: "Retained share quantity × selected currency-per-unit price.",
  unrealizedPnlBasis:
    "Market value less retained carrying amount. This is a mark-to-carrying comparison, not total-return or tax-cost P/L.",
  quantity: "10",
  carryingAmount: "1234.50",
  price: "140.60",
  priceUnit: "CurrencyPerUnit",
  marketValue: "1406.00",
  unrealizedPnl: "171.50",
  observedAsOf: "2026-11-20T16:00:00Z",
  knowledgeAsOf: "2026-11-21T10:00:00Z",
  freshnessStatus: "Current",
  isStaleFallback: false,
  daysStale: 0,
  selectedSource: "Synthetic custodian",
  selectionReceiptId: "receipt-1",
  inputs: [{ kind: "PriceSelectionReceipt", reference: "receipt-1", contentSha256: "a".repeat(64) }],
  diagnostics: [],
  blockers: []
};
