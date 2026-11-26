import { describe, expect, it, vi } from "vitest";
import { combineLoadFailures, type PanelLoadStatus } from "@/lib/combined-load-state";

const status = (state: PanelLoadStatus["state"], error: unknown = null): PanelLoadStatus => ({ state, count: null, error, retry: vi.fn() });

describe("combineLoadFailures", () => {
  it("is null while nothing has failed", () => {
    expect(combineLoadFailures({ a: status("ready"), b: status("loading"), c: undefined })).toBeNull();
  });

  it("reports every failed panel once and retries all of them together", () => {
    const accruals = status("failed", new Error("first"));
    const queue = status("failed", new Error("second"));
    const ready = status("ready");
    const failure = combineLoadFailures({ accruals, ready, queue });
    expect(failure?.failedKeys).toEqual(["accruals", "queue"]);
    expect((failure?.error as Error).message).toBe("first");
    failure?.retry();
    expect(accruals.retry).toHaveBeenCalledTimes(1);
    expect(queue.retry).toHaveBeenCalledTimes(1);
    expect(ready.retry).not.toHaveBeenCalled();
  });
});
