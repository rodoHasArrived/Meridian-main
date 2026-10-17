export interface TradingBrokerageRecoveryPortfolio {
  cash: number | null;
  buyingPower: number | null;
  portfolioValue: number | null;
  currency: string | null;
  positionCount: number;
  observedAt: string | null;
  expiresAt?: string | null;
  lastAttemptedAt: string | null;
  lastSuccessfulAt: string | null;
  isComplete: boolean;
  isFresh: boolean;
  isConsistent: boolean;
  warnings: string[];
}

export interface TradingBrokerageRecoveryRun {
  runId: string;
  strategyId: string;
  status: string;
  detail: string;
}

export interface TradingBrokerageRecovery {
  fundAccountId: string | null;
  providerId: string | null;
  externalAccountId: string | null;
  status: "Ready" | "Blocked";
  detail: string;
  portfolio: TradingBrokerageRecoveryPortfolio | null;
  affectedRuns: TradingBrokerageRecoveryRun[];
  blockingReasons: string[];
}
