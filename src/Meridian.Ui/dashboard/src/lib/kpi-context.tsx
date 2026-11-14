import { createContext, useContext, useMemo, type ReactNode } from "react";
import { applyKpiScope, kpiReadOwner, resolveKpi, type KpiId, type KpiReadModels, type KpiScope, type ResolvedKpi } from "@/lib/kpi-registry";
import { combineMetricFreshness } from "@/lib/metric-trust";
import { presentMarkFreshness } from "@/lib/mark-freshness";
import { useSharedCrossScreenCounts } from "@/lib/cross-screen-counts-context";
import { resolveFigureEvidence, resolveFigureScope, useWorkspaceFigureContext, type WorkspaceFigureContextValue } from "@/lib/workspace-figure-context";

const KpiContext = createContext<KpiReadModels>({});

/** Nested screen providers support isolated rendering without overriding other owners. */
export function KpiProvider({ models, children }: { models: KpiReadModels; children: ReactNode }) {
  const parent = useContext(KpiContext);
  const sharedCounts = useSharedCrossScreenCounts();
  const merged = useMemo(() => ({
    crossScreenCounts: sharedCounts,
    ...parent,
    ...Object.fromEntries(Object.entries(models).filter(([, value]) => value !== undefined))
  }), [parent, models, sharedCounts]);
  return <KpiContext.Provider value={merged}>{children}</KpiContext.Provider>;
}

export function useKpiModels(): KpiReadModels {
  return useContext(KpiContext);
}

export function useKpi(id: KpiId, scope?: KpiScope) {
  return useKpis([id], scope)[0];
}

const ownerLabels = {
  portfolio: "Portfolio workspace", portfolioExplorer: "Retained portfolio records", trading: "Trading workspace",
  overview: "System overview", strategy: "Strategy workspace", data: "Data workspace", accounting: "Accounting workspace", crossScreenCounts: "Cross-screen counts"
} as const;

function withOwnerEvidence(metric: ResolvedKpi, models: KpiReadModels, workspace: WorkspaceFigureContextValue | null): ResolvedKpi {
  const owner = kpiReadOwner(metric.id, models);
  const retained = owner === "portfolioExplorer";
  const payload = retained ? { asOf: null, freshness: workspace?.freshness ?? null } : resolveFigureEvidence(workspace, owner);
  const dimensions = JSON.parse(metric.scope) as Record<string, string | null>;
  const loadedAccount = retained || owner === "portfolio" ? undefined : resolveFigureScope(workspace, owner);
  if (loadedAccount !== undefined) dimensions.account = loadedAccount ?? "all";
  const asOf = metric.provenance?.asOf ?? metric.asOf ?? (retained ? dimensions.asOf : payload.asOf);
  const source = metric.provenance?.source ?? metric.source ?? ownerLabels[owner];
  const qualifier = workspace?.dataLabel;
  const provenance = {
    ...metric.provenance,
    source: qualifier && !source.endsWith(`(${qualifier})`) ? `${source} (${qualifier})` : source,
    asOf,
    scope: metric.provenance?.scope ?? (retained ? dimensions.run ? `Run ${dimensions.run}` : "Retained source scope" : dimensions.account === "all" ? "All loaded accounts" : dimensions.account)
  };
  const markReviewRequired = (metric.id === "portfolio.total-exposure" || metric.id === "portfolio.unrealized-pnl")
    && Array.isArray(models.portfolio?.positions)
    && models.portfolio.positions.some((position) => presentMarkFreshness(position.markFreshness).reviewRequired);
  return {
    ...metric,
    provenance,
    freshness: combineMetricFreshness(metric.freshness, payload.freshness, metric.status === "unavailable" || markReviewRequired ? "unverified" : null),
    scope: JSON.stringify(Object.fromEntries(Object.entries(dimensions).sort(([a], [b]) => a.localeCompare(b))))
  };
}

/** Resolve each figure with the evidence of its own payload, even in a mixed-source strip. */
export function useKpis(ids: readonly KpiId[], scope?: KpiScope): ResolvedKpi[] {
  const models = useKpiModels();
  const workspace = useWorkspaceFigureContext();
  return ids.map((id) => applyKpiScope(withOwnerEvidence(resolveKpi(id, models), models, workspace), scope));
}
