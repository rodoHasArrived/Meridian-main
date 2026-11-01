import type { BondCalculationContext, BondDecimal } from "./bond-analytics.types";

/** Kernel business-day conventions accepted by the schedule writer; the server refuses anything else. */
export const BUSINESS_DAY_CONVENTIONS = [
  "Unadjusted",
  "Following",
  "ModifiedFollowing",
  "Preceding",
  "ModifiedPreceding"
] as const;
export type BusinessDayConvention = (typeof BUSINESS_DAY_CONVENTIONS)[number];

export interface BondMigrationPreviewRequest {
  securityId: string;
  expectedVersion: number;
  context: BondCalculationContext;
  scheduleConventions: {
    couponRateUnit: "Percent" | "Fraction";
    couponCashBasis: "Level" | "DayCount";
    accrualAdjusted: boolean;
    businessDayConvention: BusinessDayConvention;
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
