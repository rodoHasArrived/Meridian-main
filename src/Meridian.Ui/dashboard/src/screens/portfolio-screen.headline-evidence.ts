// Mark evidence for Portfolio's headline totals. Exposure and unrealized P&L are sums over the
// open positions, so they are only as well evidenced as the marks those positions are valued at.
import { combineMetricFreshness } from "@/lib/metric-trust";
import type { MetricFreshness, MetricSnapshot } from "@/types";

// The totals computed from position marks. The position count does not depend on a mark.
const VALUATION_METRIC_IDS = new Set(["portfolio-total-exposure", "portfolio-unrealized-pnl"]);

/**
 * Lowers the valuation totals to unverified when a position that feeds them has a mark the shared
 * valuation policy does not accept as current (or no assessment at all), and says how many do.
 * A recent, successful load does not make such a total current: the holdings table already warns
 * about those marks, and the totals above it must not look better evidenced than their inputs.
 * It can only lower the payload's verdict (a stale payload stays stale), never raise it.
 */
export function withPortfolioMarkEvidence<T extends MetricSnapshot>(
  metrics: readonly T[],
  positions: readonly { markFreshness: { reviewRequired: boolean } }[],
  payloadFreshness: MetricFreshness
): T[] {
  const needingReview = positions.filter((position) => position.markFreshness.reviewRequired).length;
  if (needingReview === 0) {
    return [...metrics];
  }

  const caption = `${needingReview} of ${positions.length} mark${positions.length === 1 ? "" : "s"} need${needingReview === 1 ? "s" : ""} review`;
  return metrics.map((metric) => VALUATION_METRIC_IDS.has(metric.id)
    ? { ...metric, delta: caption, freshness: combineMetricFreshness(payloadFreshness, "unverified") }
    : metric);
}
