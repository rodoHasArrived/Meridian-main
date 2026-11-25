import { useCallback, useEffect, useState } from "react";
import type { ApiRequestOptions } from "@/lib/api";

export type AssetResource<T> =
  | { status: "loading"; data: null; error: null }
  | { status: "ready"; data: T; error: null }
  | { status: "error"; data: null; error: unknown };

/** Each section can recover independently. A changed asset never exposes the prior asset's data. */
export function useAssetDetailResource<T>(
  securityId: string,
  load: (id: string, options: ApiRequestOptions) => Promise<T>
) {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<{
    securityId: string;
    attempt: number;
    result: AssetResource<T>;
  }>();
  const retry = useCallback(() => setAttempt((value) => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    setState({
      securityId,
      attempt,
      result: { status: "loading", data: null, error: null }
    });
    void load(securityId, {
      signal: controller.signal,
      allowDevelopmentFallback: false
    }).then(
      (data) => {
        if (current)
          setState({
            securityId,
            attempt,
            result: { status: "ready", data, error: null }
          });
      },
      (error: unknown) => {
        if (current)
          setState({
            securityId,
            attempt,
            result: { status: "error", data: null, error }
          });
      }
    );
    return () => {
      current = false;
      controller.abort();
    };
  }, [securityId, attempt, load]);

  const result: AssetResource<T> =
    state?.securityId === securityId && state.attempt === attempt
      ? state.result
      : { status: "loading", data: null, error: null };
  return { ...result, retry };
}
