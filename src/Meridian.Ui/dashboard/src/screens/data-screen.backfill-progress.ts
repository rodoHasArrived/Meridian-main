import { formatCount, formatNumber, formatPercent, formatTimestamp } from "@/lib/format";
import type { BackfillExecutionHistoryResponse, BackfillProviderProgressSnapshot, BackfillProgressResponse } from "@/types";
import type {
  BackfillLiveProgressState, BackfillRemediationQueueRow, BackfillRemediationQueueSortState, BackfillRemediationQueueState
} from "./data-screen.view-model";

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

export function buildBackfillRemediationQueueState(
  response: BackfillExecutionHistoryResponse | null,
  sort: BackfillRemediationQueueSortState = { columnId: "tier", direction: "asc" }
): BackfillRemediationQueueState | null {
  if (!response) return null;

  const defaultProvider = formatBackfillValue(response.autoRemediation.defaultProvider, "Not configured");
  const rows = response.executions
    .filter((execution) => execution.autoRemediationSla !== null)
    .map((execution): BackfillRemediationQueueRow => {
      const sla = execution.autoRemediationSla!;
      const dueAt = new Date(sla.dueAtUtc);
      const deadlineSort = Number.isNaN(dueAt.getTime()) ? Number.MAX_SAFE_INTEGER : dueAt.getTime();
      return {
        executionId: execution.executionId,
        symbols: execution.symbols.length > 0 ? execution.symbols.join(", ") : `${execution.symbolsProcessed} symbol${execution.symbolsProcessed === 1 ? "" : "s"}`,
        provider: formatBackfillValue(sla.provider, defaultProvider),
        tier: sla.tier === "SameBusinessDay" ? "Same business day" : "Standard",
        tierSort: sla.tier === "SameBusinessDay" ? 0 : 1,
        status: formatBackfillRemediationStatus(sla.status),
        deadline: Number.isNaN(dueAt.getTime()) ? "Deadline unavailable" : formatTimestamp(dueAt),
        deadlineSort,
        workflow: formatBackfillValue(sla.downstreamWorkflow, "Unassigned"),
        owner: sla.requiresOwnerAssignment ? "Assignment required" : "Not required",
        outcome: formatBackfillValue(execution.autoRemediationLastOutcome, execution.status),
        evidence: [
          formatBackfillValue(sla.reasonCode, "Reason unavailable"),
          sla.triggerSource,
          sla.isCompatibilityDerived ? "Compatibility-derived legacy evidence" : null
        ].filter(Boolean).join(" · ")
      };
    });

  const direction = sort.direction === "asc" ? 1 : -1;
  rows.sort((left, right) => {
    const comparison = sort.columnId === "deadline"
      ? left.deadlineSort - right.deadlineSort
      : left.tierSort - right.tierSort || left.deadlineSort - right.deadlineSort;
    return comparison === 0
      ? left.executionId.localeCompare(right.executionId)
      : comparison * direction;
  });

  return {
    rows,
    summary: `${rows.length} retained remediation execution${rows.length === 1 ? "" : "s"}; ${response.autoRemediation.withReason} carry typed reason evidence.`,
    defaultProvider,
    observedAt: formatBackfillProgressTimestamp(response.timestamp)
  };
}

function formatBackfillRemediationStatus(status: string): string {
  return status
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, (value) => value.toUpperCase());
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

function formatBackfillProgressTimestamp(value: string | null | undefined): string {
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
