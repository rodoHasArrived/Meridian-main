// System overview, reference-data probe, and runtime lifecycle endpoints. Re-exported through @/lib/api.
import { buildReferenceDataApiErrorDetails, classifyReferenceDataApiError, previewReferenceDataPayload, summarizeReferenceDataPayload, type ReferenceDataEndpointProbeResult, type ReferenceDataWorkbenchCoverage } from "../reference-data-probe";
import type { MetricSnapshot, SystemEventRecord, SystemOverviewResponse } from "@/types";
import { WORKSTATION_API_ENDPOINTS, buildReferenceDataWorkbenchEndpoints, type ReferenceDataEndpointDefinition, type ReferenceDataWorkbenchEndpointSeed } from "@/lib/workstation-endpoints";
import { isApiError } from "@/lib/api-errors";
import { deriveStorageHealth, deriveSystemStatus, fallbackSystemOverview, readDegradedMode } from "@/lib/system-status";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export async function getReferenceDataWorkbenchCoverage(
  seed: ReferenceDataWorkbenchEndpointSeed,
  options: ApiRequestOptions = {}
): Promise<ReferenceDataWorkbenchCoverage> {
  const endpoints = buildReferenceDataWorkbenchEndpoints(seed);
  const results = await Promise.all(endpoints.map((endpoint) => probeReferenceDataEndpoint(endpoint, options)));

  return {
    requestedAtUtc: new Date().toISOString(),
    endpoints: results
  };
}

async function probeReferenceDataEndpoint(
  endpoint: ReferenceDataEndpointDefinition,
  options: ApiRequestOptions
): Promise<ReferenceDataEndpointProbeResult> {
  if (!endpoint.probe) {
    return {
      ...endpoint,
      status: "Deferred",
      statusCode: null,
      durationMs: null,
      responseCount: null,
      responseSummary: endpoint.requestLabel,
      responsePreview: null,
      errorSummary: null,
      errorDetails: []
    };
  }

  const startedAt = Date.now();
  try {
    const payload = await getJson<unknown>(endpoint.path, options);
    const summary = summarizeReferenceDataPayload(payload);

    return {
      ...endpoint,
      status: summary.count > 0 ? "Ready" : "Empty",
      statusCode: 200,
      durationMs: Date.now() - startedAt,
      responseCount: summary.count,
      responseSummary: summary.summary,
      responsePreview: previewReferenceDataPayload(payload),
      errorSummary: null,
      errorDetails: []
    };
  } catch (error) {
    const durationMs = Date.now() - startedAt;
    if (isApiError(error)) {
      return {
        ...endpoint,
        status: classifyReferenceDataApiError(error.status),
        statusCode: error.status,
        durationMs,
        responseCount: null,
        responseSummary: `Endpoint returned ${error.status}.`,
        responsePreview: null,
        errorSummary: error.detail || error.title || `Endpoint returned ${error.status}`,
        errorDetails: buildReferenceDataApiErrorDetails(error)
      };
    }

    const message = error instanceof Error && error.message.trim() ? error.message : "Reference endpoint probe failed.";
    return {
      ...endpoint,
      status: "Error",
      statusCode: null,
      durationMs,
      responseCount: null,
      responseSummary: message,
      responsePreview: null,
      errorSummary: message,
      errorDetails: []
    };
  }
}

// --- System overview ---

export function getSystemStatus(options: ApiRequestOptions = {}) {
  return getJson<unknown>(WORKSTATION_API_ENDPOINTS.systemStatus, options).then(normalizeSystemOverviewResponse);
}

function normalizeSystemOverviewResponse(payload: unknown): SystemOverviewResponse {
  if (!isRecord(payload)) {
    return fallbackSystemOverview();
  }

  if ("systemStatus" in payload) {
    const heartbeat = readString(payload.lastHeartbeatUtc) ?? readString(payload.timestampUtc);
    return {
      systemStatus: readSystemStatus(payload.systemStatus),
      providersOnline: readNumber(payload.providersOnline) ?? 0,
      providersTotal: readNumber(payload.providersTotal) ?? 0,
      activeRuns: readNumber(payload.activeRuns) ?? 0,
      openPositions: readNumber(payload.openPositions) ?? 0,
      activeBackfills: readNumber(payload.activeBackfills) ?? 0,
      symbolsMonitored: readNumber(payload.symbolsMonitored) ?? 0,
      storageHealth: readStorageHealth(payload.storageHealth),
      lastHeartbeatUtc: heartbeat,
      metrics: Array.isArray(payload.metrics) ? payload.metrics as MetricSnapshot[] : [],
      recentEvents: Array.isArray(payload.recentEvents) ? payload.recentEvents as SystemEventRecord[] : [],
      degradedMode: readDegradedMode(payload.degradedMode)
    };
  }

  return normalizeLegacyStatusResponse(payload);
}

function normalizeLegacyStatusResponse(payload: Record<string, unknown>): SystemOverviewResponse {
  const metrics = isRecord(payload.metrics) ? payload.metrics : {};
  const pipeline = isRecord(payload.pipeline) ? payload.pipeline : {};
  const isConnected = readBoolean(payload.isConnected) ?? false;
  const timestampUtc = readString(payload.timestampUtc) ?? readString(metrics.lastUpdatedUtc);
  const published = readNumber(metrics.published) ?? readNumber(pipeline.publishedCount) ?? 0;
  const dropped = readNumber(metrics.dropped) ?? readNumber(pipeline.droppedCount) ?? 0;
  const eventsPerSecond = readNumber(metrics.eventsPerSecond);
  const queueSize = readNumber(pipeline.currentQueueSize) ?? 0;
  const queueCapacity = readNumber(pipeline.queueCapacity) ?? 0;
  const queueUtilization = readNumber(pipeline.queueUtilization) ?? 0;
  const isStale = readBoolean(metrics.isStale) ?? false;
  const dropRate = readNumber(metrics.dropRate) ?? 0;
  const systemStatus = deriveSystemStatus(isConnected, isStale, dropped, dropRate, queueUtilization);
  const storageHealth = deriveStorageHealth(systemStatus, dropped, queueUtilization);
  const sourceProvider = readString(metrics.sourceProvider);
  const providersTotal = sourceProvider || isConnected ? 1 : 0;
  const providersOnline = isConnected && providersTotal > 0 ? 1 : 0;

  return {
    systemStatus,
    providersOnline,
    providersTotal,
    activeRuns: 0,
    openPositions: 0,
    activeBackfills: 0,
    symbolsMonitored: 0,
    storageHealth,
    lastHeartbeatUtc: timestampUtc,
    metrics: [
      {
        id: "events",
        label: "Events Published",
        value: formatMetricNumber(published),
        delta: eventsPerSecond === null ? "Rate unavailable" : `${formatMetricNumber(eventsPerSecond)} / sec`,
        tone: systemStatus === "Offline" ? "danger" : "default"
      },
      {
        id: "drops",
        label: "Dropped Events",
        value: formatMetricNumber(dropped),
        delta: `${formatMetricNumber(dropRate)} drop rate`,
        tone: dropped > 0 || dropRate > 0 ? "warning" : "success"
      },
      {
        id: "queue",
        label: "Pipeline Queue",
        value: queueCapacity > 0 ? `${formatMetricNumber(queueSize)} / ${formatMetricNumber(queueCapacity)}` : formatMetricNumber(queueSize),
        delta: `${Math.round(queueUtilization * 100)}% utilized`,
        tone: queueUtilization >= 0.8 ? "warning" : "success"
      },
      {
        id: "historical-bars",
        label: "Historical Bars",
        value: formatMetricNumber(readNumber(metrics.historicalBars) ?? 0),
        delta: `${formatMetricNumber(readNumber(metrics.trades) ?? 0)} trades, ${formatMetricNumber(readNumber(metrics.depthUpdates) ?? 0)} depth updates`,
        tone: "default"
      }
    ],
    recentEvents: timestampUtc
      ? [
          {
            id: "host-status",
            type: systemStatus === "Offline" ? "error" : systemStatus === "Degraded" ? "warning" : "info",
            message: buildLegacyStatusMessage(systemStatus, readString(payload.uptime), sourceProvider),
            source: "Meridian host",
            timestamp: timestampUtc
          }
        ]
      : [],
    degradedMode: readDegradedMode(payload.degradedMode)
  };
}

function buildLegacyStatusMessage(
  systemStatus: SystemOverviewResponse["systemStatus"],
  uptime: string | null,
  sourceProvider: string | null
): string {
  const provider = sourceProvider ?? "local host pipeline";
  const suffix = uptime ? ` Uptime ${uptime}.` : "";

  if (systemStatus === "Offline") {
    return `Host connectivity is offline for ${provider}.${suffix}`;
  }

  if (systemStatus === "Degraded") {
    return `Host status is degraded for ${provider}.${suffix}`;
  }

  return `Host status is healthy for ${provider}.${suffix}`;
}

function readSystemStatus(value: unknown): SystemOverviewResponse["systemStatus"] {
  return value === "Healthy" || value === "Degraded" || value === "Offline" ? value : "Degraded";
}

function readStorageHealth(value: unknown): SystemOverviewResponse["storageHealth"] {
  return value === "Healthy" || value === "Warning" || value === "Critical" ? value : "Warning";
}

function readNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value : null;
}

function readBoolean(value: unknown): boolean | null {
  return typeof value === "boolean" ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function formatMetricNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: value >= 10 ? 0 : 2 });
}

// --- Runtime lifecycle ---

export function getRuntimeLifecycle(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").RuntimeLifecycleSnapshot>(WORKSTATION_API_ENDPOINTS.runtimeLifecycle, {
    ...options,
    allowDevelopmentFallback: false
  });
}

export function requestRuntimeShutdown(
  request: import("@/types").LifecycleShutdownRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").LifecycleShutdownAccepted>(WORKSTATION_API_ENDPOINTS.runtimeShutdown, request, options);
}

export function getRuntimeShutdownOperation(operationUri: string, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").LifecycleShutdownOperation>(operationUri, {
    ...options,
    allowDevelopmentFallback: false
  });
}

export function getLatestRuntimeShutdownReceipt(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").LifecycleShutdownReceipt>(WORKSTATION_API_ENDPOINTS.runtimeShutdownReceiptsLatest, {
    ...options,
    allowDevelopmentFallback: false
  });
}
