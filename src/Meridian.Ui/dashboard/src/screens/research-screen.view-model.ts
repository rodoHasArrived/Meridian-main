import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  PromotionRecord,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  RunComparisonRow,
  RunDiff
} from "@/types";

export type ResearchCommand = "compare" | "diff" | "history";

export interface ResearchRunLibraryServices {
  compareRuns: (runIds: string[]) => Promise<RunComparisonRow[]>;
  diffRuns: (baseRunId: string, targetRunId: string) => Promise<RunDiff>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface ResearchRunLibraryState {
  runs: ResearchRunRecord[];
  selectedIds: string[];
  selectedRuns: ResearchRunRecord[];
  selectedRun: ResearchRunRecord | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  canCompare: boolean;
  canDiff: boolean;
  canLoadPromotionHistory: boolean;
  selectionText: string;
  selectionDetail: string;
  compareButtonLabel: string;
  diffButtonLabel: string;
  promotionHistoryButtonLabel: string;
  statusAnnouncement: string;
}

const defaultResearchServices: ResearchRunLibraryServices = {
  compareRuns: (runIds) => workstationApi.compareRuns(runIds),
  diffRuns: (baseRunId, targetRunId) => workstationApi.diffRuns(baseRunId, targetRunId),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function useResearchRunLibraryViewModel(
  data: ResearchWorkspaceResponse | null,
  services: ResearchRunLibraryServices = defaultResearchServices
) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [comparison, setComparison] = useState<RunComparisonRow[]>([]);
  const [runDiff, setRunDiff] = useState<RunDiff | null>(null);
  const [promotionHistory, setPromotionHistory] = useState<PromotionRecord[]>([]);
  const [activeCommand, setActiveCommand] = useState<ResearchCommand | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const runs = data?.runs ?? [];

  const state = useMemo(
    () => buildResearchRunLibraryState({
      runs,
      selectedIds,
      selectedRun,
      comparison,
      runDiff,
      promotionHistory,
      activeCommand,
      actionError
    }),
    [actionError, activeCommand, comparison, promotionHistory, runDiff, runs, selectedIds, selectedRun]
  );

  const toggleRun = useCallback((runId: string) => {
    setSelectedIds((current) => toggleRunSelection(current, runId));
    setActionError(null);
  }, []);

  const openRunDetail = useCallback((run: ResearchRunRecord) => {
    setSelectedRun(run);
  }, []);

  const closeRunDetail = useCallback(() => {
    setSelectedRun(null);
  }, []);

  const loadPromotionHistory = useCallback(async () => {
    setActiveCommand("history");
    setActionError(null);

    try {
      const rows = await services.getPromotionHistory();
      setPromotionHistory(rows);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Promotion history failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [services]);

  const compareSelectedRuns = useCallback(async () => {
    if (selectedIds.length !== 2) {
      setActionError("Select exactly two runs before comparing.");
      return;
    }

    setActiveCommand("compare");
    setActionError(null);

    try {
      const rows = await services.compareRuns(selectedIds);
      setComparison(rows);
      setRunDiff(null);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Run comparison failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [selectedIds, services]);

  const diffSelectedRuns = useCallback(async () => {
    if (selectedIds.length !== 2) {
      setActionError("Select exactly two runs before diffing.");
      return;
    }

    setActiveCommand("diff");
    setActionError(null);

    try {
      const result = await services.diffRuns(selectedIds[0], selectedIds[1]);
      setRunDiff(result);
      setComparison([]);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Run diff failed.");
    } finally {
      setActiveCommand(null);
    }
  }, [selectedIds, services]);

  return {
    ...state,
    toggleRun,
    openRunDetail,
    closeRunDetail,
    loadPromotionHistory,
    compareSelectedRuns,
    diffSelectedRuns
  };
}

export function buildResearchRunLibraryState({
  runs,
  selectedIds,
  selectedRun,
  comparison,
  runDiff,
  promotionHistory,
  activeCommand,
  actionError
}: {
  runs: ResearchRunRecord[];
  selectedIds: string[];
  selectedRun: ResearchRunRecord | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  activeCommand: ResearchCommand | null;
  actionError: string | null;
}): ResearchRunLibraryState {
  const selectedRuns = selectedIds
    .map((id) => runs.find((run) => run.id === id))
    .filter((run): run is ResearchRunRecord => run !== undefined);
  const hasTwoRuns = selectedIds.length === 2;
  const busy = activeCommand !== null;

  return {
    runs,
    selectedIds,
    selectedRuns,
    selectedRun,
    comparison,
    runDiff,
    promotionHistory,
    activeCommand,
    actionError,
    canCompare: hasTwoRuns && !busy,
    canDiff: hasTwoRuns && !busy,
    canLoadPromotionHistory: !busy,
    selectionText: buildSelectionText(selectedRuns),
    selectionDetail: hasTwoRuns
      ? "Ready to compare or diff the selected run pair."
      : "Select two runs to enable compare and diff commands.",
    compareButtonLabel: activeCommand === "compare" ? "Comparing..." : "Compare 2 runs",
    diffButtonLabel: activeCommand === "diff" ? "Diffing..." : "Diff 2 runs",
    promotionHistoryButtonLabel: activeCommand === "history" ? "Loading history..." : "Promotion history",
    statusAnnouncement: buildResearchStatusAnnouncement({
      activeCommand,
      actionError,
      comparison,
      runDiff,
      promotionHistory
    })
  };
}

export function toggleRunSelection(currentIds: string[], runId: string): string[] {
  if (currentIds.includes(runId)) {
    return currentIds.filter((id) => id !== runId);
  }

  return [...currentIds, runId].slice(-2);
}

function buildSelectionText(selectedRuns: ResearchRunRecord[]): string {
  if (selectedRuns.length === 0) {
    return "No runs selected";
  }

  if (selectedRuns.length === 1) {
    return `Selected ${selectedRuns[0].strategyName}`;
  }

  return `${selectedRuns[0].strategyName} vs ${selectedRuns[1].strategyName}`;
}

function buildResearchStatusAnnouncement({
  activeCommand,
  actionError,
  comparison,
  runDiff,
  promotionHistory
}: {
  activeCommand: ResearchCommand | null;
  actionError: string | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
}): string {
  if (activeCommand === "compare") {
    return "Comparing selected research runs.";
  }

  if (activeCommand === "diff") {
    return "Diffing selected research runs.";
  }

  if (activeCommand === "history") {
    return "Loading promotion history.";
  }

  if (actionError) {
    return `Research command failed: ${actionError}`;
  }

  if (runDiff) {
    return `Run diff ready for ${runDiff.baseStrategyName} and ${runDiff.targetStrategyName}.`;
  }

  if (comparison.length > 0) {
    return `${comparison.length} comparison ${comparison.length === 1 ? "row" : "rows"} loaded.`;
  }

  if (promotionHistory.length > 0) {
    return `${promotionHistory.length} promotion history ${promotionHistory.length === 1 ? "record" : "records"} loaded.`;
  }

  return "";
}
