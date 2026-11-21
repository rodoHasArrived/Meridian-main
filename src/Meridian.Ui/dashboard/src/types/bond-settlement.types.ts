import type { BondCalculationContext, BondCalculationInput, BondDecimal, BondYieldConvention } from "./bond-analytics.types";

/** Decimal inputs remain text until the server reads them into System.Decimal. */
export interface BondSettlementRequest {
  analytics: {
    securityId: string;
    context: BondCalculationContext;
    cleanPrice: string;
    yieldConvention: BondYieldConvention;
  };
  originalFace: string;
  bookedFactor: string;
  fees: string;
  observedDirtyCash: string;
  sourceEvidenceId: string;
  cashAccountId: string;
  accruedInterestAccountId: string;
  feesAccountId: string | null;
}

export interface BondSettlementResult {
  isBlocked: boolean;
  blockers: Array<{ code: string; reason: string }>;
  settlement: null | {
    securityId: string;
    currency: string;
    modelVersion: string;
    context: BondCalculationContext;
    yieldConvention: BondYieldConvention;
    originalFace: BondDecimal;
    bookedFactor: BondDecimal;
    cleanPricePer100: BondDecimal;
    accruedInterestPer100: BondDecimal;
    cleanConsideration: BondDecimal;
    purchasedAccruedInterest: BondDecimal;
    fees: BondDecimal;
    dirtyConsideration: BondDecimal;
    observedDirtyCash: BondDecimal;
    sourceEvidenceId: string;
    cashAccountId: string;
    accruedInterestAccountId: string;
    feesAccountId: string | null;
    inputs: BondCalculationInput[];
  };
}
