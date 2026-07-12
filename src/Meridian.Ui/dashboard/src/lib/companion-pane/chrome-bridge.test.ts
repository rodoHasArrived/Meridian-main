import { describe, expect, it, vi } from "vitest";
import {
  createCompanionBridge,
  normalizeCompanionBridgeMessage,
  type BroadcastChannelLike,
  type CompanionBridgeMessage
} from "@/lib/companion-pane/chrome-bridge";

class FakeChannel implements BroadcastChannelLike {
  posted: unknown[] = [];
  closed = false;
  private listeners = new Set<(event: MessageEvent) => void>();

  postMessage(message: unknown) {
    this.posted.push(message);
  }
  addEventListener(_type: "message", listener: (event: MessageEvent) => void) {
    this.listeners.add(listener);
  }
  removeEventListener(_type: "message", listener: (event: MessageEvent) => void) {
    this.listeners.delete(listener);
  }
  close() {
    this.closed = true;
  }
  emit(data: unknown) {
    this.listeners.forEach((listener) => listener({ data } as MessageEvent));
  }
}

describe("normalizeCompanionBridgeMessage", () => {
  it("accepts well-formed messages", () => {
    expect(normalizeCompanionBridgeMessage({ type: "appearance", appearance: "dark" })).toEqual({
      type: "appearance",
      appearance: "dark"
    });
    expect(normalizeCompanionBridgeMessage({ type: "scope", scope: { symbol: "AAPL" } })).toEqual({
      type: "scope",
      scope: { symbol: "AAPL" }
    });
    expect(normalizeCompanionBridgeMessage({ type: "session-expired" })).toEqual({ type: "session-expired" });
  });

  it("accepts well-formed quotes and quotes-request messages", () => {
    const snapshot = { timestamp: "2026-07-05T00:00:00Z", count: 0, quotes: [] };
    expect(
      normalizeCompanionBridgeMessage({ type: "quotes", symbolsKey: "AAPL,MSFT", snapshot })
    ).toEqual({ type: "quotes", symbolsKey: "AAPL,MSFT", snapshot });
    expect(normalizeCompanionBridgeMessage({ type: "quotes-request", symbolsKey: "SPY" })).toEqual({
      type: "quotes-request",
      symbolsKey: "SPY"
    });
  });

  it("rejects malformed or unknown payloads", () => {
    expect(normalizeCompanionBridgeMessage(null)).toBeNull();
    expect(normalizeCompanionBridgeMessage("nope")).toBeNull();
    expect(normalizeCompanionBridgeMessage({ type: "appearance", appearance: "neon" })).toBeNull();
    expect(normalizeCompanionBridgeMessage({ type: "scope", scope: "x" })).toBeNull();
    expect(normalizeCompanionBridgeMessage({ type: "other" })).toBeNull();
    // quotes with a missing/invalid snapshot or empty key is rejected.
    expect(normalizeCompanionBridgeMessage({ type: "quotes", symbolsKey: "SPY" })).toBeNull();
    expect(
      normalizeCompanionBridgeMessage({ type: "quotes", symbolsKey: "SPY", snapshot: { count: 1 } })
    ).toBeNull();
    expect(
      normalizeCompanionBridgeMessage({
        type: "quotes",
        symbolsKey: "",
        snapshot: { timestamp: "t", count: 0, quotes: [] }
      })
    ).toBeNull();
    expect(normalizeCompanionBridgeMessage({ type: "quotes-request" })).toBeNull();
    expect(normalizeCompanionBridgeMessage({ type: "quotes-request", symbolsKey: "" })).toBeNull();
  });
});

describe("createCompanionBridge", () => {
  it("posts to the channel and delivers normalized inbound messages", () => {
    const channel = new FakeChannel();
    const bridge = createCompanionBridge(channel);
    const handler = vi.fn();
    const unsubscribe = bridge.subscribe(handler);

    const message: CompanionBridgeMessage = { type: "appearance", appearance: "light" };
    bridge.post(message);
    expect(channel.posted).toEqual([message]);

    channel.emit({ type: "session-expired" });
    expect(handler).toHaveBeenCalledWith({ type: "session-expired" });

    channel.emit({ type: "garbage" }); // ignored
    expect(handler).toHaveBeenCalledTimes(1);

    unsubscribe();
    channel.emit({ type: "session-expired" });
    expect(handler).toHaveBeenCalledTimes(1);

    bridge.close();
    expect(channel.closed).toBe(true);
  });
});
