import { useCallback, useEffect, useState } from "react";
import { getCorporateActionInbox } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type { CorporateActionInboxResponse } from "@/types";

export type CorporateActionInboxFetcher = typeof getCorporateActionInbox;

export type InboxRowTone = "warning" | "neutral";

export interface CorporateActionInboxRowModel {
  key: string;
  ticker: string;
  actionType: string;
  exDateLabel: string;
  /** Days until the ex-date; negative when it already passed. */
  daysUntilEx: number;
  countdownLabel: string;
  valueLabel: string;
  /** e.g. "2/3 sources agree" — dissent is what the operator reviews. */
  consensusLabel: string;
  dissentingSources: string[];
  tone: InboxRowTone;
}

export interface CorporateActionInboxModel {
  lastIngestLabel: string;
  stagedCount: number;
  appliedLastRun: number;
  duplicatesSkippedLastRun: number;
  rows: CorporateActionInboxRowModel[];
  errors: string[];
  summary: string;
}

function formatValue(row: {
  amount: number | null;
  currency: string | null;
  splitFromFactor: number | null;
  splitToFactor: number | null;
}): string {
  if (row.splitFromFactor != null && row.splitToFactor != null) {
    return `${row.splitToFactor}:${row.splitFromFactor} split`;
  }
  if (row.amount != null) {
    return `${row.amount} ${row.currency ?? ""}`.trim();
  }
  return "—";
}

/**
 * Pure projection of the corporate-action inbox snapshot into presentation state, sorted by
 * ex-date urgency. Rows with dissenting sources carry a warning tone: the operator's job is
 * exactly to adjudicate the disagreement.
 */
export function buildCorporateActionInboxModel(
  response: CorporateActionInboxResponse,
  today: Date = new Date()
): CorporateActionInboxModel {
  const todayUtc = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());

  const rows = response.staged.map((proposal) => {
    const exDate = new Date(`${proposal.exDate}T00:00:00Z`);
    const daysUntilEx = Math.round((exDate.getTime() - todayUtc) / 86_400_000);
    const totalSources = proposal.agreeingSources.length + proposal.dissentingSources.length;
    return {
      key: `${proposal.securityId}:${proposal.actionType}:${proposal.exDate}`,
      ticker: proposal.ticker,
      actionType: proposal.actionType,
      exDateLabel: proposal.exDate,
      daysUntilEx,
      countdownLabel:
        daysUntilEx > 0
          ? `in ${daysUntilEx} day${daysUntilEx === 1 ? "" : "s"}`
          : daysUntilEx === 0
            ? "today"
            : `${-daysUntilEx} day${daysUntilEx === -1 ? "" : "s"} ago`,
      valueLabel: formatValue(proposal),
      consensusLabel: `${proposal.agreeingSources.length}/${totalSources} source${totalSources === 1 ? "" : "s"} agree`,
      dissentingSources: proposal.dissentingSources,
      tone: (proposal.dissentingSources.length > 0 ? "warning" : "neutral") as InboxRowTone
    };
  });
  rows.sort((a, b) => a.daysUntilEx - b.daysUntilEx);

  return {
    lastIngestLabel: response.lastIngestAt ? new Date(response.lastIngestAt).toLocaleString() : "never",
    stagedCount: response.stagedCount,
    appliedLastRun: response.appliedLastRun,
    duplicatesSkippedLastRun: response.duplicatesSkippedLastRun,
    rows,
    errors: response.errors,
    summary:
      response.stagedCount === 0
        ? "No staged corporate actions awaiting review."
        : `${response.stagedCount} staged proposal${response.stagedCount === 1 ? "" : "s"} awaiting review; ` +
          `${response.appliedLastRun} auto-applied last run.`
  };
}

export interface CorporateActionInboxViewModel {
  loading: boolean;
  error: string | null;
  model: CorporateActionInboxModel | null;
  refresh: () => Promise<void>;
}

/**
 * View-model for the corporate-action inbox panel, backed by
 * `/api/security-master/corporate-actions/inbox`. Fetches on mount; refresh on demand.
 */
export function useCorporateActionInboxPanel(
  fetchInbox: CorporateActionInboxFetcher = getCorporateActionInbox
): CorporateActionInboxViewModel {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [model, setModel] = useState<CorporateActionInboxModel | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetchInbox();
      setModel(buildCorporateActionInboxModel(response));
    } catch (fetchError) {
      setError(describeApiError(fetchError, "Corporate-action inbox is unavailable.").summary);
      setModel(null);
    } finally {
      setLoading(false);
    }
  }, [fetchInbox]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { loading, error, model, refresh };
}
