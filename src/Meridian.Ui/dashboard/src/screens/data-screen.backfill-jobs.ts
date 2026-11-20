import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { describeApiError } from "@/lib/api-errors";
import { formatCount, formatTimestamp } from "@/lib/format";
import type { DataOperationsWorkstream } from "@/screens/data-screen.route-state";
import type { BackfillExecutionHistoryResponse, BackfillJobReceipt, DataBackfillRecord, DataWorkspaceResponse } from "@/types";

export function useBackfillJobHistory(
  data: DataWorkspaceResponse | null,
  workstream: DataOperationsWorkstream,
  getBackfillExecutions?: (signal?: AbortSignal) => Promise<BackfillExecutionHistoryResponse>,
  getBackfillJob?: (id: string, signal?: AbortSignal) => Promise<BackfillJobReceipt>
) {
  const [backfillJob, setBackfillJob] = useState<BackfillJobReceipt | null>(null);
  const [backfillJobStatusError, setBackfillJobStatusError] = useState<string | null>(null);
  const [remediationHistory, setRemediationHistory] = useState<BackfillExecutionHistoryResponse | null>(null);
  const [remediationHistoryLoading, setRemediationHistoryLoading] = useState(false);
  const [remediationHistoryError, setRemediationHistoryError] = useState<string | null>(null);
  const remediationHistoryRevisionRef = useRef(0);
  const resetBackfillJob = useCallback(() => {
    setBackfillJob(null);
    setBackfillJobStatusError(null);
  }, []);

  const refreshRemediationHistory = useCallback(async (signal?: AbortSignal, background = false) => {
    if (!getBackfillExecutions) {
      return;
    }

    const revision = remediationHistoryRevisionRef.current + 1;
    remediationHistoryRevisionRef.current = revision;
    if (!background) setRemediationHistoryLoading(true);

    try {
      const response = await getBackfillExecutions(signal);
      if (!signal?.aborted && remediationHistoryRevisionRef.current === revision) {
        setRemediationHistory(response);
        setRemediationHistoryError(null);
      }
    } catch (failure: unknown) {
      if (!signal?.aborted && remediationHistoryRevisionRef.current === revision) {
        setRemediationHistoryError(
          describeApiError(failure, "Backfill remediation history is unavailable.").summary
        );
      }
    } finally {
      if (!signal?.aborted && remediationHistoryRevisionRef.current === revision) {
        setRemediationHistoryLoading(false);
      }
    }
  }, [getBackfillExecutions]);

  useEffect(() => {
    if (workstream !== "backfills") {
      return undefined;
    }

    const controller = new AbortController();
    void refreshRemediationHistory(controller.signal);

    return () => controller.abort();
  }, [refreshRemediationHistory, workstream]);

  const activeJobId = backfillJob && isActiveBackfillJob(backfillJob) ? backfillJob.executionId : null;
  useEffect(() => {
    if (!activeJobId || !getBackfillJob) return undefined;
    const controller = new AbortController();
    const poll = async () => {
      let retryDelay = 3000;
      while (!controller.signal.aborted) {
        let nextPoll = 3000;
        try {
          const job = await getBackfillJob(activeJobId, controller.signal);
          if (controller.signal.aborted) return;
          setBackfillJob((current) => current?.executionId === activeJobId ? job : current);
          setBackfillJobStatusError(null);
          retryDelay = 3000;
          if (!isActiveBackfillJob(job)) {
            void refreshRemediationHistory(undefined, true);
            return;
          }
          if (job.status === "Scheduled" && job.notBeforeUtc) {
            const startsIn = Date.parse(job.notBeforeUtc) - Date.now();
            nextPoll = Number.isFinite(startsIn) ? Math.max(3000, Math.min(30_000, startsIn)) : 30_000;
          } else if (job.status === "Throttled") {
            nextPoll = 5000;
          }
        } catch (failure) {
          if (!controller.signal.aborted) {
            setBackfillJobStatusError(describeApiError(failure, "Latest job status is unavailable. Retrying.").summary);
            nextPoll = retryDelay;
            retryDelay = Math.min(30_000, retryDelay * 2);
          }
        }
        await waitForBackfillProgressPoll(nextPoll, controller.signal);
      }
    };
    void poll();
    return () => controller.abort();
  }, [activeJobId, getBackfillJob, refreshRemediationHistory]);

  const historyPollInterval = (remediationHistory?.executions ?? []).reduce<number | null>((interval, { job }) => {
    if (!job || job.executionId === activeJobId || !isActiveBackfillJob(job)) return interval;
    return Math.min(interval ?? 30_000, job.status === "Scheduled" ? 30_000 : 6000);
  }, null);
  useEffect(() => {
    if (workstream !== "backfills" || historyPollInterval === null) return undefined;
    const controller = new AbortController();
    const poll = async () => {
      while (!controller.signal.aborted) {
        await waitForBackfillProgressPoll(historyPollInterval, controller.signal);
        if (!controller.signal.aborted) await refreshRemediationHistory(controller.signal, true);
      }
    };
    void poll();
    return () => controller.abort();
  }, [historyPollInterval, refreshRemediationHistory, workstream]);

  const jobReceipts = useMemo(() => {
    const jobs = new Map<string, BackfillJobReceipt>();
    if (backfillJob) jobs.set(backfillJob.executionId, backfillJob);
    for (const entry of remediationHistory?.executions ?? []) {
      if (entry.job && !jobs.has(entry.job.executionId)) jobs.set(entry.job.executionId, entry.job);
    }
    return jobs;
  }, [backfillJob, remediationHistory]);
  const dataWithBackfillJobs = useMemo(() => data && jobReceipts.size > 0 ? {
    ...data,
    backfills: [
      ...Array.from(jobReceipts.values(), projectBackfillJob),
      ...data.backfills.filter((record) => !jobReceipts.has(record.jobId))
    ]
  } : data, [data, jobReceipts]);

  return {
    backfillJob, backfillJobStatusError, setBackfillJob, resetBackfillJob,
    remediationHistory, remediationHistoryLoading, remediationHistoryError,
    refreshRemediationHistory, jobReceipts, dataWithBackfillJobs
  };
}

function isActiveBackfillJob(job: BackfillJobReceipt): boolean {
  return job.status === "Scheduled" || job.status === "Queued" ||
    job.status === "Running" || job.status === "Throttled";
}

function projectBackfillJob(job: BackfillJobReceipt): DataBackfillRecord {
  const range = formatBackfillRange(job.from, job.to);
  return {
    jobId: job.executionId,
    scope: `${job.symbols.join(", ")} · ${range}`,
    provider: job.provider,
    status: job.status,
    progress: job.barsWritten === null ? "Pending" : `${formatCount(job.barsWritten)} bars`,
    updatedAt: formatBackfillProgressTimestamp(job.completedAt ?? job.startedAt ?? job.submittedAt)
  };
}

export function formatBackfillProgressTimestamp(value: string | null | undefined): string {
  if (!value) return "Time unavailable";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? "Time unavailable" : formatTimestamp(parsed);
}

export function waitForBackfillProgressPoll(delayMs: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) {
    return Promise.resolve();
  }

  return new Promise((resolve) => {
    const complete = () => {
      window.clearTimeout(timer);
      signal.removeEventListener("abort", complete);
      resolve();
    };
    const timer = window.setTimeout(complete, delayMs);
    signal.addEventListener("abort", complete, { once: true });
  });
}

export function formatBackfillRange(from: string | null, to: string | null): string {
  const fromText = from?.trim();
  const toText = to?.trim();

  if (fromText && toText) {
    return `${fromText} to ${toText}`;
  }

  if (fromText) {
    return `From ${fromText}`;
  }

  if (toText) {
    return `Through ${toText}`;
  }

  return "Full available history";
}
