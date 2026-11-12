import type { BondCalculationContext, BondCalculationInput, BondDecimal } from "./bond-analytics.types";
import type { PortfolioCashLadder } from "./portfolio-cash-ladder.types";

export interface BondWorkflowRequest {
  securityId: string;
  context: BondCalculationContext;
  originalFace: BondDecimal;
  cleanPricePer100?: BondDecimal | null;
  quoteSettlementDate?: string | null;
  fees: BondDecimal;
  priceBasis: "NominalClean" | "RealClean";
  rateShiftBps: BondDecimal;
  spreadShiftBps: BondDecimal;
  quoteObservationId?: string | null;
  priceSelectionReceiptId?: string | null;
  accountId?: string | null;
  ratePillars?: Array<{ date: string; shiftBps: BondDecimal }>;
  observedDirtyCash?: BondDecimal | null;
  confirmationEvidenceReference?: string | null;
}
export interface BondWorkflowResult {
  securityId: string;
  currency: string;
  modelVersion: string;
  context: BondCalculationContext;
  originalFace: BondDecimal;
  currentFace: BondDecimal | null;
  accruedInterest: BondDecimal | null;
  cleanConsideration: BondDecimal | null;
  dirtyConsideration: BondDecimal | null;
  fees: BondDecimal;
  totalCash: BondDecimal | null;
  presentValue: BondDecimal | null;
  yield: BondDecimal | null;
  calibratedSpread: BondDecimal | null;
  dv01: BondDecimal | null;
  cs01: BondDecimal | null;
  duration: BondDecimal | null;
  cashFlows: Array<{
    paymentDate: string; accrualStart: string; accrualEnd: string;
    interest: BondDecimal; principal: BondDecimal; outstandingPrincipal: BondDecimal;
    indexRatio: BondDecimal; isForecast: boolean;
    observations: Array<{ accrualDate: string; observationDate: string; days: number; rate: BondDecimal; isObserved: boolean; source: string }>;
  }>;
  inputs: BondCalculationInput[];
  issues: Array<{ operation: string; code: string; reason: string; blocksCalculation: boolean }>;
  metrics: Array<{ name: string; available: boolean; reason?: string | null }>;
  isBlocked: boolean;
  quoteObservationId?: string | null;
  priceSelectionReceiptId?: string | null;
  providerYield?: BondDecimal | null;
  providerYieldConvention?: string | null;
  yieldToWorst?: BondDecimal | null;
  exerciseScenarios?: Array<{ kind: string; exerciseDate: string; redemptionPrice: BondDecimal; yield: BondDecimal | null; issue?: string | null; latestNoticeDate?: string | null }>;
  yieldBasis?: string;
  quoteSource?: string | null;
  quoteCategory?: string | null;
  quoteObservedAt?: string | null;
  quoteIsExpired?: boolean;
  quoteSettlementDate?: string | null;
  originalCleanConsideration?: BondDecimal | null;
  originalDirtyConsideration?: BondDecimal | null;
  settlementDifference?: BondDecimal | null;
  request?: BondWorkflowRequest | null;
}

export interface BondAccountScope { tenantId: string; companyId: string; accountId: string; bookId: string; baseCurrency: string }
export interface AccountBondIncomePlanningRequest {
  scope: BondAccountScope; periodStart: string; periodEnd: string; context: BondCalculationContext;
  openingBalances: unknown[]; fxConversions: unknown[]; minimumCashThreshold: number; bucketDays: number;
}
export interface BondMonitoringCaptureRequest {
  runId: string; scope: BondAccountScope; recordedAtUtc: string;
  obligations: unknown[]; payments: unknown[]; allocations: unknown[]; receiptRevisions: unknown[]; observations: unknown[];
  previousSnapshotId?: string | null; correctionRationale?: string | null;
  planningRunId?: string | null;
}
export interface AccountBondIncomePlan {
  scope: BondAccountScope; periodStart: string; periodEnd: string;
  holdingsBatchId: string | null; holdingsContentHash: string | null;
  currencyLadders: Array<PortfolioCashLadder & { isDecisionReady?: boolean; blockingReasons?: string[] }>;
  baseCurrencyLadder: PortfolioCashLadder & { isDecisionReady?: boolean; blockingReasons?: string[] };
  calculations: BondWorkflowResult[]; monitoringInputs: BondMonitoringCaptureRequest;
  blockers: string[]; generatedAt: string;
}
export interface BondMonitoringSnapshot {
  snapshotId: string; engineVersion: string; inputs: BondMonitoringCaptureRequest;
  collections: Array<{
    obligationId: string; securityId: string; flowType: string; dueDate: string;
    expectedAmount: BondDecimal; netCollected: BondDecimal; grossCollected: BondDecimal;
    withholdingTax: BondDecimal; fees: BondDecimal; remainingAmount: BondDecimal; currency: string;
    status: string; ageDays: number; revisedReceiptDate: string | null;
    includedInSpendableForecast: boolean; paymentVersionIds: string[];
  }>;
  watch: Array<{ watchId: string; securityId: string; kind: string; date: string; summary: string; agency?: string | null; source: string; requiresAttention: boolean }>;
  issues: Array<{ code: string; message: string }>;
}
export interface BondIncomePreviewRequest { ledgerBookId: string; taxLotRecordId: string; throughDate: string; knownAt: string }
export interface BondIncomePreview {
  ledgerBookId: string; taxLotRecordId: string; fingerprint: string;
  instruction?: { bondIncome?: { currentProjection: { currency: string }; accountingPolicyId: string; accountingPolicyVersion: string } | null };
  income: {
    modelVersion: string; floaterPolicy: "AcquisitionIndex" | "ProspectiveReset";
    effectiveAnnualYield: BondDecimal; openingCarryingBasis: BondDecimal; closingCarryingBasis: BondDecimal;
    couponIncome: BondDecimal; premiumDiscountIncome: BondDecimal; indexationIncome: BondDecimal;
    scheduledPrincipal: BondDecimal; capitalizedFees: BondDecimal; expensedFees: BondDecimal;
    withholdingTax: BondDecimal; interestReceivable: BondDecimal; interestPayable: BondDecimal;
    totalAccountingIncome: BondDecimal;
    journalComponents: Array<{ component: string; debit: BondDecimal; credit: BondDecimal }>;
  };
}

export interface BondComparisonRequest { runId: string; context: BondCalculationContext; bonds: BondWorkflowRequest[] }
export interface BondComparisonResult { runId: string; modelVersion: string; context: BondCalculationContext; bonds: BondWorkflowResult[]; issues: string[]; isComplete: boolean }
export interface BondPortfolioRiskRequest { context: BondCalculationContext; parallelShiftBps: BondDecimal; spreadWideningBps: BondDecimal; steepeningPillars?: Array<{ date: string; shiftBps: BondDecimal }>; runId?: string | null }
export interface BondPortfolioRiskResult {
  context?: BondCalculationContext; runId?: string | null;
  scope: BondAccountScope; holdingsBatchId: string | null; holdingsContentHash: string | null;
  positions: Array<{ securityId: string; issuer: string | null; currency: string; maturity: string | null; signedOriginalFace: BondDecimal; presentValue: BondDecimal | null; dv01: BondDecimal | null; cs01: BondDecimal | null; scenarios: Array<{ scenario: string; presentValue: BondDecimal | null; change: BondDecimal | null }>; issues: string[] }>;
  buckets: Array<{ dimension: string; key: string; currency: string; holdings: number; coveredHoldings: number; knownPresentValue: BondDecimal; knownDv01: BondDecimal; knownCs01: BondDecimal }>;
  issues: string[]; isComplete: boolean;
}
export interface BondMarketObservation {
  observationId: string; recordedAtUtc: string; contentSha256: string;
  input: { observationKey: string; revision: number; kind: string; providerId: string; seriesKey: string; economicDate: string; unit: string; value: BondDecimal | null; sourceName: string; securityId?: string | null; currency?: string | null; rating?: string | null; ratingAgency?: string | null;
    quote?: { category: string; priceBasis: string; settlementDate: string; availableFace?: BondDecimal | null; providerYield?: BondDecimal | null; providerYieldConvention?: string | null } | null };
}
export interface BondConnectionCapability {
  connectionId: string; providerId: string; capability: string; implemented: boolean; configured: boolean;
  entitled: boolean | null; evidenceStatus: string; verifiedAtUtc: string | null;
  verificationEvidence: string | null; blockers: string[]; usable: boolean;
}
export interface BondPerformanceRequest { scope: BondAccountScope; periodStart: string; periodEnd: string; knownAt: string; taxLotRecordIds: string[] }
export interface BondPerformanceResult {
  scope: BondAccountScope; periodStart: string; periodEnd: string; modelVersion: string; completeCoverage: boolean;
  baseObservedMarkAndCashChange: BondDecimal | null; coverageGaps: string[];
  securities: Array<{ securityId: string; currency: string; openingDirtyValue: BondDecimal | null; closingDirtyValue: BondDecimal | null;
    actualNetCash: BondDecimal; cashGrossCoupons: BondDecimal; principalReturned: BondDecimal;
    withholdingTax: BondDecimal; fees: BondDecimal; observedMarkAndCashChange: BondDecimal | null; economicReturn: BondDecimal | null;
    attribution: { carryAndRoll: BondDecimal | null; rateEffect: BondDecimal | null; spreadEffect: BondDecimal | null; inflationEffect: BondDecimal | null;
      collectionAndCosts: BondDecimal | null; fxEffect: BondDecimal | null; residual: BondDecimal | null; currency: string };
    inputs: BondCalculationInput[]; coverageGaps: string[] }>;
  accountingIncome: Array<{ securityId: string; taxLotRecordIds: string[]; currency: string;
    openingCarryingBasis: BondDecimal; closingCarryingBasis: BondDecimal; couponIncome: BondDecimal; premiumDiscountIncome: BondDecimal;
    indexationIncome: BondDecimal; expensedFees: BondDecimal; cashGrossCoupons: BondDecimal; accrualVersusCashDifference: BondDecimal;
    accountingIncome: BondDecimal; instructionFingerprints: string[] }>;
}
export interface BondScreeningQuery {
  asOf: string; knownAtUtc: string; maturityFrom: string; maturityTo: string;
  issuer?: string | null; currency?: string | null; couponKind?: string | null; callProtectedThrough?: string | null;
  ratingAgency?: string | null; acceptedRatings?: string[] | null; pageSize: number; cursor?: string | null;
}
export interface BondScreeningPage {
  rows: Array<{ reference: { securityId: string; issuerName?: string | null; currency: string;
    couponStructure?: { kind: string } | null; accrualConvention?: { couponKind: string } | null;
    lifecycle?: { maturityDate: string; isCallable: boolean; callDate?: string | null } | null };
    ratings: BondMarketObservation[]; gaps: string[] }>;
  nextCursor: string | null; gaps: Array<{ securityId: string; reason: string }>; sourcePagesRead: number;
  referenceKnowledgeTimeVerified: boolean; limitations: string[];
}
export interface BondCouponRecognitionRequest {
  runId: string; taxLotRecordId: string; periodId: string; throughDate: string; knownAt: string;
  cashAllocations: Array<{ allocationId: string; paymentVersionId: string; netAmount: BondDecimal; grossAmount: BondDecimal;
    withholdingTax?: BondDecimal; fees?: BondDecimal; deductionEvidence?: unknown }>;
  reversesReceiptId?: string | null; correctionRationale?: string | null;
}
export interface BondCouponRecognitionPreview {
  fingerprint: string; earnedDelta: BondDecimal; settledGross: BondDecimal; remainingSignedBalance: BondDecimal; hasChanges: boolean;
  instruction: { modelVersion: string; scope: BondAccountScope; request: BondCouponRecognitionRequest; eventId: string;
    lines: Array<{ accountId: string; debit: BondDecimal; credit: BondDecimal; currency: string }> };
  remainingReceivable?: BondDecimal; remainingPayable?: BondDecimal;
}
export interface BondCouponRecognitionPreparation {
  preparation: { preview: BondCouponRecognitionPreview };
  projection: { spine: { eventId: string; currency: string; eventKind: string }; wasReplay: boolean };
}
export interface BondIdentifierDiscoveryRequest { identifier: string; identifierType: "ISIN" | "CUSIP"; marketSector?: string | null }
export interface BondIdentifierDiscoveryResult {
  identifier: string; identifierType: string; isAmbiguous: boolean; resolvedFigi: string | null; evidenceStatus: string;
  candidates: Array<{ figi: string; compositeFigi: string | null; securityType: string | null; marketSector: string | null; ticker: string | null; name: string | null; exchangeCode: string | null }>;
}
export interface BondFredIntakeRequest { evidenceUri: string; documentId: string; from: string; through: string; vintageDate: string }
export interface BondFredIntakeResult { retainedObservations: { observations: BondMarketObservation[]; appendedCount: number }; missingDates: string[]; responseSha256: string; bindingEvidenceSha256: string; evidenceStatus: string }
