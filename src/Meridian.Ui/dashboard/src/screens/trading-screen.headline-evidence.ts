// The evidence Trading's headline totals state: the account they cover, when they were loaded and
// how far they can be trusted. Kept out of the screen so the rules have one small home.
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import { combineMetricFreshness, withMetricContext } from "@/lib/metric-trust";
import {
  resolveFigureEvidence,
  resolveFigureScope,
  type WorkspaceFigureContextValue
} from "@/lib/workspace-figure-context";
import type { MetricFreshness, MetricSnapshot } from "@/types";

export interface TradingHeadlineEvidenceInput {
  figureContext: WorkspaceFigureContextValue | null;
  /** The operator's selected fund account, as the shell passes it. */
  operatingFundAccountId: string | null | undefined;
  /** The account the screen works with: the selected one, else the payload's own. */
  fundAccountId: string | null | undefined;
  /** The Trading payload's own brokerage account label and heartbeat. */
  brokerageAccount: string | null | undefined;
  lastHeartbeat: string | null | undefined;
  /** What the broker evidence (recovery projection or connection) says about the account. */
  brokerFreshness: MetricFreshness;
}

/**
 * Stamps Trading's headline totals with the evidence of the payload that supplies them.
 *
 * - Scope is the account the payload on screen was loaded for. After the operator selects another
 *   account the previous account's payload stays on screen until the new one loads, and for as
 *   long as that load fails: those figures keep naming the account they belong to, and are stale
 *   because they are not the selected account's.
 * - As-of is that payload's load. A brokerage recovery can report a newer observation before the
 *   next Trading poll reloads the values; its time belongs to the confidence summary, not to
 *   figures it did not supply. Outside the shell the payload's heartbeat is the only time it has.
 * - Freshness is the weakest verdict: the broker evidence can lower the payload's own verdict,
 *   never raise it. A failed Trading reload keeps the previous payload on screen (stale), and a
 *   session whose data source is unconfirmed stays unverified, whatever the broker reports.
 */
export function withTradingHeadlineEvidence<T extends MetricSnapshot>(
  metrics: readonly T[],
  { figureContext, operatingFundAccountId, fundAccountId, brokerageAccount, lastHeartbeat, brokerFreshness }: TradingHeadlineEvidenceInput
): T[] {
  const loadedAccount = resolveFigureScope(figureContext, "trading");
  const selectedAccount = normalizeFundAccountGuid(operatingFundAccountId) ?? null;
  const retainedFromAnotherAccount = loadedAccount !== undefined
    && (loadedAccount?.toLowerCase() ?? null) !== (selectedAccount?.toLowerCase() ?? null);
  const payload = resolveFigureEvidence(figureContext, "trading");

  return withMetricContext(metrics, {
    provenance: {
      source: "Trading workspace",
      asOf: payload.asOf ?? (lastHeartbeat || null),
      scope: (loadedAccount !== undefined ? loadedAccount : fundAccountId) ?? brokerageAccount ?? "All loaded accounts"
    },
    freshness: combineMetricFreshness(payload.freshness, brokerFreshness, retainedFromAnotherAccount ? "stale" : null)
  });
}
