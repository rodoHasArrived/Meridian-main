import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { buildQuotesStreamUrl, isQuotesStreamSupported, subscribeQuotesStream } from "@/lib/quotes-stream";
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

describe("quotes stream client", () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal("EventSource", FakeEventSource);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("builds a normalized, deduplicated stream URL", () => {
    expect(buildQuotesStreamUrl(["msft", "AAPL", "aapl "])).toBe(
      "/api/workstation/stream?symbols=AAPL%2CMSFT"
    );
  });

  it("shares one EventSource per symbol set and closes on last unsubscribe", () => {
    const first = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn() });
    const second = subscribeQuotesStream(["spy"], { onSnapshot: vi.fn() });

    expect(FakeEventSource.instances).toHaveLength(1);

    first();
    expect(FakeEventSource.instances[0]!.closed).toBe(false);
    second();
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

  it("reports unsupported environments as unhealthy without subscribing", () => {
    vi.unstubAllGlobals();
    expect(isQuotesStreamSupported()).toBe(false);

    const onHealthChange = vi.fn();
    const unsubscribe = subscribeQuotesStream(["SPY"], { onSnapshot: vi.fn(), onHealthChange });
    expect(onHealthChange).toHaveBeenCalledWith(false);
    unsubscribe();
  });
});
