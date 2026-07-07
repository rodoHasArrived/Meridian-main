import { createComputeWorker, type ComputeWorkerHandle } from "@/lib/perf/off-thread-compute";
import type { CandlestickIndicatorSet } from "./indicators";

export type IndicatorsWorkerHandle = ComputeWorkerHandle<number[], CandlestickIndicatorSet>;

/**
 * Create a worker handle that computes candlestick indicators off the main
 * thread. Returns null when Web Workers are unavailable (SSR / jsdom tests) so
 * callers fall back to synchronous computation. The `new Worker(new URL(...))`
 * form lets Vite statically bundle the worker chunk.
 */
export function createIndicatorsWorker(): IndicatorsWorkerHandle | null {
  return createComputeWorker<number[], CandlestickIndicatorSet>(
    () => new Worker(new URL("./indicators.worker.ts", import.meta.url), { type: "module" })
  );
}
