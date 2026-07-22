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
//
// Owner failure policy: a stream the browser fail-closes (HTTP 400/429/503 never
// auto-reconnects), a flapping stream (repeated errors with no frame between
// them), or a wedged stream (no `quotes`/`heartbeat` frame inside the stale
// window) is closed and re-probed on an escalating cooldown, a bounded number of
// times. Consumers poll while unhealthy, so giving up costs freshness, not data;
// a later subscribe for the same symbol set starts a fresh probe budget.
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
  /** Null while the connection is waiting out a reopen cooldown (or gave up). */
  source: EventSource | null;
  url: string;
  symbolsKey: string;
  handlers: Set<QuotesStreamHandlers>;
  healthy: boolean;
  lastSnapshot: QuotesSnapshotResponse | null;
  closeTimer: ReturnType<typeof setTimeout> | null;
  staleTimer: ReturnType<typeof setTimeout> | null;
  reopenTimer: ReturnType<typeof setTimeout> | null;
  reopenDelayMs: number;
  reopenAttempts: number;
  consecutiveErrors: number;
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

// The server writes an `event: heartbeat` frame every 15s while idle, so a healthy
// but quiet stream still produces frames. Three missed heartbeats means the stream
// is wedged (e.g. a buffering proxy) even though the browser reports it open.
const OWNER_STALE_MS = 45_000;

// Consecutive `error` events with no frame or open between them before the owner
// stops the browser's tight auto-reconnect loop and moves to timed probes.
const OWNER_MAX_CONSECUTIVE_ERRORS = 5;

// Escalating cooldown between reopen probes after a terminal failure, and the cap
// on probes before the owner gives up and leaves consumers on their polling
// fallback. A fresh subscribe for the symbol set starts a new budget.
const OWNER_REOPEN_BASE_DELAY_MS = 30_000;
const OWNER_REOPEN_MAX_DELAY_MS = 300_000;
const OWNER_MAX_REOPEN_ATTEMPTS = 5;

// EventSource.readyState CLOSED. Referenced numerically because the constructor in
// scope may be a test double without the static constants.
const EVENT_SOURCE_CLOSED = 2;

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
    connection = {
      source: null,
      url,
      symbolsKey,
      handlers: new Set(),
      healthy: false,
      lastSnapshot: null,
      closeTimer: null,
      staleTimer: null,
      reopenTimer: null,
      reopenDelayMs: OWNER_REOPEN_BASE_DELAY_MS,
      reopenAttempts: 0,
      consecutiveErrors: 0
    };
    connections.set(url, connection);
    attachOwnerSource(connection);
  } else {
    if (connection.closeTimer !== null) {
      clearTimeout(connection.closeTimer);
      connection.closeTimer = null;
    }
    // A dormant connection (probe budget exhausted: no source, no pending
    // probe) restarts on new interest with a fresh budget, so every existing
    // subscriber shares the recovery instead of being stranded on polling.
    if (connection.source === null && connection.reopenTimer === null) {
      connection.reopenDelayMs = OWNER_REOPEN_BASE_DELAY_MS;
      connection.reopenAttempts = 0;
      attachOwnerSource(connection);
    }
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
          closeOwnerConnection(current);
          connections.delete(url);
        }
      }, CLOSE_LINGER_MS);
    }
  };
}

function setOwnerHealthy(connection: OwnerConnection, healthy: boolean): void {
  if (connection.healthy === healthy) {
    return;
  }

  connection.healthy = healthy;
  for (const handler of connection.handlers) {
    handler.onHealthChange?.(healthy);
  }
}

/** Every data/heartbeat frame proves the stream works and restores the full failure budget. */
function markOwnerSourceAlive(connection: OwnerConnection): void {
  connection.consecutiveErrors = 0;
  connection.reopenDelayMs = OWNER_REOPEN_BASE_DELAY_MS;
  connection.reopenAttempts = 0;
  armOwnerStaleTimer(connection);
}

function armOwnerStaleTimer(connection: OwnerConnection): void {
  if (connection.staleTimer !== null) {
    clearTimeout(connection.staleTimer);
  }
  connection.staleTimer = setTimeout(() => {
    connection.staleTimer = null;
    // No frame inside the stale window: the stream is wedged open. Hand
    // consumers back to polling and probe again after the cooldown.
    failOwnerSource(connection);
  }, OWNER_STALE_MS);
}

function attachOwnerSource(connection: OwnerConnection): void {
  const source = new EventSource(connection.url);
  connection.source = source;
  connection.consecutiveErrors = 0;
  armOwnerStaleTimer(connection);

  // Guard every listener on `connection.source === source`: a replaced or reaped
  // source can still emit late events that must not disturb its successor.
  //
  // A 200 handshake alone must not restore the failure budget: an accept-then-drop
  // loop (server crash-looping behind a proxy that streams the handshake) opens
  // successfully on every browser auto-reconnect and would otherwise never hit the
  // flap bound. Only real frames (quotes/heartbeat) prove the stream works.
  source.addEventListener("open", () => {
    if (connection.source !== source) {
      return;
    }
    armOwnerStaleTimer(connection);
  });

  source.addEventListener("quotes", (event) => {
    if (connection.source !== source) {
      return;
    }
    let snapshot: QuotesSnapshotResponse;
    try {
      snapshot = JSON.parse((event as MessageEvent<string>).data) as QuotesSnapshotResponse;
    } catch {
      return;
    }

    markOwnerSourceAlive(connection);
    connection.lastSnapshot = snapshot;
    setOwnerHealthy(connection, true);
    for (const handler of connection.handlers) {
      handler.onSnapshot(snapshot);
    }
    // Re-broadcast to any companion panes following this symbol set.
    getStreamBridge()?.post({ type: "quotes", symbolsKey: connection.symbolsKey, snapshot });
  });

  // Heartbeats prove the channel is alive through a quiet market. They re-arm the
  // watchdog but do not flip healthy — health comes from data frames only.
  source.addEventListener("heartbeat", () => {
    if (connection.source !== source) {
      return;
    }
    markOwnerSourceAlive(connection);
  });

  source.addEventListener("error", () => {
    if (connection.source !== source) {
      return;
    }
    setOwnerHealthy(connection, false);
    if (source.readyState === EVENT_SOURCE_CLOSED) {
      // The browser fail-closed the stream (e.g. HTTP 400/429/503) and will never
      // auto-reconnect on its own. Probe again after the cooldown.
      failOwnerSource(connection);
      return;
    }
    connection.consecutiveErrors += 1;
    if (connection.consecutiveErrors >= OWNER_MAX_CONSECUTIVE_ERRORS) {
      failOwnerSource(connection);
    }
  });
}

/** Stops the current source and schedules a bounded reopen probe. */
function failOwnerSource(connection: OwnerConnection): void {
  if (connection.staleTimer !== null) {
    clearTimeout(connection.staleTimer);
    connection.staleTimer = null;
  }
  connection.source?.close();
  connection.source = null;
  setOwnerHealthy(connection, false);
  scheduleOwnerReopen(connection);
}

function scheduleOwnerReopen(connection: OwnerConnection): void {
  if (connection.reopenTimer !== null) {
    return;
  }
  if (connection.reopenAttempts >= OWNER_MAX_REOPEN_ATTEMPTS) {
    // Probe budget exhausted — go dormant but stay registered so current
    // subscribers keep their polling fallback and the next subscribe restarts
    // the stream for all of them together with a fresh budget.
    return;
  }
  connection.reopenAttempts += 1;
  const delay = connection.reopenDelayMs;
  connection.reopenDelayMs = Math.min(connection.reopenDelayMs * 2, OWNER_REOPEN_MAX_DELAY_MS);
  connection.reopenTimer = setTimeout(() => {
    connection.reopenTimer = null;
    if (connection.handlers.size === 0) {
      connections.delete(connection.url);
      return;
    }
    attachOwnerSource(connection);
  }, delay);
}

function closeOwnerConnection(connection: OwnerConnection): void {
  if (connection.closeTimer !== null) {
    clearTimeout(connection.closeTimer);
    connection.closeTimer = null;
  }
  if (connection.staleTimer !== null) {
    clearTimeout(connection.staleTimer);
    connection.staleTimer = null;
  }
  if (connection.reopenTimer !== null) {
    clearTimeout(connection.reopenTimer);
    connection.reopenTimer = null;
  }
  connection.source?.close();
  connection.source = null;
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
  const isNewConnection = !connection;
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
  handlers.onHealthChange?.(connection.healthy);
  if (connection.lastSnapshot) {
    handlers.onSnapshot(connection.lastSnapshot);
  }

  // Warm up the opener once, when this symbol set is first followed. Re-mounts of
  // additional subscribers reuse the connection and must not re-request or reset
  // the stale watchdog — the watchdog is (re)armed only on real snapshot receipt.
  if (isNewConnection) {
    bridge.post({ type: "quotes-request", symbolsKey });
  }

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
  // Once unhealthy, the watchdog has nothing left to do — drop it so it cannot
  // fire redundantly. A later snapshot re-arms it via armFollowerStaleTimer.
  if (!healthy && connection.staleTimer !== null) {
    clearTimeout(connection.staleTimer);
    connection.staleTimer = null;
  }
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
    closeOwnerConnection(connection);
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
