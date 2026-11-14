import { createContext, useContext, useMemo, type ReactNode } from "react";
import { resolveKpi, type KpiId, type KpiReadModels, type KpiScope } from "@/lib/kpi-registry";

const KpiContext = createContext<KpiReadModels>({});

/** Nested screen providers support isolated rendering without overriding other owners. */
export function KpiProvider({ models, children }: { models: KpiReadModels; children: ReactNode }) {
  const parent = useContext(KpiContext);
  const merged = useMemo(() => ({
    ...parent,
    ...Object.fromEntries(Object.entries(models).filter(([, value]) => value !== undefined))
  }), [parent, models]);
  return <KpiContext.Provider value={merged}>{children}</KpiContext.Provider>;
}

export function useKpiModels(): KpiReadModels {
  return useContext(KpiContext);
}

export function useKpi(id: KpiId, scope?: KpiScope) {
  return resolveKpi(id, useKpiModels(), scope);
}
