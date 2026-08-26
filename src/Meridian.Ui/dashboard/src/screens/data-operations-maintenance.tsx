/**
 * Archive-maintenance operations panel for Data → Storage assurance.
 *
 * The schedule side of `/api/maintenance` was already reachable from the API
 * client; the run side — service status, statistics, execution history, manual
 * runs, and cancellation — had no operator surface at all. This panel is that
 * surface.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw, Wrench } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import {
  cancelMaintenanceExecution,
  executeMaintenanceTask,
  getFailedMaintenanceExecutions,
  getMaintenanceExecutions,
  getMaintenanceScheduleSummary,
  getMaintenanceServiceStatus,
  getMaintenanceStatistics,
  getMaintenanceTaskTypes
} from "@/lib/api/archive-maintenance.api";
import {
  buildMaintenanceExecutionRow,
  buildMaintenanceMetrics,
  type MaintenanceTone
} from "@/screens/data-operations-maintenance.view-model";
import type {
  MaintenanceExecution,
  MaintenanceScheduleSummary,
  MaintenanceServiceStatus,
  MaintenanceStatistics,
  MaintenanceTaskTypeOption
} from "@/types/archive-maintenance.types";

const EXECUTION_LIMIT = 25;

export function ArchiveMaintenanceOperations() {
  const [status, setStatus] = useState<MaintenanceServiceStatus | null>(null);
  const [statistics, setStatistics] = useState<MaintenanceStatistics | null>(null);
  const [scheduleSummary, setScheduleSummary] = useState<MaintenanceScheduleSummary | null>(null);
  const [taskTypes, setTaskTypes] = useState<MaintenanceTaskTypeOption[]>([]);
  const [executions, setExecutions] = useState<MaintenanceExecution[]>([]);
  const [failedOnly, setFailedOnly] = useState(false);
  const [selectedTaskType, setSelectedTaskType] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    // Settled rather than all: one unavailable read should not blank the whole
    // panel, and the metrics already distinguish "not loaded" from zero.
    const [statusResult, statisticsResult, summaryResult, taskTypeResult, executionResult] =
      await Promise.allSettled([
        getMaintenanceServiceStatus(),
        getMaintenanceStatistics(),
        getMaintenanceScheduleSummary(),
        getMaintenanceTaskTypes(),
        failedOnly ? getFailedMaintenanceExecutions(EXECUTION_LIMIT) : getMaintenanceExecutions(EXECUTION_LIMIT)
      ]);

    setStatus(statusResult.status === "fulfilled" ? statusResult.value : null);
    setStatistics(statisticsResult.status === "fulfilled" ? statisticsResult.value : null);
    setScheduleSummary(summaryResult.status === "fulfilled" ? summaryResult.value : null);
    if (taskTypeResult.status === "fulfilled") {
      setTaskTypes(taskTypeResult.value);
    }
    setExecutions(executionResult.status === "fulfilled" ? executionResult.value : []);

    const failures = [statusResult, statisticsResult, summaryResult, taskTypeResult, executionResult]
      .filter((result): result is PromiseRejectedResult => result.status === "rejected");
    if (failures.length > 0) {
      setError(`${failures.length} of 5 maintenance reads failed. ${errorMessage(failures[0].reason)}`);
    }
  }, [failedOnly]);

  useEffect(() => { void refresh(); }, [refresh]);

  const metrics = useMemo(
    () => buildMaintenanceMetrics(status, statistics, scheduleSummary),
    [status, statistics, scheduleSummary]
  );
  const rows = useMemo(
    () => executions.map((execution) => buildMaintenanceExecutionRow(execution, taskTypes)),
    [executions, taskTypes]
  );
  const runnableTaskType = selectedTaskType || taskTypes[0]?.value || "";

  async function runMaintenance() {
    if (!runnableTaskType) {
      return;
    }

    setBusy("execute");
    setError(null);
    setNotice(null);
    try {
      const execution = await executeMaintenanceTask({ taskType: runnableTaskType });
      setNotice(`Started ${runnableTaskType} as execution ${execution.executionId}.`);
      await refresh();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  }

  async function cancel(executionId: string) {
    setBusy(`cancel:${executionId}`);
    setError(null);
    setNotice(null);
    try {
      const result = await cancelMaintenanceExecution(executionId);
      setNotice(result.message);
      await refresh();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section aria-labelledby="archive-maintenance-title" className="workspace-region space-y-3">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="archive-maintenance-title">Scheduled archive maintenance</CardTitle>
            <CardDescription>
              Service health, execution history, and manual runs for storage health checks, compaction,
              tier migration, and retention enforcement.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? <StatusBanner tone="danger" title="Maintenance read needs attention" detail={error} /> : null}
        {notice ? <StatusBanner tone="success" title="Maintenance action accepted" detail={notice} /> : null}

        <div className="grid gap-2 md:grid-cols-4" aria-label="Archive maintenance posture">
          {metrics.map((metric) => (
            <div key={metric.id} className="rounded-[2px] border border-border bg-secondary/20 p-3">
              <div className="text-xs uppercase tracking-wide text-muted-foreground">{metric.label}</div>
              <div className={toneTextClassName(metric.tone)}>{metric.value}</div>
              <div className="mt-1 text-xs text-muted-foreground">{metric.detail}</div>
            </div>
          ))}
        </div>

        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1 text-xs uppercase tracking-wide text-muted-foreground">
            Task type
            <select
              className="h-9 rounded-[2px] border border-border bg-background px-2 text-sm text-foreground"
              aria-label="Maintenance task type"
              value={runnableTaskType}
              onChange={(event) => setSelectedTaskType(event.target.value)}
            >
              {taskTypes.map((taskType) => (
                <option key={taskType.value} value={taskType.value} title={taskType.description}>
                  {taskType.name}
                </option>
              ))}
            </select>
          </label>
          <Button size="sm" disabled={!runnableTaskType || busy === "execute"} onClick={() => void runMaintenance()}>
            <Wrench className="mr-2 h-4 w-4" />
            {busy === "execute" ? "Starting…" : "Run maintenance now"}
          </Button>
          <Button
            size="sm"
            variant="outline"
            aria-pressed={failedOnly}
            onClick={() => setFailedOnly((current) => !current)}
          >
            {failedOnly ? "Show all executions" : "Show failed only"}
          </Button>
        </div>

        <table className="w-full text-sm" aria-label="Maintenance execution history">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="py-2">Task</th>
              <th>Status</th>
              <th>Trigger</th>
              <th>Started</th>
              <th>Duration</th>
              <th>Files</th>
              <th>Issues fixed</th>
              <th>Reclaimed</th>
              <th className="sr-only">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={9} className="py-4 text-center text-muted-foreground">
                  {failedOnly ? "No failed maintenance executions recorded." : "No maintenance executions recorded."}
                </td>
              </tr>
            ) : rows.map((row) => (
              <tr key={row.executionId} className="border-t border-border/60">
                <td className="py-2">{row.taskTypeLabel}</td>
                <td>
                  <Badge variant={badgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  {row.errorMessage ? (
                    <div className="mt-1 text-xs text-danger">{row.errorMessage}</div>
                  ) : null}
                </td>
                <td>{row.triggerLabel}</td>
                <td className="font-mono text-xs">{formatTimestamp(row.startedAt)}</td>
                <td>{row.durationLabel}</td>
                <td>{row.filesProcessed}</td>
                <td>{row.issuesLabel}</td>
                <td>{row.bytesSavedLabel}</td>
                <td className="text-right">
                  {row.cancellable ? (
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy === `cancel:${row.executionId}`}
                      onClick={() => void cancel(row.executionId)}
                    >
                      Cancel
                    </Button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </section>
  );
}

function toneTextClassName(tone: MaintenanceTone): string {
  switch (tone) {
    case "success":
      return "mt-1 text-xl font-semibold text-success";
    case "warning":
      return "mt-1 text-xl font-semibold text-warning";
    case "danger":
      return "mt-1 text-xl font-semibold text-danger";
    default:
      return "mt-1 text-xl font-semibold";
  }
}

function badgeVariant(tone: MaintenanceTone): "default" | "success" | "warning" | "danger" {
  return tone === "default" ? "default" : tone;
}

function formatTimestamp(value: string): string {
  return new Date(value).toLocaleString();
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "The operation could not be completed.";
}
