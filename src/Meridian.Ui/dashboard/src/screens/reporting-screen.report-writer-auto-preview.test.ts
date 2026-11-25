import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  isAbortError,
  REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS,
  useReportWriterAutoPreview
} from "@/screens/reporting-screen.report-writer-auto-preview";

function deferred() {
  let resolve!: () => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("useReportWriterAutoPreview", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("uses a 600 ms default debounce", () => {
    expect(REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS).toBe(600);
  });

  it("collapses rapid edits into one render after the debounce", async () => {
    const render = vi.fn(async () => {});
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 100, canAutoRender: () => true, render }));

    act(() => {
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(60);
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(60);
      result.current.schedule("grid-a");
    });
    expect(render).not.toHaveBeenCalled();

    await act(async () => {
      vi.advanceTimersByTime(100);
    });
    expect(render).toHaveBeenCalledTimes(1);
    expect(render).toHaveBeenCalledWith("grid-a", expect.any(AbortSignal));
  });

  it("debounces each grid independently", async () => {
    const render = vi.fn(async (_gridId: string, _signal: AbortSignal) => {});
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 100, canAutoRender: () => true, render }));

    await act(async () => {
      result.current.schedule("grid-a");
      result.current.schedule("grid-b");
      vi.advanceTimersByTime(100);
    });

    expect(render.mock.calls.map(([gridId]) => gridId)).toEqual(["grid-a", "grid-b"]);
  });

  it("aborts the earlier render when a newer one starts, so its result is discarded", async () => {
    const pending = deferred();
    const signals: AbortSignal[] = [];
    const render = vi.fn((_gridId: string, signal: AbortSignal) => {
      signals.push(signal);
      return signals.length === 1 ? pending.promise : Promise.resolve();
    });
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 50, canAutoRender: () => true, render }));

    await act(async () => {
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(50);
    });
    expect(result.current.isPending("grid-a")).toBe(true);

    await act(async () => {
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(50);
    });

    expect(signals).toHaveLength(2);
    expect(signals[0].aborted).toBe(true);
    expect(signals[1].aborted).toBe(false);
    await act(async () => {
      pending.resolve();
    });
  });

  it("does not render when canAutoRender is false at fire time", async () => {
    let allowed = true;
    const render = vi.fn(async () => {});
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 50, canAutoRender: () => allowed, render }));

    act(() => {
      result.current.schedule("grid-a");
    });
    allowed = false;
    await act(async () => {
      vi.advanceTimersByTime(50);
    });

    expect(render).not.toHaveBeenCalled();
    expect(result.current.isPending("grid-a")).toBe(false);
  });

  it("swallows AbortError from a cancelled render", async () => {
    const pending = deferred();
    const render = vi.fn(() => pending.promise);
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 10, canAutoRender: () => true, render }));

    await act(async () => {
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(10);
    });
    act(() => {
      result.current.cancel("grid-a");
    });
    await act(async () => {
      pending.reject(new DOMException("aborted", "AbortError"));
    });

    expect(result.current.isPending("grid-a")).toBe(false);
  });

  it("cancels timers and in-flight renders on unmount", async () => {
    const signals: AbortSignal[] = [];
    const pending = deferred();
    const render = vi.fn((_gridId: string, signal: AbortSignal) => {
      signals.push(signal);
      return pending.promise;
    });
    const { result, unmount } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 10, canAutoRender: () => true, render }));

    await act(async () => {
      result.current.schedule("grid-a");
      vi.advanceTimersByTime(10);
      result.current.schedule("grid-b");
    });
    unmount();
    vi.advanceTimersByTime(50);

    expect(render).toHaveBeenCalledTimes(1);
    expect(signals[0].aborted).toBe(true);
    pending.resolve();
  });

  it("runNow skips the debounce", async () => {
    const render = vi.fn(async () => {});
    const { result } = renderHook(() => useReportWriterAutoPreview({ debounceMs: 1000, canAutoRender: () => true, render }));

    await act(async () => {
      result.current.runNow("grid-a");
    });

    expect(render).toHaveBeenCalledTimes(1);
  });
});

describe("isAbortError", () => {
  it("recognises abort errors only", () => {
    expect(isAbortError(new DOMException("aborted", "AbortError"))).toBe(true);
    expect(isAbortError(new Error("boom"))).toBe(false);
    expect(isAbortError(null)).toBe(false);
  });
});
