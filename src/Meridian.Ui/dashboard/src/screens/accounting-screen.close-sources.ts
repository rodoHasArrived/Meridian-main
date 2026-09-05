import { useCallback, useEffect, useRef, useState } from "react";
import {
  getFinancialOperationsCommandCenter,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  getPrivateCapitalCloseCockpit,
  listDailyValuationSchedules,
} from "@/lib/api";
import type {
  DailyValuationScheduleWorkItem,
  FinancialOperationsCommandCenter,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  PrivateCapitalCloseCockpit,
} from "@/types";
import { formatApprovalError } from "./accounting-screen.approvals";

export interface CloseWorkflowQuery {
  entityId?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  ledgerBookId?: string;
  periodId?: string;
  status?: string;
}

const defaultServices = {
  getFinancialOperationsCommandCenter,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  getPrivateCapitalCloseCockpit,
  listDailyValuationSchedules,
};

interface CloseSources {
  financialOperationsCommandCenter: FinancialOperationsCommandCenter | null;
  privateCapitalCloseCockpit: PrivateCapitalCloseCockpit | null;
  closeWorkflow: OperationsContinuityWorkflow | null;
  dailyValuationSchedules: DailyValuationScheduleWorkItem[];
  financialOperationsCommandCenterError: string | null;
  closeWorkflowError: string | null;
}

const emptySources: CloseSources = {
  financialOperationsCommandCenter: null,
  privateCapitalCloseCockpit: null,
  closeWorkflow: null,
  dailyValuationSchedules: [],
  financialOperationsCommandCenterError: null,
  closeWorkflowError: null,
};

/** Never expose a prior route's decision while its replacement is loading. */
export function useAccountingCloseSources(
  query: CloseWorkflowQuery,
  enabled: boolean,
  services = defaultServices,
) {
  const key = JSON.stringify([enabled, query.fundProfileId, query.fundAccountId, query.ledgerBookId,
    query.entityId, query.periodId, query.status]);
  const currentKey = useRef(key);
  currentKey.current = key;
  const revision = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [snapshot, setSnapshot] = useState<{ key: string; loading: boolean; sources: CloseSources } | null>(null);

  const refreshCloseWorkflow = useCallback(async () => {
    if (key !== currentKey.current) return;
    const requestRevision = ++revision.current;
    controller.current?.abort();
    const request = new AbortController();
    controller.current = request;
    const isCurrent = () => !request.signal.aborted && requestRevision === revision.current && key === currentKey.current;
    if (!enabled) {
      setSnapshot({ key, loading: false, sources: emptySources });
      return;
    }

    setSnapshot({ key, loading: true, sources: emptySources });
    const options = { signal: request.signal, allowDevelopmentFallback: false };
    const sources: CloseSources = { ...emptySources };
    try {
      const [commandCenter, cockpit, rows, schedules] = await Promise.all([
        services.getFinancialOperationsCommandCenter(query, options).catch(error => {
          sources.financialOperationsCommandCenterError = formatApprovalError(error, "Financial Operations command center could not be loaded.");
          return null;
        }),
        services.getPrivateCapitalCloseCockpit(query, options).catch(error => {
          sources.financialOperationsCommandCenterError = formatApprovalError(error, "Private-capital close cockpit could not be loaded.");
          return null;
        }),
        services.getOperationsContinuityWorkflows(query, options).catch(error => {
          sources.closeWorkflowError = formatApprovalError(error, "Close workflow detail could not be loaded.");
          return [];
        }),
        services.listDailyValuationSchedules(options).catch(() => []),
      ]);
      if (!isCurrent()) return;
      sources.financialOperationsCommandCenter = commandCenter;
      sources.privateCapitalCloseCockpit = cockpit;
      sources.dailyValuationSchedules = schedules;
      const selected = selectCloseWorkflowSummary(rows, query);
      sources.closeWorkflow = selected
        ? await services.getOperationsContinuityWorkflow(selected.workflowId, options)
        : null;
    } catch (error) {
      Object.assign(sources, emptySources, {
        closeWorkflowError: formatApprovalError(error, "Close workflow detail could not be loaded."),
      });
    }
    if (isCurrent()) setSnapshot({ key, loading: false, sources });
  }, [enabled, key, query, services]);

  useEffect(() => {
    void refreshCloseWorkflow();
    return () => {
      ++revision.current;
      controller.current?.abort();
    };
  }, [refreshCloseWorkflow]);

  const current = enabled && snapshot?.key === key ? snapshot : null;
  return {
    ...(current?.sources ?? emptySources),
    financialOperationsCommandCenterLoading: enabled && (current?.loading ?? true),
    closeWorkflowLoading: enabled && (current?.loading ?? true),
    refreshCloseWorkflow,
  };
}

function selectCloseWorkflowSummary(rows: OperationsContinuityWorkflowSummary[], query: CloseWorkflowQuery) {
  const sorted = [...rows].sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc));
  const matches = (actual: string | null | undefined, expected: string | undefined) =>
    expected === undefined || actual?.localeCompare(expected, undefined, { sensitivity: "accent" }) === 0;
  const scopedRows = sorted.filter(row => matches(row.fundAccountId, query.fundAccountId)
    && matches(row.ledgerBookId, query.ledgerBookId) && matches(row.periodId, query.periodId)
    && matches(row.status, query.status));
  if ((query.fundProfileId || query.fundAccountId || query.ledgerBookId || query.periodId || query.status) && scopedRows.length === 0) return null;
  return scopedRows[0] ?? sorted[0] ?? null;
}
