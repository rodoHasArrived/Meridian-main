// Shared client for the workstation SSE quote stream (live data spine).
//
// Owner window (the main workstation): one EventSource per symbol set, shared
// across subscribers with refcounting, exactly as before — and it re-broadcasts
// each snapshot over the same-origin companion BroadcastChannel.
//
// Follower window (a `/panes/*` companion pane): opens NO EventSource. It follows
// the opener's feed over the companion channel, so a pane beside a live main
// window costs one server connection, not two.
//
// Any failure — opener gone (`session-expired`), no snapshot within the stale
// window, or an unsupported environment — flips the consumer unhealthy so its
// polling fallback resumes. Fallback stays first-class everywhere.
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { QuotesSnapshotResponse } from "@/types";
import {
  createCompanionBridge,
  openCompanionChannel,
  type BroadcastChannelLike,
  type CompanionBridge
} from "@/lib/companion-pane/chrome-bridge";
import { isCompanionPaneRoute } from "@/lib/companion-pane/pane-window";

export interface QuotesStreamHandlers {
  onSnapshot: (snapshot: QuotesSnapshotResponse) => void;
  onHealthChange?: (healthy: boolean) => void;
}

export type QuotesStreamRole = "owner" | "follower";

interface OwnerConnection {
  source: EventSource;
  symbolsKey: string;
  handlers: Set<QuotesStreamHandlers>;
  healthy: boolean;
  lastSnapshot: QuotesSnapshotResponse | null;
  closeTimer: ReturnType<typeof setTimeout> | null;
}

interface FollowerConnection {
  symbolsKey: string;
  handlers: Set<QuotesStreamHandlers>;
  healthy: boolean;
  lastSnapshot: QuotesSnapshotResponse | null;
  staleTimer: ReturnType<typeof setTimeout> | null;
}

const connections = new Map<string, OwnerConnection>(); // owner, keyed by URL
const followerConnections = new Map<string, FollowerConnection>(); // follower, keyed by symbolsKey

// Grace period before an unreferenced owner connection closes, so StrictMode
// double-mounts and quick same-stream transitions reuse it instead of churning.
const CLOSE_LINGER_MS = 1000;

// A follower is healthy only while snapshots keep arriving from the opener. If the
// opener dies without firing `pagehide` (crash/kill), this watchdog flips the
// follower unhealthy so polling resumes.
const FOLLOWER_STALE_MS = 6000;

let roleOverride: QuotesStreamRole | null = null;
let channelOpener: () => BroadcastChannelLike | null = openCompanionChannel;
let streamBridge: CompanionBridge | null | undefined;
let ownerRequestUnsub: (() => void) | null = null;
let followerBridgeUnsub: (() => void) | null = null;

/** Role for this window: companion-pane routes follow, everything else owns. */
export function resolveQuotesStreamRole(): QuotesStreamRole {
  if (roleOverride !== null) {
    return roleOverride;
  }
  if (typeof window === "undefined") {
    return "owner";
  }
  return isCompanionPaneRoute(window.location.pathname) ? "follower" : "owner";
}

/** Lazily open the single shared companion bridge, or null when unavailable. */
function getStreamBridge(): CompanionBridge | null {
  if (streamBridge === undefined) {
    const channel = channelOpener();
    streamBridge = channel ? createCompanionBridge(channel) : null;
  }
  return streamBridge;
}

/** Normalized, deduplicated, sorted symbol-set key shared by URL and topic. */
export function normalizeQuotesSymbolKey(symbols: readonly string[]): string {
  return [...new Set(symbols.map((symbol) => symbol.trim().toUpperCase()).filter(Boolean))].sort().join(",");
}

export function buildQuotesStreamUrl(symbols: readonly string[]): string {
  const key = normalizeQuotesSymbolKey(symbols);
  return key.length > 0
    ? `${UI_API_ROUTES.WorkstationStream}?symbols=${encodeURIComponent(key)}`
    : UI_API_ROUTES.WorkstationStream;
}

export function isQuotesStreamSupported(): boolean {
  return typeof EventSource !== "undefined";
}

/**
 * Subscribes to pushed quote snapshots for the given symbols. Returns an
 * unsubscribe callback. In the owner window the underlying EventSource closes
 * when the last subscriber for the same symbol set leaves; in a follower pane no
 * EventSource is opened at all.
 */
export function subscribeQuotesStream(
  symbols: readonly string[],
  handlers: QuotesStreamHandlers
): () => void {
  if (symbols.length === 0) {
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  if (resolveQuotesStreamRole() === "follower") {
    return subscribeAsFollower(normalizeQuotesSymbolKey(symbols), handlers);
  }

  if (!isQuotesStreamSupported()) {
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  return subscribeAsOwner(symbols, handlers);
}

function subscribeAsOwner(symbols: readonly string[], handlers: QuotesStreamHandlers): () => void {
  const url = buildQuotesStreamUrl(symbols);
  const symbolsKey = normalizeQuotesSymbolKey(symbols);
  let connection = connections.get(url);
  if (!connection) {
    connection = openConnection(url, symbolsKey);
    connections.set(url, connection);
  } else if (connection.closeTimer !== null) {
    clearTimeout(connection.closeTimer);
    connection.closeTimer = null;
  }

  ensureOwnerRequestListener();

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

function openConnection(url: string, symbolsKey: string): OwnerConnection {
  const source = new EventSource(url);
  const connection: OwnerConnection = {
    source,
    symbolsKey,
    handlers: new Set(),
    healthy: false,
    lastSnapshot: null,
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
    // Re-broadcast to any companion panes following this symbol set.
    getStreamBridge()?.post({ type: "quotes", symbolsKey, snapshot });
  });

  // EventSource reconnects automatically; consumers resume polling while unhealthy.
  source.addEventListener("error", () => setHealthy(false));

  return connection;
}

/** Owner answers a follower's warm-up request with the latest snapshot it holds. */
function ensureOwnerRequestListener(): void {
  if (ownerRequestUnsub) {
    return;
  }
  const bridge = getStreamBridge();
  if (!bridge) {
    return;
  }
  ownerRequestUnsub = bridge.subscribe((message) => {
    if (message.type !== "quotes-request") {
      return;
    }
    for (const connection of connections.values()) {
      if (connection.symbolsKey === message.symbolsKey && connection.lastSnapshot) {
        bridge.post({ type: "quotes", symbolsKey: connection.symbolsKey, snapshot: connection.lastSnapshot });
        return;
      }
    }
  });
}

function subscribeAsFollower(symbolsKey: string, handlers: QuotesStreamHandlers): () => void {
  if (!symbolsKey) {
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  const bridge = getStreamBridge();
  if (!bridge) {
    // No cross-window channel — cannot follow; stay unhealthy so polling drives.
    handlers.onHealthChange?.(false);
    return () => undefined;
  }

  ensureFollowerListener(bridge);

  let connection = followerConnections.get(symbolsKey);
  if (!connection) {
    connection = {
      symbolsKey,
      handlers: new Set(),
      healthy: false,
      lastSnapshot: null,
      staleTimer: null
    };
    followerConnections.set(symbolsKey, connection);
  }

  connection.handlers.add(handlers);
  armFollowerStaleTimer(connection);
  handlers.onHealthChange?.(connection.healthy);
  if (connection.lastSnapshot) {
    handlers.onSnapshot(connection.lastSnapshot);
  }

  // Ask the opener to send the current snapshot immediately (fast warm-up).
  bridge.post({ type: "quotes-request", symbolsKey });

  return () => {
    const current = followerConnections.get(symbolsKey);
    if (!current) {
      return;
    }

    current.handlers.delete(handlers);
    if (current.handlers.size === 0) {
      if (current.staleTimer !== null) {
        clearTimeout(current.staleTimer);
      }
      followerConnections.delete(symbolsKey);
      if (followerConnections.size === 0) {
        detachFollowerListener();
      }
    }
  };
}

/** Follower delivers snapshots from inbound `quotes` messages and degrades on loss. */
function ensureFollowerListener(bridge: CompanionBridge): void {
  if (followerBridgeUnsub) {
    return;
  }
  followerBridgeUnsub = bridge.subscribe((message) => {
    if (message.type === "quotes") {
      const connection = followerConnections.get(message.symbolsKey);
      if (!connection) {
        return;
      }
      connection.lastSnapshot = message.snapshot;
      armFollowerStaleTimer(connection);
      setFollowerHealthy(connection, true);
      for (const handler of connection.handlers) {
        handler.onSnapshot(message.snapshot);
      }
    } else if (message.type === "session-expired") {
      // Opener is gone — flip every follower unhealthy so polling resumes.
      for (const connection of followerConnections.values()) {
        setFollowerHealthy(connection, false);
      }
    }
  });
}

function detachFollowerListener(): void {
  followerBridgeUnsub?.();
  followerBridgeUnsub = null;
}

function armFollowerStaleTimer(connection: FollowerConnection): void {
  if (connection.staleTimer !== null) {
    clearTimeout(connection.staleTimer);
  }
  connection.staleTimer = setTimeout(() => {
    setFollowerHealthy(connection, false);
  }, FOLLOWER_STALE_MS);
}

function setFollowerHealthy(connection: FollowerConnection, healthy: boolean): void {
  if (connection.healthy === healthy) {
    return;
  }
  connection.healthy = healthy;
  for (const handler of connection.handlers) {
    handler.onHealthChange?.(healthy);
  }
}

/** Test seam: force a role, or pass null to restore route-based detection. */
export function setQuotesStreamRoleForTests(role: QuotesStreamRole | null): void {
  roleOverride = role;
}

/** Test seam: inject the companion channel opener (null restores the default). */
export function setQuotesStreamChannelForTests(opener: (() => BroadcastChannelLike | null) | null): void {
  resetQuotesStreamForTests();
  channelOpener = opener ?? openCompanionChannel;
}

/** Test seam: tear down all connections, bridge listeners, and cached bridge. */
export function resetQuotesStreamForTests(): void {
  ownerRequestUnsub?.();
  ownerRequestUnsub = null;
  detachFollowerListener();
  for (const connection of connections.values()) {
    if (connection.closeTimer !== null) {
      clearTimeout(connection.closeTimer);
    }
    connection.source.close();
  }
  connections.clear();
  for (const connection of followerConnections.values()) {
    if (connection.staleTimer !== null) {
      clearTimeout(connection.staleTimer);
    }
  }
  followerConnections.clear();
  streamBridge?.close();
  streamBridge = undefined;
}
