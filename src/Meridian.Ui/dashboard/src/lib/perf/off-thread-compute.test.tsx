import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  useOffThreadCompute,
  wrapComputeWorker,
  type ComputeWorkerHandle
} from "./off-thread-compute";

describe("useOffThreadCompute", () => {
  it("computes synchronously on first paint and when no worker is available", async () => {
    const computeSync = vi.fn((n: number) => n * 2);
    const { result, rerender } = renderHook(
      ({ n }) =>
        useOffThreadCompute<number, number>({
          input: n,
          keyOf: String,
          computeSync,
          createWorker: () => null
        }),
      { initialProps: { n: 2 } }
    );

    expect(result.current.value).toBe(4);
    rerender({ n: 5 });
    await waitFor(() => expect(result.current.value).toBe(10));
  });

  it("offloads recomputes to the worker while showing the previous value", async () => {
    const run = vi.fn((n: number) => Promise.resolve(n * 100));
    const dispose = vi.fn();
    const handle: ComputeWorkerHandle<number, number> = { supported: true, run, dispose };

    const { result, rerender, unmount } = renderHook(
      ({ n }) =>
        useOffThreadCompute<number, number>({
          input: n,
          keyOf: String,
          computeSync: (x) => x * 2,
          createWorker: () => handle
        }),
      { initialProps: { n: 2 } }
    );

    // First paint is synchronous.
    expect(result.current.value).toBe(4);

    rerender({ n: 3 });
    await waitFor(() => expect(result.current.value).toBe(300));
    expect(run).toHaveBeenCalledWith(3);

    unmount();
    expect(dispose).toHaveBeenCalled();
  });

  it("falls back to synchronous computation when the worker rejects", async () => {
    const handle: ComputeWorkerHandle<number, number> = {
      supported: true,
      run: () => Promise.reject(new Error("worker died")),
      dispose: vi.fn()
    };

    const { result, rerender } = renderHook(
      ({ n }) =>
        useOffThreadCompute<number, number>({
          input: n,
          keyOf: String,
          computeSync: (x) => x * 2,
          createWorker: () => handle
        }),
      { initialProps: { n: 2 } }
    );

    rerender({ n: 9 });
    await waitFor(() => expect(result.current.value).toBe(18));
  });
});

describe("wrapComputeWorker", () => {
  class FakeWorker {
    onmessage: ((event: MessageEvent) => void) | null = null;
    onerror: ((event: { message: string }) => void) | null = null;
    terminated = false;

    postMessage(request: { id: number; input: number }) {
      // Echo back a doubled value on the next tick, id-matched.
      queueMicrotask(() =>
        this.onmessage?.({ data: { id: request.id, ok: true, result: request.input * 2 } } as MessageEvent)
      );
    }

    terminate() {
      this.terminated = true;
    }
  }

  it("resolves id-matched requests through the message channel", async () => {
    const fake = new FakeWorker();
    const handle = wrapComputeWorker<number, number>(fake as unknown as Worker);

    await expect(handle.run(21)).resolves.toBe(42);
    await expect(handle.run(50)).resolves.toBe(100);

    handle.dispose();
    expect(fake.terminated).toBe(true);
  });

  it("rejects pending requests on worker error", async () => {
    const fake = new FakeWorker();
    // Never echo; trip the error handler instead.
    fake.postMessage = () => undefined;
    const handle = wrapComputeWorker<number, number>(fake as unknown as Worker);

    const pending = handle.run(1);
    fake.onerror?.({ message: "boom" });
    await expect(pending).rejects.toThrow("boom");
  });
});
