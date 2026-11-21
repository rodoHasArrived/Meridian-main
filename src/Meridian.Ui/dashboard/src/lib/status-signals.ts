/**
 * The one rule behind every workstation status indicator.
 *
 * The masthead attention pill, the per-page status row, the Daily Control Tower and the footer
 * status bar all classify, rank and summarize their facts through this module. A fact carries a
 * tone; this file alone decides whether that tone is an issue, which issue is the worst, and what
 * the summary says. Two surfaces given the same facts therefore cannot report different states.
 */

/**
 * `pending` (still loading) and `unknown` (loaded, but not established) are separate words for
 * the operator and rank the same: neither is confirmed, neither is a warning yet.
 */
export type StatusSignalTone = "ready" | "pending" | "unknown" | "review" | "blocked";

export interface StatusSignalLike {
  tone: StatusSignalTone;
}

export interface StatusSignalCounts {
  blocked: number;
  review: number;
  /** Pending and unknown facts together: not confirmed, not yet a warning. */
  unconfirmed: number;
}

export interface StatusSignalSummary<TSignal extends StatusSignalLike> {
  /** The worst tone present; `ready` when there are no signals. */
  tone: TSignal["tone"] | "ready";
  /** True when every signal is `ready`. */
  clear: boolean;
  /** Every signal that is not `ready`, worst first; equal tones keep their given order. */
  issues: TSignal[];
  /** The first entry of `issues`, or null when clear. */
  worst: TSignal | null;
  counts: StatusSignalCounts;
}

const TONE_RANK: Record<StatusSignalTone, number> = {
  ready: 0,
  pending: 1,
  unknown: 1,
  review: 2,
  blocked: 3
};

const TONE_LABEL: Record<StatusSignalTone, string> = {
  ready: "Ready",
  pending: "Pending",
  unknown: "Unknown",
  review: "Needs review",
  blocked: "Blocked"
};

export function statusToneRank(tone: StatusSignalTone): number {
  return TONE_RANK[tone];
}

/** The text every tone carries, so colour is never the only status signal. */
export function statusToneLabel(tone: StatusSignalTone): string {
  return TONE_LABEL[tone];
}

/** Anything short of `ready` is named to the operator. */
export function isStatusIssue(tone: StatusSignalTone): boolean {
  return tone !== "ready";
}

export function worstStatusTone(tones: Iterable<StatusSignalTone>): StatusSignalTone {
  let worst: StatusSignalTone = "ready";
  for (const tone of tones) {
    if (TONE_RANK[tone] > TONE_RANK[worst]) {
      worst = tone;
    }
  }
  return worst;
}

export function summarizeStatusSignals<TSignal extends StatusSignalLike>(
  signals: readonly TSignal[]
): StatusSignalSummary<TSignal> {
  // Array.prototype.sort is stable, so equal tones stay in the order the caller ranked them.
  const issues = signals
    .filter((signal) => isStatusIssue(signal.tone))
    .sort((left, right) => TONE_RANK[right.tone] - TONE_RANK[left.tone]);
  const counts: StatusSignalCounts = { blocked: 0, review: 0, unconfirmed: 0 };
  for (const issue of issues) {
    if (issue.tone === "blocked") {
      counts.blocked += 1;
    } else if (issue.tone === "review") {
      counts.review += 1;
    } else {
      counts.unconfirmed += 1;
    }
  }

  return {
    tone: issues[0]?.tone ?? "ready",
    clear: issues.length === 0,
    issues,
    worst: issues[0] ?? null,
    counts
  };
}
