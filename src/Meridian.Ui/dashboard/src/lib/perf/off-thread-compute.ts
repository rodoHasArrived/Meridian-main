import { useEffect, useRef, useState } from "react";

// A tiny primitive for moving pure, serializable computations off the main
// thread. It always keeps a synchronous fallback so the value is correct on the
// first paint and in environments without Web Workers (SSR, jsdom/tests): the
// first result is computed synchronously, and subsequent input changes are handed
// to the worker so interactive recomputes don't block the UI thread.

interface WorkerRequest<I> {
  id: number;
  input: I;
}

interface WorkerResponse<O> {
  id: number;
  ok: boolean;
  result?: O;
  error?: string;
}

export interface ComputeWorkerHandle<I, O> {
  /** True when a real Web Worker backs this handle. */
  readonly supported: boolean;
  run(input: I): Promise<O>;
  dispose(): void;
}

/** Wraps a raw Worker in an id-matched request/response client. */
export function wrapComputeWorker<I, O>(worker: Worker): ComputeWorkerHandle<I, O> {
  let nextId = 0;
  const pending = new Map<number, { resolve: (value: O) => void; reject: (error: Error) => void }>();

  worker.onmessage = (event: MessageEvent<WorkerResponse<O>>) => {
    const { id, ok, result, error } = event.data;
    const entry = pending.get(id);
    if (!entry) {
      return;
    }
    pending.delete(id);
    if (ok && result !== undefined) {
      entry.resolve(result);
    } else {
      entry.reject(new Error(error ?? "Worker computation failed."));
    }
  };
  worker.onerror = (event) => {
    const message = event.message ?? "Worker error.";
    for (const entry of pending.values()) {
      entry.reject(new Error(message));
    }
    pending.clear();
  };

  return {
    supported: true,
    run(input: I) {
      const id = (nextId += 1);
      return new Promise<O>((resolve, reject) => {
        pending.set(id, { resolve, reject });
        const request: WorkerRequest<I> = { id, input };
        worker.postMessage(request);
      });
    },
    dispose() {
      pending.clear();
      worker.terminate();
    }
  };
}

/**
 * Build a worker handle from a factory, degrading gracefully to `null` when Web
 * Workers are unavailable or construction throws (so callers fall back to the
 * synchronous path rather than crashing).
 */
export function createComputeWorker<I, O>(factory: () => Worker): ComputeWorkerHandle<I, O> | null {
  if (typeof Worker === "undefined") {
    return null;
  }
  try {
    return wrapComputeWorker<I, O>(factory());
  } catch {
    return null;
  }
}

export interface UseOffThreadComputeOptions<I, O> {
  input: I;
  /** Stable key identifying the input; recompute only when it changes. */
  keyOf: (input: I) => string;
  /** Correct synchronous computation, used for first paint and as fallback. */
  computeSync: (input: I) => O;
  /** Lazily create the worker handle (called once). Return null to stay synchronous. */
  createWorker?: () => ComputeWorkerHandle<I, O> | null;
}

export interface OffThreadComputeResult<O> {
  value: O;
  /** Key for the input that produced `value`; it can lag the current input while a worker runs. */
  valueKey: string;
  /** True while a worker recompute is in flight (previous value still shown). */
  computing: boolean;
}

/**
 * Returns the computed value for `input`, off the main thread when possible.
 * First render is synchronous (correct, no flash); later input changes are run on
 * the worker with the previous value shown until the worker responds.
 */
export function useOffThreadCompute<I, O>({
  input,
  keyOf,
  computeSync,
  createWorker
}: UseOffThreadComputeOptions<I, O>): OffThreadComputeResult<O> {
  const key = keyOf(input);
  const handleRef = useRef<ComputeWorkerHandle<I, O> | null | undefined>(undefined);
  if (handleRef.current === undefined) {
    handleRef.current = createWorker ? createWorker() : null;
  }

  const [state, setState] = useState<{ key: string; value: O }>(() => ({ key, value: computeSync(input) }));
  const [computing, setComputing] = useState(false);

  useEffect(() => {
    return () => {
      handleRef.current?.dispose();
      handleRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (state.key === key) {
      return;
    }

    const handle = handleRef.current;
    if (!handle?.supported) {
      // No worker: recompute synchronously off the render path.
      setState({ key, value: computeSync(input) });
      return;
    }

    let active = true;
    setComputing(true);
    handle
      .run(input)
      .then((value) => {
        if (active) {
          setState({ key, value });
        }
      })
      .catch(() => {
        // Worker failed mid-flight — fall back to a synchronous result.
        if (active) {
          setState({ key, value: computeSync(input) });
        }
      })
      .finally(() => {
        if (active) {
          setComputing(false);
        }
      });

    return () => {
      active = false;
    };
    // computeSync/input are captured via the key; recompute only when key changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return { value: state.value, valueKey: state.key, computing };
}
