// The one selector for every count more than one screen shows (A3): open orders, provider health
// and open reconciliation breaks. Trading, Data, Settings, Accounting, the trust strip and the
// operator readiness console all read these, and the definitions mirror the server's
// CrossScreenCountDefinitions (Meridian.Ui.Shared), held together by the shared fixture
// tests/fixtures/cross-screen-counts/cross-screen-counts.json.
//
// A screen that has its own source list loaded (the blotter's open orders, the Data provider rows,
// the accounting break queue) counts that list with these definitions; a screen without it (Settings,
// the shell) reads the server's cross-screen counts, which are counted the same way over the same
// sources. Either way the number is the same, and a count that cannot be read is null, never a
// substitute figure.
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";

export const OPEN_ORDERS_DEFINITION =
  "Orders the order manager reports open (working, partially filled or pending routing) for the fund account in scope.";
export const PROVIDERS_DEFINITION = "Providers whose status is Healthy, out of every provider the Data workspace lists.";
export const OPEN_BREAKS_DEFINITION = "Break-queue items not yet resolved, dismissed or signed off.";

const CLOSED_BREAK_STATUSES = new Set(["resolved", "dismissed", "signedoff", "closed"]);

export interface ProviderHealthCount {
  healthy: number;
  total: number;
}

/** Every order in the order manager's open list counts, whatever its working state. */
export function countOpenOrders(openOrders: readonly unknown[] | null | undefined): number | null {
  return openOrders ? openOrders.length : null;
}

export function isHealthyProviderStatus(status: string | null | undefined): boolean {
  return (status ?? "").trim().toLowerCase() === "healthy";
}

/** Healthy providers out of every provider row listed. */
export function countProviderHealth(
  providers: readonly { status?: string | null }[] | null | undefined
): ProviderHealthCount | null {
  if (!providers) {
    return null;
  }
  return {
    healthy: providers.filter((provider) => isHealthyProviderStatus(provider.status)).length,
    total: providers.length
  };
}

/**
 * A break is open until resolved, dismissed or signed off. Written as the exclusion so a status
 * added later counts as work still to do rather than being dropped.
 */
export function isOpenBreakStatus(status: string | null | undefined): boolean {
  return !CLOSED_BREAK_STATUSES.has((status ?? "").replace(/[\s_-]/g, "").toLowerCase());
}

/** Open breaks in a break-queue read, each item counted once. */
export function countOpenBreaks(items: readonly { status?: string | null }[] | null | undefined): number | null {
  return items ? items.filter((item) => isOpenBreakStatus(item.status)).length : null;
}

export interface CrossScreenCountSources {
  /** The server's cross-screen counts, loaded by the shell for every route. */
  shared?: WorkstationCrossScreenCounts | null;
  /** The trading blotter's open-order list, when the trading workspace is loaded. */
  openOrders?: readonly unknown[] | null;
  /** The Data workspace's provider rows, when loaded. */
  providers?: readonly { status?: string | null }[] | null;
  /** The accounting break queue, when loaded. */
  breakQueue?: readonly { status?: string | null }[] | null;
}

export interface CrossScreenCountValues {
  openOrders: number | null;
  providers: ProviderHealthCount | null;
  openBreaks: number | null;
}

/**
 * The value every screen shows for each cross-screen count: the screen's own source list counted
 * with the shared definition when it has one, otherwise the server's count, otherwise null.
 */
export function selectCrossScreenCounts(sources: CrossScreenCountSources): CrossScreenCountValues {
  const shared = sources.shared ?? null;
  return {
    openOrders: countOpenOrders(sources.openOrders) ?? shared?.openOrders?.count ?? null,
    providers: countProviderHealth(sources.providers)
      ?? (shared?.providers ? { healthy: shared.providers.healthy, total: shared.providers.total } : null),
    openBreaks: countOpenBreaks(sources.breakQueue) ?? shared?.openBreaks?.count ?? null
  };
}

/** "2 of 4 healthy", or "—" when the count cannot be read. */
export function formatProviderHealth(count: ProviderHealthCount | null): string {
  return count ? `${count.healthy} of ${count.total} healthy` : "—";
}

/** A count as text, or "—" when it cannot be read. */
export function formatCrossScreenCount(count: number | null): string {
  return count === null ? "—" : String(count);
}

// Workspace headline cards that show a cross-screen count, by the ids the server and fixtures use.
const OPEN_ORDER_METRIC_IDS = new Set(["orders", "open-orders", "trading-open-orders"]);
const PROVIDER_METRIC_IDS = new Set(["providers", "providers-healthy"]);
const OPEN_BREAK_METRIC_IDS = new Set(["open-breaks", "breaks"]);

/**
 * Headline cards that show a cross-screen count take their value from the one selector instead of
 * the payload's own card string, which was counted separately and could disagree with the list
 * beside it. Their delta is dropped: it had no stated basis. Other cards pass through unchanged.
 */
/**
 * Sets the open-breaks card's delta (C3). The delta must state its basis, e.g. "2 new since your
 * last visit"; an empty delta leaves the card without one.
 */
export function applyOpenBreakDelta<T extends { id: string; delta: string }>(metrics: readonly T[], delta: string): T[] {
  return metrics.map((metric) => OPEN_BREAK_METRIC_IDS.has(metric.id) ? { ...metric, delta } : metric);
}

export function applyCrossScreenCounts<T extends { id: string; value: string; delta: string; tone: string }>(
  metrics: readonly T[],
  values: CrossScreenCountValues
): T[] {
  return metrics.map((metric) => {
    if (OPEN_ORDER_METRIC_IDS.has(metric.id)) {
      return { ...metric, value: formatCrossScreenCount(values.openOrders), delta: "" };
    }
    if (PROVIDER_METRIC_IDS.has(metric.id)) {
      const providers = values.providers;
      return {
        ...metric,
        value: providers ? `${providers.healthy}/${providers.total}` : "—",
        delta: "",
        tone: !providers ? "default" : providers.total > 0 && providers.healthy === providers.total ? "success" : "warning"
      };
    }
    if (OPEN_BREAK_METRIC_IDS.has(metric.id)) {
      return {
        ...metric,
        value: formatCrossScreenCount(values.openBreaks),
        delta: "",
        tone: values.openBreaks === null ? "default" : values.openBreaks === 0 ? "success" : "warning"
      };
    }
    return metric;
  });
}
