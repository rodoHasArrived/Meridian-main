import { formatRelativeAge } from "@/lib/time";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";

export type FreshnessState = "fresh" | "aging" | "failing" | "live" | "unknown";

export interface FreshnessChipInput {
  /** ISO timestamp of the last successful update — lifecycle `lastSucceededAt` or a server as-of field. */
  timestamp: string | null;
  /** Age beyond which the data is presented as aging/stale. */
  staleBudgetMs: number;
  now: number;
  /** Subject used in the accessible label, e.g. "Quotes". */
  label?: string;
  /** Marks the surface as failing regardless of age. */
  errorMessage?: string | null;
  /** Suppresses the aging tone while a refresh is running. */
  inFlight?: boolean;
  /** Push-fed surface indicator (reserved for the live data spine). */
  live?: boolean;
  /** Extra hover detail such as retry metadata. */
  detail?: string | null;
}

export interface FreshnessChipViewModel {
  state: FreshnessState;
  badgeVariant: "outline" | "warning" | "danger" | "success";
  dot: boolean;
  text: string;
  ariaLabel: string;
  title: string | null;
}

export function buildFreshnessChipViewModel(input: FreshnessChipInput): FreshnessChipViewModel {
  const subject = input.label?.trim() || "Data";
  const age = formatRelativeAge(input.timestamp, input.now);

  if (input.live) {
    return {
      state: "live",
      badgeVariant: "success",
      dot: true,
      text: "Live",
      ariaLabel: `${subject} is streaming live.`,
      title: input.detail ?? null
    };
  }

  if (input.errorMessage) {
    return {
      state: "failing",
      badgeVariant: "danger",
      dot: false,
      text: input.timestamp ? `Failing · ${age}` : "Failing",
      ariaLabel: `${subject} refresh is failing: ${input.errorMessage}${input.timestamp ? ` Last success ${age}.` : ""}`,
      title: joinDetail(input.errorMessage, input.detail)
    };
  }

  if (!input.timestamp) {
    return {
      state: "unknown",
      badgeVariant: "outline",
      dot: false,
      text: "No data",
      ariaLabel: `${subject} has not been updated yet.`,
      title: input.detail ?? null
    };
  }

  const ageMs = input.now - new Date(input.timestamp).getTime();
  const aging = Number.isFinite(ageMs) && ageMs > input.staleBudgetMs && !input.inFlight;
  if (aging) {
    return {
      state: "aging",
      badgeVariant: "warning",
      dot: false,
      text: `Stale · ${age}`,
      ariaLabel: `${subject} is stale: last success ${age}.`,
      title: input.detail ?? null
    };
  }

  return {
    state: "fresh",
    badgeVariant: "outline",
    dot: false,
    text: age,
    ariaLabel: `${subject} updated ${age}.`,
    title: input.detail ?? null
  };
}

/** Maps a request lifecycle status onto chip input so screens share one derivation. */
export function freshnessInputFromLifecycle(
  status: RequestLifecycleStatus,
  staleBudgetMs: number,
  label?: string
): Omit<FreshnessChipInput, "now"> {
  const retry = status.backoff.nextRetryDelayMs;
  return {
    timestamp: status.lastSucceededAt,
    staleBudgetMs,
    label,
    errorMessage: status.phase === "failed" ? status.error : null,
    inFlight: status.inFlight,
    detail: retry != null ? `Retrying in ${Math.round(retry / 1000)}s` : null
  };
}

function joinDetail(errorMessage: string, detail: string | null | undefined): string {
  return detail ? `${errorMessage} — ${detail}` : errorMessage;
}
