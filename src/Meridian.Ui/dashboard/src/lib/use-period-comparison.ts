import { useCallback, useEffect, useState } from "react";
import type { ApiRequestOptions } from "@/lib/api";
import type { PeriodComparison } from "@/lib/api/period-comparison.api";
import type { PeriodComparisonState } from "@/components/meridian/period-comparison-panel";

/**
 * Loads a prior-period comparison whenever `key` changes; a null key means there is nothing to
 * compare yet. A later request replaces an earlier one, and an aborted one never lands.
 */
export function usePeriodComparison(
  key: string | null,
  load: (options: ApiRequestOptions) => Promise<PeriodComparison>
): PeriodComparisonState & { retry: () => void } {
  const [state, setState] = useState<PeriodComparisonState & { key: string | null }>({ key: null, loading: false, error: null, result: null });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (key === null) {
      setState({ key, loading: false, error: null, result: null });
      return;
    }
    const controller = new AbortController();
    setState({ key, loading: true, error: null, result: null });
    load({ signal: controller.signal })
      .then((result) => { if (!controller.signal.aborted) setState({ key, loading: false, error: null, result }); })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) setState({ key, loading: false, error: failure instanceof Error ? failure.message : "The comparison could not be loaded.", result: null });
      });
    return () => controller.abort();
    // `load` is rebuilt each render; `key` captures everything it depends on.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, attempt]);
  const retry = useCallback(() => setAttempt((value) => value + 1), []);
  const current = state.key === key ? state : { loading: key !== null, error: null, result: null };
  return { loading: current.loading, error: current.error, result: current.result, retry };
}
