import type { BondCalculationContext, BondDecimal } from "./bond-analytics.types";

export interface BondMigrationPreviewRequest {
  securityId: string;
  expectedVersion: number;
  context: BondCalculationContext;
  scheduleConventions: {
    couponRateUnit: "Percent" | "Fraction";
    couponCashBasis: "Level" | "DayCount";
    accrualAdjusted: boolean;
    businessDayConvention: string;
    endOfMonthRule: boolean;
    paymentCalendars?: string[];
  };
}

export interface BondMigrationPreview {
  request: BondMigrationPreviewRequest;
  securityContentSha256: string;
  previewSha256: string;
  cashDifferences: Array<{
    paymentDate: string;
    previousPrincipal: BondDecimal;
    candidatePrincipal: BondDecimal;
    previousInterest: BondDecimal;
    candidateInterest: BondDecimal;
  }>;
  gaps: string[];
  canStage: boolean;
}

/** Displayed financial projections are never resubmitted. The server recomputes and checks both digests. */
export interface BondMigrationSubmitRequest {
  request: BondMigrationPreviewRequest;
  previewSha256: string;
  securityContentSha256: string;
  justification: string;
}
