import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useQuotesStream } from "@/hooks/use-quotes-stream";

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly listeners = new Map<string, Set<(event: MessageEvent<string> | Event) => void>>();
  closed = false;

  constructor(readonly url: string) {
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

describe("useQuotesStream", () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal("EventSource", FakeEventSource);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("stays closed and unhealthy without symbols", () => {
    const { result } = renderHook(() => useQuotesStream([]));
    expect(result.current.healthy).toBe(false);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it("delivers pushed snapshots and closes the source on unmount", () => {
    const { result, unmount } = renderHook(() => useQuotesStream(["SPY"]));

    const payload = { timestamp: "2026-07-03T00:00:00Z", count: 0, quotes: [] };
    act(() => {
      FakeEventSource.instances[0]!.emit("quotes", JSON.stringify(payload));
    });

    expect(result.current.healthy).toBe(true);
    expect(result.current.snapshot).toEqual(payload);

    unmount();
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
  });
});
