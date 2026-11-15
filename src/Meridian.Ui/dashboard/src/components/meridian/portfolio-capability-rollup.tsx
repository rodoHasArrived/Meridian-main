import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { getInstrumentCapabilities } from "@/lib/api/instrument-capabilities.api";
import type { BrokerageHouseholdPortfolio, InstrumentCapabilities, InstrumentCapabilityBatch } from "@/types";

interface ObservedHolding { key: string; securityId: string | null; symbol: string; accountId: string }
interface RollupState { key: string; results: InstrumentCapabilities[]; loading: boolean; failed: boolean }

export function buildObservedAccrualRollup(holdings: ObservedHolding[], assessments: InstrumentCapabilities[]) {
  const bySecurity = new Map(assessments.map((item) => [item.securityId.toLowerCase(), item]));
  const blocked = holdings.flatMap((holding) => {
    const accrual = holding.securityId ? bySecurity.get(holding.securityId.toLowerCase())?.capabilities.find((item) => item.capability === "accrual") : null;
    return accrual?.status === "Blocked" && accrual.applicability === "Applicable" ? [{ ...holding, reasons: accrual.reasons }] : [];
  });
  const reasons = new Map<string, { code: string; reason: string; count: number; remediationPath: string }>();
  for (const holding of blocked) {
    for (const item of new Map(holding.reasons.map((reason) => [reason.code, reason])).values()) {
      const previous = reasons.get(item.code);
      const reason = item.code === "RateUnitUndeclared" ? "A required rate unit is undeclared."
        : item.code === "RecurringAccrualClassUnsupported" ? "The class has no registered recurring accrual profile."
        : item.reason;
      reasons.set(item.code, { ...item, reason, count: (previous?.count ?? 0) + 1 });
    }
  }
  return { blocked, reasons: [...reasons.values()], review: holdings.length - blocked.length, unresolved: holdings.filter((item) => !item.securityId).length };
}

export function PortfolioCapabilityRollup({ portfolio, accountId, scopeLabel, service = getInstrumentCapabilities }: {
  portfolio: BrokerageHouseholdPortfolio | null | undefined;
  accountId: string;
  scopeLabel: string;
  service?: (securityIds: string[], signal?: AbortSignal) => Promise<InstrumentCapabilityBatch>;
}) {
  const holdings = useMemo(() => {
    const unique = new Map<string, ObservedHolding>();
    for (const position of portfolio?.positions ?? []) {
      if (position.quantity === 0 || accountId !== "all" && position.fundAccountId !== accountId) continue;
      const securityId = position.security?.securityId?.trim() || null;
      const key = `${position.fundAccountId}|${securityId?.toLowerCase() ?? `unresolved:${position.positionId ?? position.symbol}`}`;
      unique.set(key, { key, securityId, symbol: position.symbol, accountId: position.fundAccountId });
    }
    return [...unique.values()];
  }, [portfolio, accountId]);
  const ids = [...new Set(holdings.flatMap((holding) => holding.securityId ? [holding.securityId.toLowerCase()] : []))].sort();
  const requestKey = `${portfolio?.asOf ?? ""}|${accountId}|${ids.join(",")}`;
  const [state, setState] = useState<RollupState>({ key: "", results: [], loading: false, failed: false });

  useEffect(() => {
    const controller = new AbortController();
    const securities = requestKey.split("|")[2]?.split(",").filter(Boolean) ?? [];
    const batches: string[][] = [];
    for (let offset = 0; offset < securities.length; offset += 200) batches.push(securities.slice(offset, offset + 200));
    setState({ key: requestKey, results: [], loading: batches.length > 0, failed: false });
    let next = 0;
    const worker = async () => {
      while (next < batches.length && !controller.signal.aborted) {
        const selected = batches[next++];
        try {
          const response = await service(selected, controller.signal);
          if (controller.signal.aborted) return;
          const allowed = new Set(selected);
          const seen = new Set<string>();
          const results = response.securities.filter((item) => {
            const id = item.securityId.toLowerCase();
            if (!allowed.has(id) || seen.has(id)) return false;
            seen.add(id);
            return true;
          });
          setState((current) => current.key === requestKey ? { ...current, results: [...current.results, ...results], failed: current.failed || seen.size !== selected.length } : current);
        } catch {
          if (!controller.signal.aborted) setState((current) => current.key === requestKey ? { ...current, failed: true } : current);
        }
      }
    };
    void Promise.all([worker(), worker()]).then(() => {
      if (!controller.signal.aborted) setState((current) => current.key === requestKey ? { ...current, loading: false } : current);
    });
    return () => controller.abort();
  }, [requestKey, service]);

  const current = state.key === requestKey ? state : { results: [], loading: ids.length > 0, failed: false };
  const rollup = buildObservedAccrualRollup(holdings, current.results);
  return (
    <section aria-label="Observed holdings accrual eligibility" className="space-y-3 rounded border border-border p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold">Recurring accrual eligibility</h3>
          <p className="text-xs text-muted-foreground">Loaded brokerage observations · {scopeLabel} · observed {portfolio?.asOf ?? "date unavailable"}</p>
        </div>
        <Badge variant="outline">{portfolio ? `${holdings.length} observed holding${holdings.length === 1 ? "" : "s"}` : "Snapshot unavailable"}</Badge>
      </div>
      {portfolio ? <p role="status" className="text-sm">{current.loading ? "Checking current security definitions… " : ""}{rollup.blocked.length} holding{rollup.blocked.length === 1 ? " has" : "s have"} known accrual blockers; {rollup.review} need book and lot review.</p>
        : <p role="status" className="text-sm">No loaded brokerage snapshot is available; holding eligibility has not been evaluated.</p>}
      <p className="text-xs text-muted-foreground">Recurring Security Master fixed-income accrual, using current definitions for distinct account/security pairs in the dated observations. Loan servicing has its own workflow. Reasons may overlap; approval, dated lots and book-period checks remain required.</p>
      {rollup.unresolved > 0 ? <p className="text-xs text-muted-foreground">{rollup.unresolved} holding{rollup.unresolved === 1 ? " has an unresolved Security Master identity" : "s have unresolved Security Master identities"}.</p> : null}
      {current.failed ? <p role="status" className="text-xs text-warning">Some definitions could not be evaluated. Those holdings remain under review.</p> : null}
      {rollup.reasons.length > 0 ? <ul className="space-y-1 text-xs">{rollup.reasons.map((reason) => (
        <li key={reason.code}>{reason.count}: {reason.reason} {reason.remediationPath.startsWith("/") && !reason.remediationPath.startsWith("//") ? <Link to={reason.remediationPath} className="text-primary underline">Review evidence</Link> : null}</li>
      ))}</ul> : null}
      {rollup.blocked.length > 0 ? <details><summary className="cursor-pointer text-xs font-medium">Inspect blocked holdings</summary><ul className="mt-2 space-y-1 text-xs">{rollup.blocked.map((holding) => (
        <li key={holding.key}><Link className="text-primary underline" to={`/accounting/security-master?securityId=${encodeURIComponent(holding.securityId!)}`}>{holding.symbol}</Link> · account {holding.accountId}</li>
      ))}</ul></details> : null}
    </section>
  );
}
