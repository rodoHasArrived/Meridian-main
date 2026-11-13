/** Shared bond analytics read contract. Monetary results are per 100 face; rates are annual fractions. */
export type BondDecimal = number | string;
export type BondYieldConvention = "NominalPeriodic" | "AnnualEffective" | "Simple";
export type BondCalculationVersion = "bond-analytics-v1" | "bond-analytics-v2" | "bond-analytics-v3";

export interface BondExerciseScenario {
  kind: string;
  exerciseDate: string;
  redemptionPrice: BondDecimal;
  yield: BondDecimal;
  latestNoticeDate?: string | null;
  earliestNoticeDate?: string | null;
}

export interface BondCalculationContext {
  asOf: string;
  settlementDate?: string | null;
  knownAt?: string | null;
  securityVersion?: number | null;
  observedFixingReceiptIds?: string[] | null;
  curves?: Array<{
    role: string;
    curveId: string;
    curveVersionId: string;
  }> | null;
}

export interface BondCalculationInput {
  kind: string;
  key: string;
  version?: number | null;
  contentSha256?: string | null;
  versionId?: string | null;
  asOf?: string | null;
  recordedAt?: string | null;
}

export interface BondAnalyticsResult {
  securityId: string;
  currency: string;
  modelVersion: string;
  context: BondCalculationContext;
  yieldConvention?: BondYieldConvention | null;
  cleanPrice: BondDecimal | null;
  dirtyPrice: BondDecimal | null;
  accruedInterest: BondDecimal | null;
  yieldToMaturity: BondDecimal | null;
  currentYield: BondDecimal | null;
  macaulayDuration: BondDecimal | null;
  modifiedDuration: BondDecimal | null;
  convexity: BondDecimal | null;
  dv01: BondDecimal | null;
  /** Dirty-price change per 100 face for +1 bp coupon spread at unchanged projected-flow yield. */
  spreadDv01?: BondDecimal | null;
  yieldToWorst: BondDecimal | null;
  cashFlows: Array<{
    flowId: string;
    paymentDate: string;
    accrualStart: string;
    accrualEnd: string;
    interest: BondDecimal;
    principal: BondDecimal;
    timeInYears: BondDecimal;
    /** Server-selected fixing evidence; absent for v1/v2. Index and coupon rates are annual fractions. */
    floatingCoupon?: {
      resetDate: string;
      isObserved: boolean;
      indexName: string;
      indexRate: BondDecimal;
      spreadBps: BondDecimal;
      couponRate: BondDecimal;
      source: string;
    } | null;
  }>;
  exerciseYields: BondExerciseScenario[];
  redemptionScenarios?: BondExerciseScenario[];
  yieldToWorstScenarios?: BondExerciseScenario[];
  inputs: BondCalculationInput[];
  blockers: Array<{ code: string; reason: string }>;
  capabilities: Array<{
    operation: string;
    supported: boolean;
    reason?: string | null;
  }>;
  diagnostics: string[];
  isBlocked: boolean;
}

export interface BondAnalyticsQuery {
  asOf: string;
  settlementDate: string;
  calculationVersion?: BondCalculationVersion;
  knownAt?: string;
  cleanPrice?: string;
  yield?: string;
  yieldConvention?: BondYieldConvention;
  selectionReceiptId?: string;
  priceAccountId?: string;
  observedFixingReceiptIds?: string;
}

/** Exact retained provider observation and immutable operator-choice receipt. */
export interface BondRetainedObservation {
  securityId: string;
  selectedSource: string;
  goldenCopyPrice: BondDecimal;
  priceAsOf: string;
  selectionReceiptId: string;
  isStaleFallback: boolean;
  daysStale?: number | null;
  unit: string;
  quotation?: {
    basis: "Unspecified" | "Clean" | "Dirty";
    convention: "Unspecified" | "Per100Face" | "CurrencyPerUnit";
    status: "Unspecified" | "Bid" | "Ask" | "Mid" | "Evaluated" | "Trade";
    settlementDate: string;
    settlementBasis: string;
  } | null;
}
