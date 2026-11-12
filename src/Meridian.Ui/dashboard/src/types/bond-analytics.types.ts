/** Shared bond analytics read contract. Monetary results are per 100 face; rates are annual fractions. */
export type BondDecimal = number | string;
export type BondYieldConvention = "NominalPeriodic" | "AnnualEffective" | "Simple";

export interface BondCalculationContext {
  asOf: string;
  settlementDate?: string | null;
  knownAt?: string | null;
  securityVersion?: number | null;
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
  yieldToWorst: BondDecimal | null;
  cashFlows: Array<{
    flowId: string;
    paymentDate: string;
    accrualStart: string;
    accrualEnd: string;
    interest: BondDecimal;
    principal: BondDecimal;
    timeInYears: BondDecimal;
  }>;
  exerciseYields: Array<{
    kind: string;
    exerciseDate: string;
    redemptionPrice: BondDecimal;
    yield: BondDecimal;
    latestNoticeDate?: string | null;
  }>;
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
  knownAt?: string;
  cleanPrice?: string;
  yield?: string;
  yieldConvention?: BondYieldConvention;
  selectionReceiptId?: string;
  priceAccountId?: string;
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
