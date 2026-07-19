import { useEffect, useMemo, useRef, type Dispatch, type SetStateAction } from "react";
import type { ReconciliationBreakQueueItem } from "@/types";
import type { AccountingWorkstream } from "./accounting-screen.task-mode-view-model";

export function useScopedReconciliationBreakQueue(
  breakQueue: ReconciliationBreakQueueItem[],
  selectedRunId: string | null,
  workstream: AccountingWorkstream,
  setSelectedBreakId: Dispatch<SetStateAction<string | null>>
): ReconciliationBreakQueueItem[] {
  const scopedBreakQueue = useMemo(
    () => workstream === "reconciliation"
      ? selectedRunId
        ? breakQueue.filter((item) => item.runId === selectedRunId)
        : []
      : breakQueue,
    [breakQueue, selectedRunId, workstream]
  );
  const selectedBreakRunIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (workstream !== "reconciliation") {
      selectedBreakRunIdRef.current = null;
      return;
    }

    const runChanged = selectedBreakRunIdRef.current !== selectedRunId;
    selectedBreakRunIdRef.current = selectedRunId;
    setSelectedBreakId((current) => {
      if (!runChanged && current && scopedBreakQueue.some((item) => item.breakId === current)) {
        return current;
      }

      return scopedBreakQueue[0]?.breakId ?? null;
    });
  }, [scopedBreakQueue, selectedRunId, setSelectedBreakId, workstream]);

  return scopedBreakQueue;
}
