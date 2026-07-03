// Shared client for the workstation SSE quote stream (live data spine, step 1).
// One EventSource per symbol set is shared across subscribers with refcounting;
// consumers keep their polling paths and suspend them only while the stream is
// healthy, so degraded environments fall back to polling automatically.
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { QuotesSnapshotResponse } from "@/types";

export interface QuotesStreamHandlers {
  onSnapshot: (snapshot: QuotesSnapshotResponse) => void;
  onHealthChange?: (healthy: boolean) => void;
}

interface QuotesStreamConnection {
  source: EventSource;
  handlers: Set<QuotesStreamHandlers>;
  healthy: boolean;
  lastSnapshot: QuotesSnapshotResponse | null;
}

const connections = new Map<string, QuotesStreamConnection>();

export function buildQuotesStreamUrl(symbols: readonly string[]): string {
  const normalized = [...new Set(symbols.map((symbol) => symbol.trim().toUpperCase()).filter(Boolean))].sort();
  return normalized.length > 0
    ? `${UI_API_ROUTES.WorkstationStream}?symbols=${encodeURIComponent(normalized.join(","))}`
    : UI_API_ROUTES.WorkstationStream;
}

export function isQuotesStreamSupported(): boolean {
  return typeof EventSource !== "undefined";
}

/**
 * Subscribes to pushed quote snapshots for the given symbols. Returns an
 * unsubscribe callback; the underlying EventSource closes when the last
 * subscriber for the same symbol set leaves.
 */
export function subscribeQuotesStream(
  symbols: readonly string[],
  handlers: QuotesStreamHandlers
): () => void {
  if (!isQuotesStreamSupported() || symbols.length === 0) {
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  const url = buildQuotesStreamUrl(symbols);
  let connection = connections.get(url);
  if (!connection) {
    connection = openConnection(url);
    connections.set(url, connection);
  }

  connection.handlers.add(handlers);
  handlers.onHealthChange?.(connection.healthy);
  if (connection.lastSnapshot) {
    handlers.onSnapshot(connection.lastSnapshot);
  }

  return () => {
    const current = connections.get(url);
    if (!current) {
      return;
    }

    current.handlers.delete(handlers);
    if (current.handlers.size === 0) {
      current.source.close();
      connections.delete(url);
    }
  };
}

function openConnection(url: string): QuotesStreamConnection {
  const source = new EventSource(url);
  const connection: QuotesStreamConnection = {
    source,
    handlers: new Set(),
    healthy: false,
    lastSnapshot: null
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

  source.addEventListener("quotes", (event) => {
    let snapshot: QuotesSnapshotResponse;
    try {
      snapshot = JSON.parse((event as MessageEvent<string>).data) as QuotesSnapshotResponse;
    } catch {
      return;
    }

    connection.lastSnapshot = snapshot;
    setHealthy(true);
    for (const handler of connection.handlers) {
      handler.onSnapshot(snapshot);
    }
  });

  // EventSource reconnects automatically; consumers resume polling while unhealthy.
  source.addEventListener("error", () => setHealthy(false));

  return connection;
}
