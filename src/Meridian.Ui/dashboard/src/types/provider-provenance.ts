export interface ProviderProvenanceSource {
  providerId: string;
  connectionId?: string | null;
  environment: string;
  feedLabel: string;
  entitlementLabel: string;
  dataSemantics: string;
  priority?: number | null;
  requests: number;
  responsesWithData: number;
  unavailable: number;
  emptyResponses: number;
  staleResponses: number;
  staleResponsesAccepted: number;
  replayEvents: number;
  staleToleranceDays?: number | null;
}

export interface ProviderProvenanceValidation {
  version: string;
  staleToleranceDays?: number | null;
  futureToleranceDays?: number | null;
  tryOtherProvidersForStaleBars?: boolean | null;
  symbolResolution?: boolean | null;
  crossValidation?: boolean | null;
  rateLimitRotation?: boolean | null;
  rateLimitRotationThreshold?: number | null;
  rateLimitRetries?: number | null;
  rateLimitRetryBudgetSeconds?: number | null;
  failureBackoffSeconds?: number | null;
  preflightEnabled?: boolean | null;
  corporateActionsApplied?: boolean | null;
  failOnUnknownSymbols?: boolean | null;
  optionsStrikeRange?: number | null;
}

export interface ProviderProvenanceRequest {
  from?: string | null;
  to?: string | null;
  symbols: readonly string[];
  granularity?: string | null;
  maxConcurrentSymbols?: number | null;
  symbolPriorities?: readonly { symbol: string; priority: number }[] | null;
  resumeFromCheckpoint?: boolean | null;
  initialCash?: number | null;
  adjustForCorporateActions?: boolean | null;
  failOnUnknownSymbols?: boolean | null;
  withheldSymbolCount: number;
  usesDiscoveredUniverse: boolean;
  skipExistingData?: boolean | null;
  fillGapsOnly?: boolean | null;
}

export interface ProviderProvenanceReceipt {
  schemaVersion: number;
  receiptId: string;
  capability: string;
  routingDecision: string;
  providers: readonly ProviderProvenanceSource[];
  fallbackChain: readonly string[];
  fallbackOccurred: boolean;
  validationProfile: ProviderProvenanceValidation;
  request: ProviderProvenanceRequest;
  startedUtc: string;
  completedUtc: string;
  dataSemantics: string;
  reproductionUnavailableReason?: string | null;
  routingHistoryComplete?: boolean;
}

export interface BackfillClonePlan {
  request: ProviderProvenanceRequest;
  status: "Ready" | "ReconfirmationRequired" | "Unavailable";
  messages: string[];
  confirmationCode?: string | null;
}
