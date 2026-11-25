import { useCallback, useEffect, useState } from "react";
import { getProviderCapabilityMatrix, getProviderDeclaredCapabilities } from "@/lib/api";
import type { DeclaredProviderCapabilities } from "@/lib/api/provider-management.api";
import { formatRelativeTime } from "@/lib/format";
import { describeApiError } from "@/lib/api-errors";
import type {
  ProviderCapabilityMatrixResponse,
  ProviderDiscoveryFailure,
  ProviderFixedIncomeTaskCapability
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
  fixedIncomeTasks: ProviderFixedIncomeTaskCapability[];
  capabilities?: DeclaredProviderCapabilities;
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
export function buildCapabilityMatrixModel(response: ProviderCapabilityMatrixResponse,
  declarations: ReadonlyMap<string, DeclaredProviderCapabilities> = new Map()): CapabilityMatrixModel {
  const rows = response.providers.map((provider) => ({
    providerId: provider.providerId,
    declaredCount: provider.declaredInstrumentTypes.length,
    fixedIncomeTasks: provider.fixedIncomeTasks ?? [],
    capabilities: declarations.get(provider.providerId.toLowerCase()),
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
  enabled = true,
  fetchMatrix: CapabilityMatrixFetcher = getProviderCapabilityMatrix,
  fetchDeclarations = getProviderDeclaredCapabilities
): CapabilityMatrixViewModel {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [model, setModel] = useState<CapabilityMatrixModel | null>(null);

  const refresh = useCallback(async () => {
    if (!enabled) return;
    setLoading(true);
    setError(null);
    try {
      const [response, declarations] = await Promise.all([
        fetchMatrix(),
        Promise.resolve().then(() => fetchDeclarations()).catch(() => null)
      ]);
      const byProvider = new Map<string, DeclaredProviderCapabilities>(
        declarations?.providers.map(row => [row.id.toLowerCase(), row.capabilities] as const)
      );
      setModel(buildCapabilityMatrixModel(response, byProvider));
    } catch (fetchError) {
      setError(describeApiError(fetchError, "Provider capability matrix is unavailable.").summary);
      setModel(null);
    } finally {
      setLoading(false);
    }
  }, [enabled, fetchMatrix, fetchDeclarations]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { loading, error, model, refresh };
}

/** How current a provider's evidence is, for the capability matrix's Freshness column (UX Phase D). */
export interface CapabilityFreshnessModel {
  state: "current" | "stale" | "no-evidence";
  /** Chip text: "Current", "Stale" or "No evidence". */
  label: string;
  /** Age of the newest evidence, e.g. "3h ago", "12d ago"; null when none carries a time. */
  ageText: string | null;
}

export interface CapabilityFreshnessSource {
  providerId: string;
  latestEvidenceAt: string | null;
  evidenceStale: boolean;
}

/** Evidence age through the shared age vocabulary: hours to 48h, days to 60d, then "since <date>". */
export function formatEvidenceAge(ageMs: number, nowMs: number = Date.now()): string {
  if (ageMs < 3_600_000) return "under 1h ago";
  return formatRelativeTime(nowMs - ageMs, nowMs);
}

/**
 * Joins provider evidence onto the capability matrix by provider id (case-insensitive), so the
 * matrix says how fresh each provider's evidence is as well as what it declares. A provider the
 * Data workspace has no connection row for reads "No evidence", the same as one with no timestamp.
 */
export function buildCapabilityFreshnessIndex(
  sources: readonly CapabilityFreshnessSource[],
  nowMs: number = Date.now()
): (providerId: string) => CapabilityFreshnessModel {
  const byId = new Map(sources.map((source) => [source.providerId.trim().toLowerCase(), source]));
  return (providerId) => {
    const source = byId.get(providerId.trim().toLowerCase());
    const time = Date.parse(source?.latestEvidenceAt ?? "");
    if (!source || !Number.isFinite(time)) return { state: "no-evidence", label: "No evidence", ageText: null };
    const ageText = formatEvidenceAge(Math.max(0, nowMs - time), nowMs);
    return source.evidenceStale ? { state: "stale", label: "Stale", ageText } : { state: "current", label: "Current", ageText };
  };
}
