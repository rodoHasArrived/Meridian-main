/**
 * Portfolio cash ladder read-model types.
 *
 * Kept in a dedicated module (rather than the shared `@/types` barrel) so the
 * forecasting surface can grow without inflating the central types file.
 */

export interface PortfolioCashLadderContribution {
  securityId: string;
  displayName: string;
  assetClass: string;
  projectionRunId?: string | null;
  projectionEngineVersion?: string | null;
  termsVersionId?: string | null;
  termsHash?: string | null;
  sourceDomain: string;
  sourceEntityId?: string | null;
  flowType: string;
  sourceLane: string;
  dueDate: string;
  amount: number;
  currency: string;
  baseAmount: number;
  scenarioAdjustment?: string | null;
}

export interface PortfolioCashLadderSourceSlice {
  source: string;
  amount: number;
  flowCount: number;
}

export interface PortfolioCashLadderBucket {
  bucketStart: string;
  bucketEnd: string;
  label: string;
  inflows: number;
  outflows: number;
  netFlow: number;
  cumulativeCash: number;
  breachesMinimumBalance: boolean;
  sources: PortfolioCashLadderSourceSlice[];
}

export interface PortfolioCashScenario {
  scenarioId: string;
  displayName: string;
  description: string;
  modeledEffects: string[];
  assumptions: string[];
}

export interface PortfolioCashLadder {
  engineVersion: string;
  asOfDate: string;
  horizonDays: number;
  baseCurrency: string;
  scenarioId: string;
  openingCash: number;
  minimumCashThreshold: number;
  buckets: PortfolioCashLadderBucket[];
  contributions: PortfolioCashLadderContribution[];
  availableScenarios: PortfolioCashScenario[];
  assumptions: string[];
  warnings: string[];
  generatedAt: string;
  securitiesEvaluated: number;
  securitiesWithFlows: number;
}
