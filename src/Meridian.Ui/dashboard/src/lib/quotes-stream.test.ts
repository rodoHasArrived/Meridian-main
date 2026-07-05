import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  buildQuotesStreamUrl,
  isQuotesStreamSupported,
  resetQuotesStreamForTests,
  setQuotesStreamChannelForTests,
  setQuotesStreamRoleForTests,
  subscribeQuotesStream
} from "@/lib/quotes-stream";
import type { BroadcastChannelLike } from "@/lib/companion-pane/chrome-bridge";
import type { QuotesSnapshotResponse } from "@/types";

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  readonly listeners = new Map<string, Set<(event: MessageEvent<string> | Event) => void>>();
  closed = false;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent<string> | Event) => void) {
    const set = this.listeners.get(type) ?? new Set();
    set.add(listener);
    this.listeners.set(type, set);
  }

  emit(type: string, data?: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(data === undefined ? new Event(type) : new MessageEvent(type, { data }));
    }
  }

  close() {
    this.closed = true;
  }
}

// Same-origin channel double: postMessage records (a real BroadcastChannel never
// echoes to its own instance); emit() simulates an inbound message from the other
// window and is delivered to subscribers.
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

const snapshot: QuotesSnapshotResponse = {
  timestamp: "2026-07-03T00:00:00Z",
  count: 1,
  quotes: [
    {
      symbol: "SPY",
      timestamp: "2026-07-03T00:00:00Z",
      bidPrice: 450,
      bidSize: 100,
      askPrice: 450.05,
      askSize: 200,
      midPrice: 450.025,
      spread: 0.05,
      lastPrice: null,
      lastSize: null,
      lastTradeTimestamp: null,
      sequenceNumber: 1,
      streamId: "TEST",
      venue: "NYSE",
      session: null
    }
  ]
};

let fakeChannel: FakeChannel;

describe("quotes stream client", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    FakeEventSource.instances = [];
    vi.stubGlobal("EventSource", FakeEventSource);
    fakeChannel = new FakeChannel();
    setQuotesStreamChannelForTests(() => fakeChannel);
    setQuotesStreamRoleForTests("owner");
  });

  afterEach(() => {
    // Drain any pending timers so connections never leak into the next test.
    vi.runAllTimers();
    resetQuotesStreamForTests();
    setQuotesStreamRoleForTests(null);
    setQuotesStreamChannelForTests(null);
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("builds a normalized, deduplicated stream URL", () => {
    expect(buildQuotesStreamUrl(["msft", "AAPL", "aapl "])).toBe(
      "/api/workstation/stream?symbols=AAPL%2CMSFT"
    );
  });

  it("shares one EventSource per symbol set and closes after the linger on last unsubscribe", () => {
    const first = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    const second = subscribeQuotesStream(["spy"], { onSnapshot: vi.fn() });

    expect(FakeEventSource.instances).toHaveLength(1);

    first();
    second();
    expect(FakeEventSource.instances[0]!.closed).toBe(false);

    vi.runAllTimers();
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
  });

  it("reuses a lingering connection when a subscriber returns before the close fires", () => {
    const first = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    first();

    const second = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    vi.runAllTimers();

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0]!.closed).toBe(false);

    second();
    vi.runAllTimers();
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
  });

  it("dispatches parsed snapshots and health transitions", () => {
    const onSnapshot = vi.fn();
    const onHealthChange = vi.fn();
    const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot, onHealthChange });

    expect(onHealthChange).toHaveBeenLastCalledWith(false);

    const source = FakeEventSource.instances[0]!;
    source.emit("quotes", JSON.stringify(snapshot));
    expect(onSnapshot).toHaveBeenCalledWith(snapshot);
    expect(onHealthChange).toHaveBeenLastCalledWith(true);

    source.emit("error");
    expect(onHealthChange).toHaveBeenLastCalledWith(false);

    source.emit("quotes", "not json");
    expect(onSnapshot).toHaveBeenCalledTimes(1);

    unsubscribe();
  });

  it("replays the last snapshot to late subscribers", () => {
    const early = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    FakeEventSource.instances[0]!.emit("quotes", JSON.stringify(snapshot));

    const onSnapshot = vi.fn();
    const late = subscribeQuotesStream(["SPY"], { onSnapshot });
    expect(onSnapshot).toHaveBeenCalledWith(snapshot);

    early();
    late();
  });

  it("owner re-broadcasts each snapshot and answers follower warm-up requests", () => {
    const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    FakeEventSource.instances[0]!.emit("quotes", JSON.stringify(snapshot));

    // The snapshot is pushed to any companion panes following this symbol set.
    expect(fakeChannel.posted).toContainEqual({ type: "quotes", symbolsKey: "SPY", snapshot });

    // A follower requests the current snapshot; the owner replies with the last one.
    fakeChannel.posted = [];
    fakeChannel.emit({ type: "quotes-request", symbolsKey: "SPY" });
    expect(fakeChannel.posted).toContainEqual({ type: "quotes", symbolsKey: "SPY", snapshot });

    unsubscribe();
  });

  it("reports unsupported environments as unhealthy without subscribing", () => {
    vi.unstubAllGlobals();
    expect(isQuotesStreamSupported()).toBe(false);

    const onHealthChange = vi.fn();
    const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn(), onHealthChange });
    expect(onHealthChange).toHaveBeenCalledWith(false);
    unsubscribe();
  });

  describe("follower role", () => {
    beforeEach(() => {
      setQuotesStreamRoleForTests("follower");
    });

    it("follows the opener's feed without opening its own EventSource", () => {
      const onSnapshot = vi.fn();
      const onHealthChange = vi.fn();
      const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot, onHealthChange });

      // A pane opens no EventSource and asks the opener to warm it up.
      expect(FakeEventSource.instances).toHaveLength(0);
      expect(fakeChannel.posted).toContainEqual({ type: "quotes-request", symbolsKey: "SPY" });
      expect(onHealthChange).toHaveBeenLastCalledWith(false);

      // An inbound snapshot from the opener drives the follower healthy.
      fakeChannel.emit({ type: "quotes", symbolsKey: "SPY", snapshot });
      expect(onSnapshot).toHaveBeenCalledWith(snapshot);
      expect(onHealthChange).toHaveBeenLastCalledWith(true);

      // Opener gone (session-expired) → unhealthy → polling resumes.
      fakeChannel.emit({ type: "session-expired" });
      expect(onHealthChange).toHaveBeenLastCalledWith(false);

      unsubscribe();
    });

    it("degrades to unhealthy after the stale watchdog when snapshots stop", () => {
      const onHealthChange = vi.fn();
      const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn(), onHealthChange });

      fakeChannel.emit({ type: "quotes", symbolsKey: "SPY", snapshot });
      expect(onHealthChange).toHaveBeenLastCalledWith(true);

      vi.advanceTimersByTime(6000);
      expect(onHealthChange).toHaveBeenLastCalledWith(false);

      unsubscribe();
    });
  });
});
