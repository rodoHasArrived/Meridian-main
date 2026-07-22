import { useCallback, useEffect, useMemo, useState } from "react";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import * as workstationApi from "@/lib/api";
import type {
  ProviderCatalogResponse,
  ProviderConnectionHealthResponse,
  ProviderConnectionHealthSnapshot,
  ProviderRateLimitSnapshot,
  ProviderRateLimitsResponse,
  ProviderRegistrationFailure
} from "@/types";

export interface ProviderAccountingServices {
  getCatalog: (signal?: AbortSignal) => Promise<ProviderCatalogResponse>;
  getRateLimits: (signal?: AbortSignal) => Promise<ProviderRateLimitsResponse>;
  getConnectionHealth: (signal?: AbortSignal) => Promise<ProviderConnectionHealthResponse>;
}

export interface ProviderRegistrationFailureRow {
  id: string;
  stage: string;
  subject: string;
  module: string;
  error: string;
}

export interface ProviderRateLimitRow {
  id: string;
  provider: string;
  surface: string;
  status: string;
  statusTone: "success" | "warning" | "danger";
  requestUsage: string;
  remaining: string;
  resetCountdown: string;
  failureReason: string;
  retryPosture: string;
  connectionPosture: string;
  historyPosture: string;
}

export interface ProviderAccountingPanelState {
  registrationTitle: string;
  registrationSummary: string;
  registrationTone: "success" | "warning" | "danger";
  registrationFailures: ProviderRegistrationFailureRow[];
  rateLimitSummary: string;
  rateLimits: ProviderRateLimitRow[];
  historyPosture: string;
  observedAt: string | null;
}

const HISTORY_UNAVAILABLE = "Unavailable — runtime rate-limit history is not retained.";

const defaultServices: ProviderAccountingServices = {
  getCatalog: (signal) => workstationApi.getProviderCatalog({ signal }),
  getRateLimits: (signal) => workstationApi.getProviderRateLimits({ signal }),
  getConnectionHealth: (signal) => workstationApi.getProviderConnectionHealth({ signal })
};

export function useProviderAccountingPanel(services: ProviderAccountingServices = defaultServices) {
  const [catalog, setCatalog] = useState<ProviderCatalogResponse | null>(null);
  const [rateLimits, setRateLimits] = useState<ProviderRateLimitsResponse | null>(null);
  const [connectionHealth, setConnectionHealth] = useState<ProviderConnectionHealthResponse | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const {
    status: requestStatus,
    start,
    succeed,
    fail,
    finish
  } = useRequestLifecycle({
    operation: "provider-accounting",
    idleMessage: "Provider runtime accounting has not loaded.",
    runningMessage: "Loading provider registration and current rate-limit state.",
    successMessage: "Provider registration and rate-limit state loaded.",
    failureMessage: "Provider runtime accounting is unavailable."
  });

  const refresh = useCallback(async () => {
    const token = start({ busyMode: "drop" });
    if (!token) return;

    try {
      const [nextCatalog, nextRateLimits, nextConnectionHealth] = await Promise.all([
        services.getCatalog(token.signal),
        services.getRateLimits(token.signal),
        services.getConnectionHealth(token.signal)
      ]);
      if (!token.isCurrent()) return;
      token.safeSetState(setCatalog, nextCatalog);
      token.safeSetState(setRateLimits, nextRateLimits);
      token.safeSetState(setConnectionHealth, nextConnectionHealth);
      setNow(Date.now());
      succeed(token);
    } catch (error) {
      if (token.signal.aborted) return;
      fail(token, error);
    } finally {
      finish(token);
    }
  }, [fail, finish, services, start, succeed]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!rateLimits?.providers.some((provider) => provider.resetAt)) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => window.clearInterval(timer);
  }, [rateLimits]);

  const panel = useMemo(
    () => buildProviderAccountingPanelState(catalog, rateLimits, now, connectionHealth),
    [catalog, connectionHealth, now, rateLimits]
  );

  return {
    panel,
    requestStatus,
    refresh
  };
}

export function buildProviderAccountingPanelState(
  catalog: ProviderCatalogResponse | null,
  rateLimits: ProviderRateLimitsResponse | null,
  now: number,
  connectionHealth: ProviderConnectionHealthResponse | null = null
): ProviderAccountingPanelState {
  const report = catalog?.registrationReport ?? null;
  const registrationFailures = (report?.failures ?? []).map(buildRegistrationFailureRow);
  const registrationTitle = report === null
    ? "Registration report unavailable"
    : report.isHealthy
      ? "Provider registration healthy"
      : `${registrationFailures.length} provider registration failure${registrationFailures.length === 1 ? "" : "s"}`;
  const registrationSummary = report === null
    ? "The catalog did not return provider discovery evidence. Registration health cannot be inferred."
    : `${report.registeredModuleCount} registered, ${report.skippedModuleCount} skipped, ${report.discoveredSourceCount} discovered sources. Reported ${formatTimestamp(report.generatedAt)}.`;
  const connectionByProvider = new Map(
    (connectionHealth?.providers ?? []).map((provider) => [normalizeProviderId(provider.providerId), provider])
  );
  const rateLimitRows = (rateLimits?.providers ?? []).map((provider) => buildRateLimitRow(
    provider,
    now,
    connectionByProvider.get(normalizeProviderId(provider.provider)) ?? null
  ));

  return {
    registrationTitle,
    registrationSummary,
    registrationTone: report === null ? "warning" : report.isHealthy ? "success" : "danger",
    registrationFailures,
    rateLimitSummary: rateLimits === null
      ? "Current provider rate-limit state is unavailable."
      : rateLimitRows.length === 0
        ? "No provider runtime exposed a current rate-limit snapshot."
        : `${rateLimitRows.length} current provider surface${rateLimitRows.length === 1 ? "" : "s"} observed ${formatTimestamp(rateLimits.timestamp)}.`,
    rateLimits: rateLimitRows,
    historyPosture: HISTORY_UNAVAILABLE,
    observedAt: rateLimits?.timestamp ?? null
  };
}

function buildRegistrationFailureRow(failure: ProviderRegistrationFailure, index: number): ProviderRegistrationFailureRow {
  return {
    id: `${failure.stage}-${failure.moduleId ?? failure.subject}-${index}`,
    stage: formatToken(failure.stage),
    subject: failure.subject,
    module: failure.moduleId ?? "Module unavailable",
    error: `${failure.errorType}: ${failure.errorMessage}`
  };
}

function buildRateLimitRow(
  provider: ProviderRateLimitSnapshot,
  now: number,
  connection: ProviderConnectionHealthSnapshot | null
): ProviderRateLimitRow {
  const resetCountdown = formatResetCountdown(provider.resetAt, now);
  const limited = provider.stateAvailable && provider.isRateLimited;
  const reason = normalizeReason(provider.reason);

  return {
    id: `${provider.provider}-${provider.surface}`,
    provider: provider.displayName || provider.name || provider.provider,
    surface: formatToken(provider.surface),
    status: !provider.stateAvailable ? "State unavailable" : limited ? "Rate limited" : "Available",
    statusTone: !provider.stateAvailable ? "warning" : limited ? "danger" : "success",
    requestUsage: provider.requestsInWindow === null
      ? `Unavailable / ${provider.maxRequestsPerWindow}`
      : `${provider.requestsInWindow} / ${provider.maxRequestsPerWindow}`,
    remaining: provider.remainingRequests === null ? "Unavailable" : provider.remainingRequests.toString(),
    resetCountdown,
    failureReason: limited
      ? `Current rate-limit reason: ${reason ?? "reason unavailable"}.`
      : reason
        ? `Current runtime reason: ${reason}.`
        : "Last rate-limit failure unavailable — history is not retained.",
    retryPosture: buildRetryPosture(provider, resetCountdown),
    connectionPosture: buildConnectionPosture(connection),
    historyPosture: HISTORY_UNAVAILABLE
  };
}

function buildConnectionPosture(connection: ProviderConnectionHealthSnapshot | null): string {
  if (connection === null) {
    return "Unknown — reachability unavailable; no runtime diagnostics.";
  }
  if (!connection.isEnabled || connection.connectionState.toLowerCase() === "disabled") {
    return "Disabled — provider runtime is not enabled.";
  }
  if (!connection.diagnosticsAvailable || connection.isConnected === null) {
    return "Unknown — reachability unavailable; no runtime diagnostics.";
  }

  const failure = connection.lastFailureKind ? ` (${normalizeReason(connection.lastFailureKind)})` : "";
  switch (connection.connectionState.trim().toLowerCase()) {
    case "reconnecting":
      return `Reconnecting — attempt ${connection.reconnectAttempts ?? 0}; runtime is recovering${failure}.`;
    case "degraded":
      return `Degraded — runtime lost healthy reachability${failure}.`;
    case "connecting":
      return "Connecting — runtime handshake is in progress.";
    case "disconnecting":
      return "Disconnecting — runtime shutdown is in progress.";
    case "failed":
      return `Failed — runtime connection could not recover${failure}.`;
    case "connected":
      if (connection.isConnected) return "Connected — runtime probe reports reachable.";
      break;
  }

  return `Disconnected — runtime probe reports unreachable${failure}.`;
}

function normalizeProviderId(value: string): string {
  return value.trim().toLowerCase().replaceAll(/[^a-z0-9]/g, "");
}

function buildRetryPosture(provider: ProviderRateLimitSnapshot, resetCountdown: string): string {
  if (!provider.stateAvailable) return "Retry posture unavailable until runtime diagnostics are exposed.";
  if (!provider.isRateLimited) return "Requests may proceed within the reported window.";
  if (provider.resetAt) return `Retry after ${resetCountdown.toLowerCase()}.`;
  return "Retry is blocked; the provider did not report a reset time.";
}

export function formatResetCountdown(resetAt: string | null, now: number): string {
  if (!resetAt) return "No reset pending";
  const reset = Date.parse(resetAt);
  if (Number.isNaN(reset)) return "Reset time unavailable";
  const remainingSeconds = Math.max(0, Math.ceil((reset - now) / 1_000));
  if (remainingSeconds === 0) return "Reset due";
  const hours = Math.floor(remainingSeconds / 3_600);
  const minutes = Math.floor((remainingSeconds % 3_600) / 60);
  const seconds = remainingSeconds % 60;
  if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

function normalizeReason(reason: string | null): string | null {
  if (!reason?.trim()) return null;
  return reason.replaceAll("-", " ").replaceAll(":", ": ");
}

function formatToken(value: string): string {
  const normalized = value.trim().replaceAll("-", " ");
  return normalized.length === 0 ? "Unavailable" : normalized[0].toUpperCase() + normalized.slice(1);
}

function formatTimestamp(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}
