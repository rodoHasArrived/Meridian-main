import { useEffect, useState } from "react";
import { getSignedInUsername } from "@/lib/api/auth.api";

/**
 * The signed-in operator's username, read once per mount from `GET /api/auth/me`.
 *
 * `null` while loading, when no one is signed in, or when the read fails: callers must treat `null`
 * as "operator unknown" and skip per-operator state (recovery keys) rather than fall back to the
 * session's desk-label `displayName`.
 */
const defaultLoad = (signal: AbortSignal) => getSignedInUsername({ signal });

export function useSignedInUsername(load: (signal: AbortSignal) => Promise<string | null> = defaultLoad): string | null {
  const [username, setUsername] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal)
      .then((value) => { if (!controller.signal.aborted) setUsername(value); })
      .catch(() => { if (!controller.signal.aborted) setUsername(null); });
    return () => controller.abort();
  }, [load]);
  return username;
}
