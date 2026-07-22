// Same-origin cross-window bridge between the main workstation and its companion
// panes. Built on BroadcastChannel: the main window posts appearance, operating
// scope, and session-lifecycle updates; panes subscribe and apply them live.
// Persisted state (theme, scope) is already shared through same-origin
// localStorage on load — this channel carries the LIVE push so panes update
// without waiting on a storage round-trip.
import { isAppearance, type Appearance } from "@/lib/theme";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { QuotesSnapshotResponse } from "@/types";

export const COMPANION_CHANNEL_NAME = "meridian.workstation.companion.v1";

export type CompanionBridgeMessage =
  | { type: "appearance"; appearance: Appearance }
  | { type: "scope"; scope: AppShellOperatingScopeInput }
  | { type: "session-expired" }
  // Owner window re-broadcasts a quote snapshot for a symbol set so follower panes
  // consume the opener's feed instead of opening their own EventSource.
  | { type: "quotes"; symbolsKey: string; snapshot: QuotesSnapshotResponse }
  // Follower pane asks the owner to re-send the latest snapshot for a symbol set
  // (fast warm-up when a pane opens after the owner is already streaming).
  | { type: "quotes-request"; symbolsKey: string };

/** Structural check for a cross-window snapshot payload (untrusted data). */
function isQuotesSnapshot(value: unknown): value is QuotesSnapshotResponse {
  if (!value || typeof value !== "object") {
    return false;
  }
  const record = value as { timestamp?: unknown; count?: unknown; quotes?: unknown };
  return (
    typeof record.timestamp === "string" &&
    typeof record.count === "number" &&
    Array.isArray(record.quotes)
  );
}

/** Minimal BroadcastChannel surface, injectable for tests. */
export interface BroadcastChannelLike {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: MessageEvent) => void): void;
  removeEventListener(type: "message", listener: (event: MessageEvent) => void): void;
  close(): void;
}

/** Validate an inbound payload (cross-window data is untrusted). */
export function normalizeCompanionBridgeMessage(data: unknown): CompanionBridgeMessage | null {
  if (!data || typeof data !== "object") {
    return null;
  }
  const type = (data as { type?: unknown }).type;
  if (type === "appearance") {
    const appearance = (data as { appearance?: unknown }).appearance;
    return isAppearance(appearance) ? { type: "appearance", appearance } : null;
  }
  if (type === "scope") {
    const scope = (data as { scope?: unknown }).scope;
    return scope && typeof scope === "object"
      ? { type: "scope", scope: scope as AppShellOperatingScopeInput }
      : null;
  }
  if (type === "session-expired") {
    return { type: "session-expired" };
  }
  if (type === "quotes") {
    const record = data as { symbolsKey?: unknown; snapshot?: unknown };
    const snapshot = record.snapshot;
    return typeof record.symbolsKey === "string" && record.symbolsKey.length > 0 && isQuotesSnapshot(snapshot)
      ? { type: "quotes", symbolsKey: record.symbolsKey, snapshot }
      : null;
  }
  if (type === "quotes-request") {
    const symbolsKey = (data as { symbolsKey?: unknown }).symbolsKey;
    return typeof symbolsKey === "string" && symbolsKey.length > 0
      ? { type: "quotes-request", symbolsKey }
      : null;
  }
  return null;
}

export interface CompanionBridge {
  post(message: CompanionBridgeMessage): void;
  subscribe(handler: (message: CompanionBridgeMessage) => void): () => void;
  close(): void;
}

export function createCompanionBridge(channel: BroadcastChannelLike): CompanionBridge {
  const handlers = new Set<(message: CompanionBridgeMessage) => void>();
  const onMessage = (event: MessageEvent) => {
    const message = normalizeCompanionBridgeMessage(event.data);
    if (message) {
      handlers.forEach((handler) => handler(message));
    }
  };
  channel.addEventListener("message", onMessage);
  return {
    post: (message) => channel.postMessage(message),
    subscribe: (handler) => {
      handlers.add(handler);
      return () => {
        handlers.delete(handler);
      };
    },
    close: () => {
      handlers.clear();
      channel.removeEventListener("message", onMessage);
      channel.close();
    }
  };
}

/** Open the shared channel, or null when BroadcastChannel is unavailable. */
export function openCompanionChannel(name: string = COMPANION_CHANNEL_NAME): BroadcastChannelLike | null {
  if (typeof BroadcastChannel === "undefined") {
    return null;
  }
  return new BroadcastChannel(name);
}
