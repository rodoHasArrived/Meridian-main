import { useEffect, useState } from "react";
import { subscribeReportRunStream, type ReportRunStreamStatus } from "@/lib/report-run-stream";

export interface ReportRunStreamState {
  status: ReportRunStreamStatus | null;
  /** True while the SSE channel is delivering; consumers suspend polling reliance then. */
  healthy: boolean;
}

/**
 * React binding for the shared report-run status stream. Pass the run id to watch; a
 * null/empty id keeps the stream closed and `healthy` false so the caller's polling
 * fallback (the 30s reporting-workspace refresh) stays in charge.
 */
export function useReportRunStream(runId: string | null): ReportRunStreamState {
  const [state, setState] = useState<ReportRunStreamState>({ status: null, healthy: false });

  useEffect(() => {
    const trimmed = runId?.trim() ?? "";
    if (trimmed.length === 0) {
      setState({ status: null, healthy: false });
      return;
    }

    const unsubscribe = subscribeReportRunStream(trimmed, {
      onStatus: (status) => setState((current) => ({ ...current, status })),
      onHealthChange: (healthy) =>
        setState((current) => (current.healthy === healthy ? current : { ...current, healthy }))
    });

    return () => {
      unsubscribe();
      setState({ status: null, healthy: false });
    };
  }, [runId]);

  return state;
}
