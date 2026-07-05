import { useCallback, useEffect, useState } from "react";
import { getSecurityMasterCoverageDraft, getSecurityMasterQualityReport } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type { SecurityMasterDraftProposal, SecurityMasterQualityReport } from "@/types";

export type CoverageReportFetcher = typeof getSecurityMasterQualityReport;
export type CoverageDraftFetcher = typeof getSecurityMasterCoverageDraft;

export interface CoverageGapRowModel {
  symbol: string;
  /** The surfaces that reported the symbol active, parsed from the RC001 message. */
  sourcesLabel: string;
  message: string;
}

export interface CoverageGapsModel {
  runAtLabel: string;
  rows: CoverageGapRowModel[];
  summary: string;
}

const COVERAGE_RULE_ID = "RC001";
const SYMBOL_FIELD_PREFIX = "symbol.";

function parseSources(message: string): string {
  const start = message.indexOf("active in ");
  const end = message.indexOf(" but has", start);
  return start >= 0 && end > start ? message.slice(start + "active in ".length, end) : "unknown source";
}

/**
 * Pure projection of the security-master quality report onto the coverage-gap queue: RC001
 * violations only, one row per unmastered symbol with the surfaces that flagged it.
 */
export function buildCoverageGapsModel(report: SecurityMasterQualityReport): CoverageGapsModel {
  const rows = report.violations
    .filter((violation) => violation.ruleId === COVERAGE_RULE_ID)
    .map((violation) => ({
      symbol: violation.fieldPath?.startsWith(SYMBOL_FIELD_PREFIX)
        ? violation.fieldPath.slice(SYMBOL_FIELD_PREFIX.length)
        : (violation.fieldPath ?? "?"),
      sourcesLabel: parseSources(violation.message),
      message: violation.message
    }))
    .sort((a, b) => a.symbol.localeCompare(b.symbol));

  return {
    runAtLabel: new Date(report.runAt).toLocaleString(),
    rows,
    summary:
      rows.length === 0
        ? "Every active symbol has a security-master record."
        : `${rows.length} active symbol${rows.length === 1 ? "" : "s"} without a security-master record.`
  };
}

export interface CoverageGapsViewModel {
  loading: boolean;
  error: string | null;
  model: CoverageGapsModel | null;
  refresh: () => Promise<void>;
  /** Drafts keyed by symbol once requested via requestDraft. */
  drafts: Record<string, SecurityMasterDraftProposal>;
  draftErrors: Record<string, string>;
  draftingSymbol: string | null;
  requestDraft: (symbol: string) => Promise<void>;
}

/**
 * View-model for the coverage-gaps panel: RC001 rows from the latest security-master quality
 * report, plus the machine-proposed draft flow behind each row's "Master this" action.
 */
export function useCoverageGapsPanel(
  fetchReport: CoverageReportFetcher = getSecurityMasterQualityReport,
  fetchDraft: CoverageDraftFetcher = getSecurityMasterCoverageDraft
): CoverageGapsViewModel {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [model, setModel] = useState<CoverageGapsModel | null>(null);
  const [drafts, setDrafts] = useState<Record<string, SecurityMasterDraftProposal>>({});
  const [draftErrors, setDraftErrors] = useState<Record<string, string>>({});
  const [draftingSymbol, setDraftingSymbol] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const report = await fetchReport();
      setModel(buildCoverageGapsModel(report));
    } catch (fetchError) {
      setError(describeApiError(fetchError, "Security-master coverage report is unavailable.").summary);
      setModel(null);
    } finally {
      setLoading(false);
    }
  }, [fetchReport]);

  const requestDraft = useCallback(
    async (symbol: string) => {
      setDraftingSymbol(symbol);
      setDraftErrors((current) => ({ ...current, [symbol]: "" }));
      try {
        const draft = await fetchDraft(symbol);
        setDrafts((current) => ({ ...current, [symbol]: draft }));
      } catch (fetchError) {
        setDraftErrors((current) => ({
          ...current,
          [symbol]: describeApiError(fetchError, "Draft could not be built.").summary
        }));
      } finally {
        setDraftingSymbol(null);
      }
    },
    [fetchDraft]
  );

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { loading, error, model, refresh, drafts, draftErrors, draftingSymbol, requestDraft };
}
