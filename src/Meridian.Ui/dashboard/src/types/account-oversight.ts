/** Exact invariant decimal text retained by shared services. Never convert financial values to JS numbers. */
export type OversightDecimal = string;

export interface OversightScope { accountId: string; ledgerBookId: string; baseCurrency: string }
export interface OversightContext {
  accountId: string;
  bookId: string;
  baseCurrency: string;
  bookName: string;
  actor: string;
  canWrite: boolean;
  canReadPrices: boolean;
  canManagePrices: boolean;
  canProvision: boolean;
}

/** Unannotated shared enums use their numeric HTTP representation. */
export type ReconciliationSourceType = 0 | 1 | 2 | 3;
export type BreakClassification = 0 | 1 | 2 | 3;
export interface AccountCashReconciliationScope {
  tenantId: string; companyId: string; accountId: string; ledgerBookId: string;
}
export interface NormalizedCashEntry {
  cashEntryId: string; accountId: string; amount: OversightDecimal; currency: string;
  amountBase: OversightDecimal; baseCurrency: string; postedAtUtc: string;
  accountingPeriod: string; sourceReference: string;
  counterpartyReference?: string | null; settlementId?: string | null; comment?: string | null;
}
export interface AccountCashBalanceSourceRow { row: NormalizedCashEntry; originalPayload: string }
export interface AccountCashBalanceSource {
  sourceId: string; sourceType: ReconciliationSourceType; version: string; evidenceReference: string;
  capturedAtUtc: string; completePopulation: boolean; rows: AccountCashBalanceSourceRow[];
}
export interface CashToleranceRule {
  ruleId: string; absoluteCashTolerance: OversightDecimal; basisPointCashTolerance: OversightDecimal | null;
  settlementDateTolerance: string;
}
export interface StatementToleranceProfile {
  profileId: string; version: number; cashRules: CashToleranceRule[];
  positionRules: { ruleId: string; quantityTolerance: OversightDecimal; marketValueTolerance: OversightDecimal; priceTolerance: OversightDecimal }[];
  transactionRules: { ruleId: string; absoluteCashTolerance: OversightDecimal; settlementDateTolerance: string; priceTolerance: OversightDecimal }[];
}
export interface AccountCashReconciliationRequest {
  runId: string; scope: AccountCashReconciliationScope; businessDate: string; observedAtUtc: string;
  left: AccountCashBalanceSource; right: AccountCashBalanceSource; tolerance: StatementToleranceProfile; initialOwner: string;
}
export interface CashMatchGroup {
  matchGroupId: string; classification: BreakClassification; ruleId: string;
  positionIds: string[]; cashEntryIds: string[]; evidenceIds: string[];
  toleranceProfileId: string; toleranceProfileVersion: number; toleranceRuleId?: string | null;
}
export interface CashBreakRecord {
  breakId: string; classification: BreakClassification; ruleId: string; reason: string; snapshotIds: string[]; evidenceIds: string[];
}
export interface CashMatchEvidence {
  evidenceId: string; ruleId: string; stage: string; narrative: string;
  numericDelta: OversightDecimal | null; attributes: Record<string, string>;
}
export interface AccountCashComparison {
  currency: string; observationState: string; matches: CashMatchGroup[]; breaks: CashBreakRecord[]; evidence: CashMatchEvidence[];
}
export interface AccountCashReconciliationRun {
  input: AccountCashReconciliationRequest; inputHash: string; comparisons: AccountCashComparison[]; resultHash: string;
}
export interface AccountCashReconciliationCase {
  caseId: string; scope: AccountCashReconciliationScope; leftSourceId: string; rightSourceId: string;
  currency: string; owner: string; version: number; firstObservedAtUtc: string; lastObservedAtUtc: string;
  state: string; resolvedAtUtc: string | null; resolutionRunId: string | null; agingDays: number;
  history: { runId: string; observedAtUtc: string; state: string; resultHash: string }[];
  assignments: { owner: string; actor: string; reason: string; assignedAtUtc: string }[];
}

export type PriceUnit = "Unspecified" | "CurrencyPerUnit" | "PercentOfPar";
export interface ClosePriceOverride { price: OversightDecimal; unit: PriceUnit; rationale: string; evidenceReference: string }
export interface ClosePriceInput { securityId: string; override?: ClosePriceOverride | null }
export interface CreateClosePriceSetRequest {
  accountId: string; bookId: string; valuationDate: string; evaluatedAsOf: string;
  securities: ClosePriceInput[]; maxSourceDispersionPercent: OversightDecimal; createdBy: string;
  correctsSnapshotId?: string | null; correctionRationale?: string | null; knowledgeAsOf?: string | null;
  /** Stable command identity retained across retries; the resulting snapshot has this UUID. */
  requestId?: string | null;
}
export interface ApproveClosePriceSetRequest {
  accountId: string; bookId: string; valuationDate: string; snapshotId: string; reviewer: string; rationale: string;
}
export interface ClosePriceObservation { sourceId: string; price: OversightDecimal; priceAsOf: string; unit: PriceUnit }
export interface ClosePriceSelection {
  securityId: string; goldenCopyPrice: OversightDecimal; priceKind: string; selectedSource: string;
  priceAsOf: string; isStaleFallback: boolean; daysStale: number | null; unit: PriceUnit;
  comparisonPrices: (ClosePriceObservation & { pctDiffFromGoldenCopy: OversightDecimal | null })[];
  evaluatedAsOf?: string | null; hierarchyAsOf?: string | null; knowledgeAsOf?: string | null;
  selectionReceiptId?: string | null;
  hierarchySnapshot?: {
    securityId: string; accountId: string | null; asOf: string; updatedBy: string;
    entries: { priority: number; sourceId: string; sourceDisplayName: string; maxDaysStale: number }[];
  } | null;
}
export interface ClosePriceLine {
  securityId: string; selection: ClosePriceSelection | null; observations: ClosePriceObservation[];
  override: ClosePriceOverride | null; closePrice: OversightDecimal | null; unit: PriceUnit;
}
export interface ClosePriceSnapshot {
  snapshotId: string; accountId: string; bookId: string; valuationDate: string; version: number;
  correctsSnapshotId: string | null; correctionRationale: string | null; evaluatedAsOf: string;
  knowledgeAsOf: string; maxSourceDispersionPercent: OversightDecimal; createdBy: string; createdAt: string;
  lines: ClosePriceLine[]; blockers: { securityId: string; code: string; reason: string }[];
  /** Absent on legacy snapshots; binds an identified creation command on new snapshots. */
  creationCommandDigest?: string | null;
  /** Preserves whether the original request supplied an explicit knowledge cutoff. */
  requestedKnowledgeAsOf?: string | null;
}
export interface ClosePriceApproval {
  snapshotId: string; snapshotSha256: string; reviewer: string; rationale: string; approvedAt: string;
}
export interface ClosePriceSet { snapshot: ClosePriceSnapshot; approval: ClosePriceApproval | null }

export interface AccountCashForecastScope {
  tenantId: string; companyId: string; accountId: string; bookId: string; baseCurrency: string;
}
export interface RetainedCashEvidence {
  evidenceId: string; evidenceUri: string; contentHashSha256: string; sourceSystem: string; sourceReference: string;
  reviewStatus: string; reviewedBy: string; reviewedAtUtc: string; effectiveDate: string;
  evidenceVersion: number; retainedAtUtc: string; retainedBy: string; subjectType: string; subjectId: string;
}
export interface AccountCashProvenance {
  sourceDomain: string; sourceEntityId: string; sourceContentHash: string;
  /** Observation = 0, Projection = 1, DeclaredFx = 2. */
  role: 0 | 1 | 2; evidence: RetainedCashEvidence;
}
export interface AccountSettledCashObservation {
  accountId: string; bookId: string; asOfDate: string; amount: OversightDecimal; currency: string;
  isSettled: boolean; provenance: AccountCashProvenance;
}
export interface AccountCashFlow {
  flowId: string; versionId: string; accountId: string; bookId: string;
  /** Expected = 0; Actual = 1. */
  status: 0 | 1; flowType: string; amount: OversightDecimal; currency: string;
  economicDate: string | null; settlementDate: string | null; cashReceiptDate: string | null;
  provenance: AccountCashProvenance; securityId?: string | null; projectionRunId?: string | null;
  projectionEngineVersion?: string | null; termsVersionId?: string | null; termsHash?: string | null;
  supersedesVersionId?: string | null;
}
export interface AccountCashFxConversion {
  currency: string; baseCurrency: string; conversionDate: string; baseUnitsPerCurrencyUnit: OversightDecimal;
  provenance: AccountCashProvenance;
}
export interface AccountCashForecastRequest {
  runId: string; scope: AccountCashForecastScope; periodStart: string; periodEnd: string;
  openingCash: AccountSettledCashObservation | null; closingCash: AccountSettledCashObservation | null;
  flows: AccountCashFlow[]; fxConversions: AccountCashFxConversion[]; recordedAtUtc: string;
  previousSnapshotId?: string | null; correctionRationale?: string | null;
  minimumCashThreshold: OversightDecimal; bucketDays: number;
}
export interface AccountCashForecastLadder {
  engineVersion: string; asOfDate: string; horizonDays: number; baseCurrency: string; scenarioId: string;
  openingCash: OversightDecimal; minimumCashThreshold: OversightDecimal;
  buckets: {
    bucketStart: string; bucketEnd: string; label: string; inflows: OversightDecimal; outflows: OversightDecimal;
    netFlow: OversightDecimal; cumulativeCash: OversightDecimal; breachesMinimumBalance: boolean;
    sources: { source: string; amount: OversightDecimal; flowCount: number }[];
  }[];
  contributions: {
    securityId: string; displayName: string; assetClass: string; projectionRunId: string | null;
    projectionEngineVersion: string | null; termsVersionId: string | null; termsHash: string | null;
    sourceDomain: string; sourceEntityId: string | null; flowType: string; sourceLane: string;
    dueDate: string; amount: OversightDecimal; currency: string; baseAmount: OversightDecimal; scenarioAdjustment: string | null;
  }[];
  availableScenarios: { scenarioId: string; displayName: string; description: string; modeledEffects: string[]; assumptions: string[] }[];
  assumptions: string[]; warnings: string[]; generatedAt: string; securitiesEvaluated: number;
  securitiesWithFlows: number; isDecisionReady: boolean; blockingReasons: string[];
}
export interface AccountCashForecastSnapshot {
  snapshotId: string; evidenceHash: string; engineVersion: string; inputs: AccountCashForecastRequest;
  openingBaseCash: OversightDecimal | null; actualNetBaseFlow: OversightDecimal | null;
  reconciledClosingBaseCash: OversightDecimal | null; observedClosingBaseCash: OversightDecimal | null;
  closingVariance: OversightDecimal | null; isCertified: boolean; forecastLadder: AccountCashForecastLadder;
  blockers: { code: string; message: string; inputId?: string | null }[];
}
