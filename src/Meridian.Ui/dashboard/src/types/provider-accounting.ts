export interface ProviderRegistrationFailure {
  stage: string;
  subject: string;
  moduleId: string | null;
  errorType: string;
  errorMessage: string;
}

export interface ProviderRegistrationReport {
  generatedAt: string;
  discoveredSourceCount: number;
  moduleCandidateCount: number;
  moduleActivationAttemptCount: number;
  moduleRegistrationAttemptCount: number;
  registeredModuleCount: number;
  skippedModuleCount: number;
  failedModuleCount: number;
  isHealthy: boolean;
  failures: ProviderRegistrationFailure[];
}

export interface ProviderCatalogResponse {
  providers: unknown[];
  totalCount: number;
  timestamp: string;
  source: string;
  registrationReport: ProviderRegistrationReport | null;
}

export interface ProviderRateLimitCapabilities {
  adjustedPrices: boolean;
  intraday: boolean;
  dividends: boolean;
  splits: boolean;
  quotes: boolean;
  trades: boolean;
  auctions: boolean;
  supportedMarkets: string[];
}

export interface ProviderRateLimitSnapshot {
  provider: string;
  name: string;
  displayName: string;
  priority: number;
  capabilities: ProviderRateLimitCapabilities;
  surface: string;
  stateAvailable: boolean;
  observedAt: string;
  requestsInWindow: number | null;
  maxRequestsPerWindow: number;
  remainingRequests: number | null;
  windowSeconds: number;
  usageRatio: number | null;
  isRateLimited: boolean;
  isThrottled: boolean;
  resetAt: string | null;
  reason: string | null;
  status: "available" | "rate-limited" | "unavailable" | string;
}

export interface ProviderRateLimitsResponse {
  providers: ProviderRateLimitSnapshot[];
  timestamp: string;
}

export interface ProviderConnectionHealthSnapshot {
  providerId: string;
  displayName: string;
  isEnabled: boolean;
  isConnected: boolean | null;
  connectionState: string;
  diagnosticsAvailable: boolean;
  lastFailureKind: string | null;
  reconnectAttempts?: number | null;
}

export interface ProviderConnectionHealthResponse {
  providers: ProviderConnectionHealthSnapshot[];
  timestamp: string;
}
