/** Shared valuation policy assessment; clients must not recompute mark eligibility. */
export interface MarkFreshnessAssessmentDto {
  symbol: string;
  securityId?: string | null;
  financialAccountId?: string | null;
  valuationDate: string;
  observedOn?: string | null;
  ageDays?: number | null;
  policyVersion: string;
  status: string;
  blockReason?: string | null;
}

export interface ValuationFreshnessPreviewDto {
  policyVersion: string;
  assessedPositionCount: number;
  blockedPositionCount: number;
  affectedValuationCount: number;
  positions: MarkFreshnessAssessmentDto[];
  evaluatedAtUtc: string;
}
