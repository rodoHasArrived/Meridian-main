export interface MarketDataInputEvidence {
  providerId: string | null;
  eventReference: string | null;
  observedAt: string | null;
  datasetId: string | null;
  backfillRevisionId: string | null;
  contentHash: string | null;
  repaired: boolean | null;
  gapFilled: boolean | null;
  repairReceiptId: string | null;
  qualityScore: number | null;
  qualityThreshold?: number | null;
  evidenceIssue?: string | null;
  qualityAssessedAt: string | null;
  assessmentFrom: string | null;
  assessmentTo: string | null;
}

export interface MarketDataQualityWarning {
  kind: string;
  message: string;
  affectedFillCount: number;
  evidence: MarketDataInputEvidence[];
}
