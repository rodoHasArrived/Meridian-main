export type ReportWriterSampleDataset = "portfolioPositions" | "ledgerFacts" | "cashLadder";

export const reportWriterSamples: Record<ReportWriterSampleDataset, {
  label: string;
  rows: Record<string, string>[];
}> = {
  portfolioPositions: {
    label: "Portfolio positions",
    rows: [
      { sector: "Technology", strategy: "Core", security: "ALP", country: "US", marketValue: "100", pnl: "18", grossExposure: "100", netExposure: "100", weightPct: "40" },
      { sector: "Energy", strategy: "Core", security: "BRV", country: "GB", marketValue: "50", pnl: "-6", grossExposure: "50", netExposure: "50", weightPct: "20" },
      { sector: "Technology", strategy: "Hedge", security: "CRN", country: "US", marketValue: "75", pnl: "4", grossExposure: "75", netExposure: "-75", weightPct: "30" },
      { sector: "Financials", strategy: "Hedge", security: "DLT", country: "CA", marketValue: "25", pnl: "0", grossExposure: "25", netExposure: "-25", weightPct: "10" }
    ]
  },
  ledgerFacts: {
    label: "Ledger facts",
    rows: [
      { account: "Cash", accountType: "Asset", currency: "USD", debit: "1000", credit: "0", netAmount: "1000" },
      { account: "Income", accountType: "Revenue", currency: "USD", debit: "0", credit: "1000", netAmount: "-1000" },
      { account: "Expense", accountType: "Expense", currency: "EUR", debit: "250", credit: "0", netAmount: "250" },
      { account: "Cash", accountType: "Asset", currency: "EUR", debit: "0", credit: "250", netAmount: "-250" }
    ]
  },
  cashLadder: {
    label: "Cash ladder",
    rows: [
      { bucket: "Today", currency: "USD", cash: "1250", pendingSettlement: "0" },
      { bucket: "Tomorrow", currency: "USD", cash: "900", pendingSettlement: "350" },
      { bucket: "Today", currency: "EUR", cash: "650", pendingSettlement: "250" },
      { bucket: "Tomorrow", currency: "EUR", cash: "300", pendingSettlement: "0" }
    ]
  }
};
