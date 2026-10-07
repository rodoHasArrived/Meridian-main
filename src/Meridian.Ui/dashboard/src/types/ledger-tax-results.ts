/** Server-owned retained disposal results. Quantities and amounts are exact plain decimal text. */
export interface LedgerDisposalTaxParcel {
  lotId: string;
  acquiredDate: string;
  holdingPeriodStart: string;
  holdingPeriodDays: number;
  holdingPeriodCarried: boolean;
  character: "ShortTerm" | "LongTerm";
  quantity: string;
  proceeds: string;
  costBasis: string;
  economicGainOrLoss: string;
  recognizedGainOrLoss: string | null;
  deferredLoss: string | null;
}

export interface LedgerDisposalTaxResult {
  mutationBatchId: string;
  journalEntryId: string;
  saleDate: string;
  accountName: string;
  symbol: string | null;
  reliefMethod: string;
  policyRevision: string | null;
  recordedAt: string | null;
  state: "Settled" | "Provisional" | "MissingEvidence";
  stateReason: string;
  canChange: boolean;
  reEvaluationRequired: boolean;
  replacementWindowEnd: string | null;
  character: "ShortTerm" | "LongTerm" | "Mixed" | null;
  economicGainOrLoss: string | null;
  recognizedGainOrLoss: string | null;
  deferredLoss: string | null;
  parcels: LedgerDisposalTaxParcel[];
}

export interface LedgerJournalTaxResults {
  ledgerBookId: string;
  periodId: string;
  journalEntryId: string;
  functionalCurrency: string;
  evaluatedAt: string;
  evidenceState: "Available" | "MissingEvidence";
  message: string;
  disposals: LedgerDisposalTaxResult[];
}
