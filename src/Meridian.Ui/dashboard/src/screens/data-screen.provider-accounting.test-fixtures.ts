import type {
  ProviderCatalogResponse,
  ProviderConnectionHealthResponse,
  ProviderRateLimitsResponse
} from "@/types";

export function buildProviderAccountingCatalogFixture(): ProviderCatalogResponse {
  return {
    providers: [],
    totalCount: 0,
    timestamp: "2026-07-13T12:00:00Z",
    source: "registry",
    registrationReport: {
      generatedAt: "2026-07-13T12:00:00Z",
      discoveredSourceCount: 4,
      moduleCandidateCount: 3,
      moduleActivationAttemptCount: 3,
      moduleRegistrationAttemptCount: 2,
      registeredModuleCount: 1,
      skippedModuleCount: 1,
      failedModuleCount: 1,
      isHealthy: false,
      failures: [{
        stage: "activate",
        subject: "Meridian.Infrastructure.Adapters.NYSE.NyseProviderModule",
        moduleId: "nyse-module",
        errorType: "InvalidOperationException",
        errorMessage: "Provider construction failed."
      }]
    }
  };
}

export function buildProviderRateLimitsFixture(): ProviderRateLimitsResponse {
  return {
    timestamp: "2026-07-13T12:00:00Z",
    providers: [{
      provider: "nyse",
      name: "nyse",
      displayName: "NYSE",
      priority: 1,
      capabilities: {
        adjustedPrices: true,
        intraday: true,
        dividends: true,
        splits: true,
        quotes: true,
        trades: true,
        auctions: true,
        supportedMarkets: ["US"]
      },
      surface: "historical",
      stateAvailable: true,
      observedAt: "2026-07-13T12:00:00Z",
      requestsInWindow: 8,
      maxRequestsPerWindow: 10,
      remainingRequests: 2,
      windowSeconds: 60,
      usageRatio: 0.8,
      isRateLimited: true,
      isThrottled: true,
      resetAt: "2026-07-13T12:01:05Z",
      reason: "provider-response",
      status: "rate-limited"
    }]
  };
}

export function buildProviderConnectionHealthFixture(
  isConnected: boolean | null = null
): ProviderConnectionHealthResponse {
  return {
    timestamp: "2026-07-13T12:00:00Z",
    providers: [{
      providerId: "nyse",
      displayName: "NYSE",
      isEnabled: true,
      isConnected,
      connectionState: isConnected === null ? "unknown" : isConnected ? "connected" : "disconnected",
      diagnosticsAvailable: isConnected !== null,
      lastFailureKind: isConnected === false ? "socket-closed" : null
    }]
  };
}
