import type { StrategyRunRecord } from "@/types";

import { VALUE_STATE_LABELS } from "@/lib/format";
export interface RunLeaderboardRow {
  id: string;
  strategyName: string;
  /** The run's Sharpe as a number, or null when the source reports none. */
  sharpe: number | null;
  sharpeText: string;
  pnlText: string;
  maxDrawdown: number | null;
  trendValues: number[] | null;
  href: string;
}

export interface RunLeaderboardModel {
  ranked: RunLeaderboardRow[];
  /** Runs with no usable Sharpe, listed after the ranking rather than silently dropped. */
  unranked: RunLeaderboardRow[];
}

function parseSharpe(value: string | null | undefined): number | null {
  const trimmed = value?.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Top runs by Sharpe for the Strategy overview (UX Phase D); ties keep the library's order. */
export function buildRunLeaderboard(runs: readonly StrategyRunRecord[], limit = 5): RunLeaderboardModel {
  const rows = runs.map((run): RunLeaderboardRow => {
    const values = run.trendValues?.filter((value) => Number.isFinite(value)) ?? [];
    return {
      id: run.id,
      strategyName: run.strategyName,
      sharpe: parseSharpe(run.sharpe),
      sharpeText: run.sharpe?.trim() || VALUE_STATE_LABELS.unavailable,
      pnlText: run.pnl?.trim() || VALUE_STATE_LABELS.unavailable,
      maxDrawdown: Number.isFinite(run.maxDrawdown) ? run.maxDrawdown ?? null : null,
      trendValues: values.length >= 2 ? values : null,
      href: `/strategy/promotions?runId=${encodeURIComponent(run.id)}`
    };
  });
  const ranked = rows
    .map((row, index) => ({ row, index }))
    .filter((entry) => entry.row.sharpe !== null)
    .sort((a, b) => (b.row.sharpe! - a.row.sharpe!) || (a.index - b.index))
    .slice(0, limit)
    .map((entry) => entry.row);
  return { ranked, unranked: rows.filter((row) => row.sharpe === null) };
}
