import { formatCount, formatNumber, formatPercent, formatTimestamp } from "@/lib/format";
import type { BackfillProgressResponse, BackfillProviderProgressSnapshot } from "@/types";

export interface BackfillLiveProgressSymbolState {
  symbol: string;
  range: string;
  provider: string;
  attempt: string;
  status: string;
  progress: string;
  error: string | null;
}

export interface BackfillLiveProgressAttemptState {
  id: string;
  label: string;
  detail: string;
  tone: "default" | "warning" | "danger";
}

export interface BackfillLiveProgressState {
  ariaLabel: string;
  title: string;
  summary: string;
  active: boolean;
  overallPercent: number;
  symbols: BackfillLiveProgressSymbolState[];
  recentAttempts: BackfillLiveProgressAttemptState[];
  droppedNotificationWarning: string | null;
  observedAt: string;
}

export function buildBackfillLiveProgressState(
  response: BackfillProgressResponse | null
): BackfillLiveProgressState | null {
  const snapshot = response?.providerProgress;
  if (!snapshot) {
    return null;
  }

  const symbols = Object.values(snapshot.symbols)
    .sort((left, right) => left.symbol.localeCompare(right.symbol))
    .map((symbol) => ({
      symbol: symbol.symbol,
      range: formatBackfillRange(symbol.rangeStart, symbol.rangeEnd),
      provider: formatBackfillValue(symbol.currentProvider, "Waiting for provider"),
      attempt: symbol.providerAttempt > 0
        ? `Attempt ${symbol.providerAttempt}${symbol.retryRound > 0 ? `, retry ${symbol.retryRound}` : ""}`
        : "Not started",
      status: resolveBackfillProviderProgressStatus(symbol),
      progress: `${formatPercent(clampBackfillProgress(symbol.percentComplete), { maximumFractionDigits: 1 })}`,
      error: symbol.error
    }));
  const recentAttempts = snapshot.recentProviderAttempts.slice(-8).reverse().map((attempt) => ({
    id: [attempt.symbol, attempt.provider, attempt.providerAttempt, attempt.retryRound, attempt.observedAt].join("-"),
    label: `${attempt.symbol} · ${attempt.provider}`,
    detail: [
      formatBackfillRange(attempt.rangeStart, attempt.rangeEnd),
      `attempt ${attempt.providerAttempt}${attempt.retryRound > 0 ? `, retry ${attempt.retryRound}` : ""}`,
      attempt.status ?? attempt.operation ?? "provider update",
      `${formatCount(attempt.barsDownloaded)} bars`,
      attempt.error
    ].filter(Boolean).join(" · "),
    tone: attempt.error
      ? "danger" as const
      : attempt.status?.toLowerCase().includes("skip")
        ? "warning" as const
        : "default" as const
  }));
  const overallPercent = clampBackfillProgress(snapshot.overallPercentComplete);
  const active = response.isActive ?? response.active ?? false;
  const observedAt = formatBackfillProgressTimestamp(snapshot.timestamp || response.timestamp);

  return {
    ariaLabel: `Live backfill provider progress. ${snapshot.completedSymbols} of ${snapshot.totalSymbols} symbols complete. ${formatNumber(overallPercent, { maximumFractionDigits: 1 })} percent.`,
    title: active ? "Live provider progress" : "Final provider progress",
    summary: `${snapshot.completedSymbols} of ${snapshot.totalSymbols} symbols complete · ${snapshot.failedSymbols} failed · ${formatPercent(overallPercent, { maximumFractionDigits: 1 })} overall`,
    active,
    overallPercent,
    symbols,
    recentAttempts,
    droppedNotificationWarning: snapshot.droppedProviderNotifications > 0
      ? `${formatCount(snapshot.droppedProviderNotifications)} older provider notification${snapshot.droppedProviderNotifications === 1 ? " was" : "s were"} dropped from the bounded live stream; the latest per-symbol state remains authoritative.`
      : null,
    observedAt
  };
}

function resolveBackfillProviderProgressStatus(
  symbol: BackfillProviderProgressSnapshot["symbols"][string]
): string {
  if (symbol.isFailed) return "Failed";
  if (symbol.isSkipped) return "Skipped";
  if (symbol.isCompleted) return "Completed";
  return symbol.currentStatus ?? symbol.operation ?? "Pending";
}

function clampBackfillProgress(value: number): number {
  return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
}

export function formatBackfillProgressTimestamp(value: string | null | undefined): string {
  if (!value) return "Time unavailable";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? "Time unavailable" : formatTimestamp(parsed);
}

export function formatBackfillValue(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

export function formatBackfillRange(from: string | null, to: string | null): string {
  const fromText = formatBackfillValue(from, "");
  const toText = formatBackfillValue(to, "");

  if (fromText && toText) {
    return `${fromText} to ${toText}`;
  }

  if (fromText) {
    return `From ${fromText}`;
  }

  if (toText) {
    return `Through ${toText}`;
  }

  return "Full available history";
}
