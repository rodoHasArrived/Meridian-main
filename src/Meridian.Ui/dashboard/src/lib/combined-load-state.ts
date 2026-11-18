/**
 * Several panels that read from one backend fail together when that backend is down. Instead of a
 * notice and a Retry per panel, each panel reports its load status upward and the page shows one
 * notice with one Retry that reloads every failed panel.
 */
export interface PanelLoadStatus {
  state: "idle" | "loading" | "ready" | "failed";
  /** Rows the panel loaded, or null before it has loaded any. */
  count: number | null;
  error: unknown;
  /** Re-reads the panel's data. */
  retry: () => void;
}

export interface CombinedLoadFailure {
  /** The first failed panel's error, for the notice's wording. */
  error: unknown;
  /** Keys of every failed panel, in the order given. */
  failedKeys: string[];
  /** Reloads every failed panel. */
  retry: () => void;
}

export function combineLoadFailures(statuses: Readonly<Record<string, PanelLoadStatus | undefined>>): CombinedLoadFailure | null {
  const failed = Object.entries(statuses).filter((entry): entry is [string, PanelLoadStatus] => entry[1]?.state === "failed");
  if (failed.length === 0) return null;
  return {
    error: failed[0][1].error,
    failedKeys: failed.map(([key]) => key),
    retry: () => { for (const [, status] of failed) status.retry(); }
  };
}
