import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useReportRunStream } from "@/hooks/use-report-run-stream";

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

const status = {
  runId: "job-1-20260501",
  templateId: "investor-monthly-statement",
  asOfDate: "2026-05-01",
  status: "Approved",
  trigger: "AdHoc",
  attemptCount: 1,
  entries: []
};

describe("useReportRunStream", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    FakeEventSource.instances = [];
    vi.stubGlobal("EventSource", FakeEventSource);
  });

  afterEach(() => {
    vi.runAllTimers();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("stays closed and unhealthy without a run id", () => {
    const { result } = renderHook(() => useReportRunStream(null));
    expect(result.current.healthy).toBe(false);
    expect(result.current.status).toBeNull();
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it("delivers pushed status and closes the source on unmount", () => {
    const { result, unmount } = renderHook(() => useReportRunStream("job-1-20260501"));

    act(() => {
      FakeEventSource.instances[0]!.emit("report-run", JSON.stringify(status));
    });

    expect(result.current.healthy).toBe(true);
    expect(result.current.status).toEqual(status);

    unmount();
    act(() => {
      vi.runAllTimers();
    });
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
  });

  it("flips unhealthy on stream error so polling resumes", () => {
    const { result } = renderHook(() => useReportRunStream("job-1-20260501"));

    act(() => {
      FakeEventSource.instances[0]!.emit("report-run", JSON.stringify(status));
    });
    expect(result.current.healthy).toBe(true);

    act(() => {
      FakeEventSource.instances[0]!.emit("error");
    });
    expect(result.current.healthy).toBe(false);
  });
});
