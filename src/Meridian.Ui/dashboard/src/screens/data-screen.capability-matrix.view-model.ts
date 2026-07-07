import { useCallback, useEffect, useState } from "react";
import { getProviderCapabilityMatrix } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type {
  ProviderCapabilityMatrixResponse,
  ProviderDiscoveryFailure
} from "@/types";

export type CapabilityMatrixFetcher = typeof getProviderCapabilityMatrix;

export interface CapabilityMatrixCellModel {
  instrumentType: string;
  supported: boolean;
  /** Compact capability marks for the cell, e.g. "S·B·C"; "—" when unsupported. */
  marks: string;
  /** Full-text description for tooltips and screen readers. */
  description: string;
}

export interface CapabilityMatrixRowModel {
  providerId: string;
  declaredCount: number;
  cells: CapabilityMatrixCellModel[];
}

export interface CapabilityMatrixModel {
  columns: string[];
  rows: CapabilityMatrixRowModel[];
  failures: ProviderDiscoveryFailure[];
  summary: string;
}

interface CapabilityMark {
  flag: "stream" | "backfill" | "corporateActions" | "symbolSearch" | "optionsChain";
  code: string;
  label: string;
}

/** Legend order is meaningful: supply-side capabilities first, derived data last. */
export const CAPABILITY_LEGEND: readonly CapabilityMark[] = [
  { flag: "stream", code: "S", label: "Streaming" },
  { flag: "backfill", code: "B", label: "Backfill" },
  { flag: "symbolSearch", code: "Q", label: "Symbol search" },
  { flag: "corporateActions", code: "C", label: "Corporate actions" },
  { flag: "optionsChain", code: "O", label: "Options chain" }
] as const;

/**
 * Pure projection of the capability-matrix read-model into presentation state: one row per
 * provider with compact per-instrument capability marks, plus the discovery failures that
 * explain why a provider is missing or gray.
 */
export function buildCapabilityMatrixModel(response: ProviderCapabilityMatrixResponse): CapabilityMatrixModel {
  const rows = response.providers.map((provider) => ({
    providerId: provider.providerId,
    declaredCount: provider.declaredInstrumentTypes.length,
    cells: provider.cells.map((cell) => {
      const active = CAPABILITY_LEGEND.filter((mark) => cell[mark.flag]);
      return {
        instrumentType: cell.instrumentType,
        supported: cell.supported,
        marks: cell.supported && active.length > 0 ? active.map((mark) => mark.code).join("·") : "—",
        description: cell.supported
          ? active.length > 0
            ? `${provider.providerId} supports ${cell.instrumentType}: ${active.map((mark) => mark.label).join(", ")}`
            : `${provider.providerId} declares ${cell.instrumentType} but exposes no capability surface for it`
          : `${provider.providerId} does not declare ${cell.instrumentType}`
      };
    })
  }));

  const declaredCells = rows.reduce(
    (count, row) => count + row.cells.filter((cell) => cell.supported).length,
    0
  );

  return {
    columns: response.instrumentTypes,
    rows,
    failures: response.discoveryFailures,
    summary:
      `${rows.length} providers × ${response.instrumentTypes.length} instrument types, ` +
      `${declaredCells} declared coverage cells` +
      (response.discoveryFailures.length > 0
        ? `, ${response.discoveryFailures.length} discovery failure${response.discoveryFailures.length === 1 ? "" : "s"}.`
        : ".")
  };
}

export interface CapabilityMatrixViewModel {
  loading: boolean;
  error: string | null;
  model: CapabilityMatrixModel | null;
  refresh: () => Promise<void>;
}

/**
 * View-model for the Data workspace capability-matrix panel, backed by
 * `/api/providers/capability-matrix`. Fetches on mount; consumers refresh on demand.
 */
export function useCapabilityMatrixPanel(
  fetchMatrix: CapabilityMatrixFetcher = getProviderCapabilityMatrix
): CapabilityMatrixViewModel {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [model, setModel] = useState<CapabilityMatrixModel | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetchMatrix();
      setModel(buildCapabilityMatrixModel(response));
    } catch (fetchError) {
      setError(describeApiError(fetchError, "Provider capability matrix is unavailable.").summary);
      setModel(null);
    } finally {
      setLoading(false);
    }
  }, [fetchMatrix]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { loading, error, model, refresh };
}
