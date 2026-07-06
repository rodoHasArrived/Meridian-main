// Shared client for the report-run status SSE stream. One EventSource per run id is
// shared across subscribers with refcounting. Consumers keep their polling path (the
// 30s reporting-workspace refresh) and this only pushes run/approval-state changes
// faster; any error flips the consumer unhealthy so polling stays first-class.
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

/** One audit-trail entry of a reporting run (server ReportingRunAuditEntryDto, camelCased). */
export interface ReportRunAuditEntry {
  runId: string;
  timestampUtc: string;
  action: string;
  actor: string;
  notes: string;
}

/** Run status + audit trail pushed by the stream (server ReportingRunAuditTrailDto, camelCased). */
export interface ReportRunStreamStatus {
  runId: string;
  templateId: string;
  asOfDate: string;
  status: string;
  trigger: string;
  attemptCount: number;
  entries: ReportRunAuditEntry[];
}

export interface ReportRunStreamHandlers {
  onStatus: (status: ReportRunStreamStatus) => void;
  onHealthChange?: (healthy: boolean) => void;
}

interface ReportRunStreamConnection {
  source: EventSource;
  handlers: Set<ReportRunStreamHandlers>;
  healthy: boolean;
  lastStatus: ReportRunStreamStatus | null;
  closeTimer: ReturnType<typeof setTimeout> | null;
}

const connections = new Map<string, ReportRunStreamConnection>();

// Grace period before an unreferenced connection closes, so StrictMode double-mounts
// and quick same-run transitions reuse it instead of churning.
const CLOSE_LINGER_MS = 1000;

export function buildReportRunStreamUrl(runId: string): string {
  return UI_API_ROUTES.ReportingRunStream.replace("{runId}", encodeURIComponent(runId.trim()));
}

export function isReportRunStreamSupported(): boolean {
  return typeof EventSource !== "undefined";
}

/**
 * Subscribe to pushed status for a reporting run. Returns an unsubscribe callback;
 * the underlying EventSource closes when the last subscriber for the run leaves. An
 * empty run id keeps the stream closed and reports unhealthy so polling stays in charge.
 */
export function subscribeReportRunStream(runId: string, handlers: ReportRunStreamHandlers): () => void {
  const trimmed = runId?.trim() ?? "";
  if (!isReportRunStreamSupported() || trimmed.length === 0) {
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  const url = buildReportRunStreamUrl(trimmed);
  let connection = connections.get(url);
  if (!connection) {
    connection = openConnection(url);
    connections.set(url, connection);
  } else if (connection.closeTimer !== null) {
    clearTimeout(connection.closeTimer);
    connection.closeTimer = null;
  }

  connection.handlers.add(handlers);
  handlers.onHealthChange?.(connection.healthy);
  if (connection.lastStatus) {
    handlers.onStatus(connection.lastStatus);
  }

  return () => {
    const current = connections.get(url);
    if (!current) {
      return;
    }

    current.handlers.delete(handlers);
    if (current.handlers.size === 0 && current.closeTimer === null) {
      current.closeTimer = setTimeout(() => {
        if (current.handlers.size === 0) {
          current.source.close();
          connections.delete(url);
        }
      }, CLOSE_LINGER_MS);
    }
  };
}

function openConnection(url: string): ReportRunStreamConnection {
  const source = new EventSource(url);
  const connection: ReportRunStreamConnection = {
    source,
    handlers: new Set(),
    healthy: false,
    lastStatus: null,
    closeTimer: null
  };

  const setHealthy = (healthy: boolean) => {
    if (connection.healthy === healthy) {
      return;
    }

    connection.healthy = healthy;
    for (const handler of connection.handlers) {
      handler.onHealthChange?.(healthy);
    }
  };

  source.addEventListener("report-run", (event) => {
    let status: ReportRunStreamStatus;
    try {
      status = JSON.parse((event as MessageEvent<string>).data) as ReportRunStreamStatus;
    } catch {
      return;
    }

    connection.lastStatus = status;
    setHealthy(true);
    for (const handler of connection.handlers) {
      handler.onStatus(status);
    }
  });

  // EventSource reconnects automatically; consumers resume polling while unhealthy.
  source.addEventListener("error", () => setHealthy(false));

  return connection;
}
