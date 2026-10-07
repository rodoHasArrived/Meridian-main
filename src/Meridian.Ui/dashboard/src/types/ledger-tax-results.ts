/** Server-owned retained disposal results. Browser clients format these values only. */
export interface LedgerDisposalTaxParcel {
  lotId: string;
  acquiredDate: string;
  holdingPeriodStart: string;
  holdingPeriodDays: number;
  holdingPeriodCarried: boolean;
  character: "ShortTerm" | "LongTerm";
  quantity: number;
  proceeds: number;
  costBasis: number;
  economicGainOrLoss: number;
  recognizedGainOrLoss: number | null;
  deferredLoss: number | null;
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
  economicGainOrLoss: number | null;
  recognizedGainOrLoss: number | null;
  deferredLoss: number | null;
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
