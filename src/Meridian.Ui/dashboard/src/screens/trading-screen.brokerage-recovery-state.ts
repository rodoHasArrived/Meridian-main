import { useEffect, useRef, type Dispatch, type MutableRefObject, type SetStateAction } from "react";
import type { TradingOperatorReadiness } from "@/types";

/** Observe account recovery without letting a failed read renew portfolio freshness. */
export function useBrokerageRecoveryObservation({ fundAccountId, readiness, setReadiness, refreshRef }: {
  fundAccountId?: string;
  readiness: TradingOperatorReadiness | null;
  setReadiness: Dispatch<SetStateAction<TradingOperatorReadiness | null>>;
  refreshRef: MutableRefObject<() => Promise<void>>;
}) {
  const portfolioExpiryRef = useRef<{ snapshotKey: string; deadline: number } | null>(null);
  useEffect(() => {
    if (!fundAccountId) return;
    const poll = window.setInterval(() => { void refreshRef.current(); }, 10_000);
    return () => window.clearInterval(poll);
  }, [fundAccountId, refreshRef]);

  const recoveryAccountId = readiness?.brokerageRecovery?.fundAccountId;
  const portfolioExpiresAt = readiness?.brokerageRecovery?.portfolio?.expiresAt;
  const portfolioIsFresh = readiness?.brokerageRecovery?.portfolio?.isFresh;
  const readinessAsOf = readiness?.asOf;
  useEffect(() => {
    if (!fundAccountId || recoveryAccountId?.toLowerCase() !== fundAccountId.toLowerCase() || !portfolioIsFresh) return;
    // Server-relative lifetime tolerates a browser clock ahead of or behind the host.
    // Repeated delivery of the same snapshot must not renew its local lifetime.
    const snapshotKey = `${fundAccountId}:${readinessAsOf}:${portfolioExpiresAt}`;
    if (portfolioExpiryRef.current?.snapshotKey !== snapshotKey) {
      const lifetime = Date.parse(portfolioExpiresAt ?? "") - Date.parse(readinessAsOf ?? "");
      portfolioExpiryRef.current = { snapshotKey, deadline: Date.now() + (Number.isFinite(lifetime) ? Math.max(0, lifetime) : 0) };
    }
    const expire = () => setReadiness((current) => {
      const recovery = current?.brokerageRecovery;
      if (!current || recovery?.fundAccountId !== recoveryAccountId || !recovery?.portfolio?.isFresh
        || current.asOf !== readinessAsOf || recovery.portfolio.expiresAt !== portfolioExpiresAt) return current;
      const reason = "Brokerage portfolio evidence expired. Synchronize and reconcile before trading.";
      return {
        ...current,
        overallStatus: "Blocked",
        readyForLiveOperation: false,
        liveOperationBlockers: [...new Set([...(current.liveOperationBlockers ?? []), "brokeragePortfolio:Expired"])],
        brokerageRecovery: {
          ...recovery,
          status: "Blocked",
          detail: reason,
          blockingReasons: [...new Set([...recovery.blockingReasons, reason])],
          portfolio: { ...recovery.portfolio, isFresh: false }
        }
      };
    });
    const delay = Math.max(0, portfolioExpiryRef.current.deadline - Date.now());
    if (delay === 0) { expire(); return; }
    const timer = window.setTimeout(expire, Math.min(delay, 2_147_483_647));
    return () => window.clearTimeout(timer);
  }, [fundAccountId, recoveryAccountId, readinessAsOf, portfolioExpiresAt, portfolioIsFresh, setReadiness]);

}
