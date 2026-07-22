import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  buildReportRunStreamUrl,
  isReportRunStreamSupported,
  subscribeReportRunStream,
  type ReportRunStreamStatus
} from "@/lib/report-run-stream";

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

const status: ReportRunStreamStatus = {
  runId: "job-1-20260501",
  templateId: "investor-monthly-statement",
  asOfDate: "2026-05-01",
  status: "InReview",
  trigger: "AdHoc",
  attemptCount: 1,
  entries: [
    { runId: "job-1-20260501", timestampUtc: "2026-05-01T12:00:00Z", action: "ApprovalTransition", actor: "bob", notes: "review" }
  ]
};

describe("report-run stream client", () => {
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

  it("builds a run-scoped, encoded stream URL", () => {
    expect(buildReportRunStreamUrl("  job-1-20260501  ")).toBe(
      "/api/fund-structure/reporting/runs/job-1-20260501/stream"
    );
  });

  it("shares one EventSource per run and closes after the linger on last unsubscribe", () => {
    const first = subscribeReportRunStream("job-1-20260501", { onStatus: vi.fn() });
    const second = subscribeReportRunStream("job-1-20260501", { onStatus: vi.fn() });

    expect(FakeEventSource.instances).toHaveLength(1);

    first();
    second();
    expect(FakeEventSource.instances[0]!.closed).toBe(false);

    vi.runAllTimers();
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
  });

  it("dispatches parsed status and health transitions", () => {
    const onStatus = vi.fn();
    const onHealthChange = vi.fn();
    const unsubscribe = subscribeReportRunStream("job-1-20260501", { onStatus, onHealthChange });

    expect(onHealthChange).toHaveBeenLastCalledWith(false);

    const source = FakeEventSource.instances[0]!;
    source.emit("report-run", JSON.stringify(status));
    expect(onStatus).toHaveBeenCalledWith(status);
    expect(onHealthChange).toHaveBeenLastCalledWith(true);

    source.emit("error");
    expect(onHealthChange).toHaveBeenLastCalledWith(false);

    source.emit("report-run", "not json");
    expect(onStatus).toHaveBeenCalledTimes(1);

    unsubscribe();
  });

  it("replays the last status to late subscribers", () => {
    const early = subscribeReportRunStream("job-1-20260501", { onStatus: vi.fn() });
    FakeEventSource.instances[0]!.emit("report-run", JSON.stringify(status));

    const onStatus = vi.fn();
    const late = subscribeReportRunStream("job-1-20260501", { onStatus });
    expect(onStatus).toHaveBeenCalledWith(status);

    early();
    late();
  });

  it("reports empty run id and unsupported environments as unhealthy without subscribing", () => {
    const onHealthChange = vi.fn();
    subscribeReportRunStream("   ", { onStatus: vi.fn(), onHealthChange });
    expect(onHealthChange).toHaveBeenCalledWith(false);
    expect(FakeEventSource.instances).toHaveLength(0);

    vi.unstubAllGlobals();
    expect(isReportRunStreamSupported()).toBe(false);
    const onHealthChange2 = vi.fn();
    subscribeReportRunStream("job-1-20260501", { onStatus: vi.fn(), onHealthChange: onHealthChange2 });
    expect(onHealthChange2).toHaveBeenCalledWith(false);
  });
});
