import { useCallback, useEffect, useMemo, useRef } from "react";

// Debounced, cancellable auto-preview for report-writer grids. Each grid owns one timer and one
// in-flight AbortController; starting a render aborts the grid's previous render, so a result
// whose signal is aborted is out of date and must not be applied.
// Design: docs/engineering/blueprints/report-writer-auto-preview-blueprint.md

export const REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS = 600;
/** Datasets larger than this pause auto-preview; the operator previews them manually. */
export const REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS = 200;

export type ReportWriterPreviewPhase = "idle" | "stale" | "rendering" | "ready" | "error";

export interface ReportWriterQuietPreviewError {
  summary: string;
  details: string[];
}

export interface ReportWriterAutoPreviewOptions {
  debounceMs?: number;
  /** Re-checked when the timer fires, so a grid paused after scheduling does not render. */
  canAutoRender: (gridId: string) => boolean;
  /** Renders one grid. Must not apply its result once `signal` is aborted. */
  render: (gridId: string, signal: AbortSignal) => Promise<void>;
}

export interface ReportWriterAutoPreviewController {
  schedule: (gridId: string) => void;
  /** Renders immediately, skipping the debounce (used by Retry). */
  runNow: (gridId: string) => void;
  cancel: (gridId: string) => void;
  cancelAll: () => void;
  isPending: (gridId: string) => boolean;
}

export function isAbortError(error: unknown): boolean {
  return typeof error === "object"
    && error !== null
    && "name" in error
    && (error as { name?: unknown }).name === "AbortError";
}

export function useReportWriterAutoPreview(options: ReportWriterAutoPreviewOptions): ReportWriterAutoPreviewController {
  const optionsRef = useRef(options);
  useEffect(() => {
    optionsRef.current = options;
  });

  const timersRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const controllersRef = useRef(new Map<string, AbortController>());

  const clearTimer = useCallback((gridId: string) => {
    const timer = timersRef.current.get(gridId);
    if (timer !== undefined) {
      clearTimeout(timer);
      timersRef.current.delete(gridId);
    }
  }, []);

  const abortInFlight = useCallback((gridId: string) => {
    const controller = controllersRef.current.get(gridId);
    if (controller) {
      controller.abort();
      controllersRef.current.delete(gridId);
    }
  }, []);

  const run = useCallback((gridId: string) => {
    clearTimer(gridId);
    if (!optionsRef.current.canAutoRender(gridId)) {
      return;
    }

    abortInFlight(gridId);
    const controller = new AbortController();
    controllersRef.current.set(gridId, controller);
    void optionsRef.current.render(gridId, controller.signal)
      .catch((error: unknown) => {
        if (!isAbortError(error)) {
          throw error;
        }
      })
      .finally(() => {
        if (controllersRef.current.get(gridId) === controller) {
          controllersRef.current.delete(gridId);
        }
      });
  }, [abortInFlight, clearTimer]);

  const schedule = useCallback((gridId: string) => {
    clearTimer(gridId);
    const delay = optionsRef.current.debounceMs ?? REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS;
    timersRef.current.set(gridId, setTimeout(() => run(gridId), delay));
  }, [clearTimer, run]);

  const cancel = useCallback((gridId: string) => {
    clearTimer(gridId);
    abortInFlight(gridId);
  }, [abortInFlight, clearTimer]);

  const cancelAll = useCallback(() => {
    for (const gridId of [...timersRef.current.keys(), ...controllersRef.current.keys()]) {
      cancel(gridId);
    }
  }, [cancel]);

  const isPending = useCallback(
    (gridId: string) => timersRef.current.has(gridId) || controllersRef.current.has(gridId),
    []
  );

  useEffect(() => cancelAll, [cancelAll]);

  return useMemo(
    () => ({ schedule, runNow: run, cancel, cancelAll, isPending }),
    [cancel, cancelAll, isPending, run, schedule]
  );
}
