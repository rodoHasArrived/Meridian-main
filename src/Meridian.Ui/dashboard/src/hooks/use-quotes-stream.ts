import { useEffect, useMemo, useState } from "react";
import { subscribeQuotesStream } from "@/lib/quotes-stream";
import type { QuotesSnapshotResponse } from "@/types";

export interface QuotesStreamState {
  snapshot: QuotesSnapshotResponse | null;
  /** True while the SSE channel is delivering; consumers suspend polling then. */
  healthy: boolean;
}

/**
 * React binding for the shared workstation quote stream. Pass the subscribed
 * symbols; an empty list keeps the stream closed and `healthy` false so the
 * caller's polling fallback stays in charge.
 */
export function useQuotesStream(symbols: readonly string[]): QuotesStreamState {
  const symbolsKey = useMemo(
    () => [...new Set(symbols.map((symbol) => symbol.trim().toUpperCase()).filter(Boolean))].sort().join(","),
    [symbols]
  );
  const [state, setState] = useState<QuotesStreamState>({ snapshot: null, healthy: false });

  useEffect(() => {
    if (!symbolsKey) {
      setState({ snapshot: null, healthy: false });
      return;
    }

    const unsubscribe = subscribeQuotesStream(symbolsKey.split(","), {
      onSnapshot: (snapshot) => setState((current) => ({ ...current, snapshot })),
      onHealthChange: (healthy) => setState((current) => (current.healthy === healthy ? current : { ...current, healthy }))
    });

    return () => {
      unsubscribe();
      setState({ snapshot: null, healthy: false });
    };
  }, [symbolsKey]);

  return state;
}
