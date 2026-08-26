/**
 * Presentation logic for the archive-maintenance panel on Data → Storage assurance.
 *
 * The `/api/maintenance/*` group serializes enums as ordinals, so the two lookups
 * below are the only place that coupling lives. Task-type names are resolved from
 * `GET /api/maintenance/task-types`, whose array is the enum in declaration order —
 * so the ordinal is its index and the labels stay correct without a second copy of
 * the enum in this file.
 */

import type {
  MaintenanceExecution,
  MaintenanceScheduleSummary,
  MaintenanceServiceStatus,
  MaintenanceStatistics,
  MaintenanceTaskTypeOption
} from "@/types/archive-maintenance.types";

export type MaintenanceTone = "default" | "success" | "warning" | "danger";

/**
 * `MaintenanceExecutionStatus` in declaration order. No endpoint publishes this
 * enum, so it is mirrored here; an ordinal outside the list is reported as unknown
 * rather than guessed at.
 */
const EXECUTION_STATUS_LABELS = [
  "Pending",
  "Running",
  "Completed",
  "Completed with warnings",
  "Failed",
  "Cancelled",
  "Timed out"
] as const;

const EXECUTION_STATUS_TONES: readonly MaintenanceTone[] = [
  "default",
  "default",
  "success",
  "warning",
  "danger",
  "warning",
  "danger"
];

/** Ordinals whose runs can still be cancelled. */
const CANCELLABLE_STATUS_ORDINALS = new Set([0, 1]);

export interface MaintenanceMetricViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: MaintenanceTone;
}

export interface MaintenanceExecutionRowViewModel {
  executionId: string;
  taskTypeLabel: string;
  statusLabel: string;
  statusTone: MaintenanceTone;
  triggerLabel: string;
  scheduleLabel: string;
  startedAt: string;
  durationLabel: string;
  filesProcessed: number;
  issuesLabel: string;
  bytesSavedLabel: string;
  errorMessage: string | null;
  cancellable: boolean;
}

export function maintenanceExecutionStatusLabel(ordinal: number): string {
  return EXECUTION_STATUS_LABELS[ordinal] ?? `Unknown status (${ordinal})`;
}

export function maintenanceExecutionStatusTone(ordinal: number): MaintenanceTone {
  return EXECUTION_STATUS_TONES[ordinal] ?? "warning";
}

/**
 * Resolves a task-type ordinal against the catalog the server published. Falls back
 * to the ordinal itself when the catalog has not loaded or is shorter than the
 * ordinal, so a run is never labelled as the wrong kind of maintenance.
 */
export function maintenanceTaskTypeLabel(
  ordinal: number,
  taskTypes: readonly MaintenanceTaskTypeOption[]
): string {
  return taskTypes[ordinal]?.name ?? `Task type ${ordinal}`;
}

export function buildMaintenanceExecutionRow(
  execution: MaintenanceExecution,
  taskTypes: readonly MaintenanceTaskTypeOption[]
): MaintenanceExecutionRowViewModel {
  return {
    executionId: execution.executionId,
    taskTypeLabel: maintenanceTaskTypeLabel(execution.taskType, taskTypes),
    statusLabel: maintenanceExecutionStatusLabel(execution.status),
    statusTone: maintenanceExecutionStatusTone(execution.status),
    triggerLabel: execution.manualTrigger ? "Manual" : "Scheduled",
    scheduleLabel: execution.scheduleName ?? execution.scheduleId ?? "—",
    startedAt: execution.startedAt,
    durationLabel: formatDuration(execution.duration),
    filesProcessed: execution.filesProcessed,
    issuesLabel: `${execution.issuesResolved}/${execution.issuesFound}`,
    bytesSavedLabel: formatBytes(execution.bytesSaved),
    errorMessage: execution.errorMessage ?? null,
    cancellable: CANCELLABLE_STATUS_ORDINALS.has(execution.status)
  };
}

/**
 * The headline row. Every value is server-reported; a read that has not resolved
 * shows an em dash rather than a zero, so "no data yet" never reads as "nothing
 * has run".
 */
export function buildMaintenanceMetrics(
  status: MaintenanceServiceStatus | null,
  statistics: MaintenanceStatistics | null,
  scheduleSummary: MaintenanceScheduleSummary | null
): MaintenanceMetricViewModel[] {
  return [
    {
      id: "service",
      label: "Maintenance service",
      value: status === null ? "—" : status.isRunning ? "Running" : "Stopped",
      detail: status === null
        ? "Service status has not loaded."
        : `${status.queuedExecutions} queued · ${status.activeSchedules} active schedules`,
      tone: status === null ? "default" : status.isRunning ? "success" : "warning"
    },
    {
      id: "success-rate",
      label: "Success rate",
      value: statistics === null ? "—" : formatSuccessRate(statistics),
      detail: statistics === null
        ? "Execution statistics have not loaded."
        : `${statistics.successfulExecutions} succeeded · ${statistics.failedExecutions} failed of ${statistics.totalExecutions}`,
      tone: statistics === null || statistics.totalExecutions === 0
        ? "default"
        : statistics.failedExecutions > 0
          ? "warning"
          : "success"
    },
    {
      id: "recent-activity",
      label: "Runs in last 24h",
      value: statistics === null ? "—" : String(statistics.executionsLast24Hours),
      detail: statistics === null
        ? "Execution statistics have not loaded."
        : `${statistics.executionsLast7Days} over 7 days · ${formatBytes(statistics.totalBytesSaved)} reclaimed`,
      tone: "default"
    },
    {
      id: "schedules",
      label: "Schedules",
      value: scheduleSummary === null ? "—" : `${scheduleSummary.enabledSchedules}/${scheduleSummary.totalSchedules}`,
      detail: scheduleSummary === null
        ? "Schedule summary has not loaded."
        : scheduleSummary.nextDueScheduleName
          ? `Next due: ${scheduleSummary.nextDueScheduleName}`
          : "No schedule is currently due",
      tone: scheduleSummary === null
        ? "default"
        : scheduleSummary.enabledSchedules === 0 && scheduleSummary.totalSchedules > 0
          ? "warning"
          : "default"
    }
  ];
}

/** Blank while statistics are absent, so an unloaded panel cannot read as 0%. */
function formatSuccessRate(statistics: MaintenanceStatistics): string {
  if (statistics.totalExecutions === 0) {
    return "No runs yet";
  }

  return `${((statistics.successfulExecutions / statistics.totalExecutions) * 100).toFixed(1)}%`;
}

/** Renders a .NET TimeSpan (`d.hh:mm:ss.fffffff` or `hh:mm:ss.fffffff`) compactly. */
export function formatDuration(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(value);
  if (!match) {
    return value;
  }

  const [, days, hours, minutes, seconds] = match;
  const parts: string[] = [];
  if (days) {
    parts.push(`${Number(days)}d`);
  }
  if (days || Number(hours) > 0) {
    parts.push(`${Number(hours)}h`);
  }
  if (days || Number(hours) > 0 || Number(minutes) > 0) {
    parts.push(`${Number(minutes)}m`);
  }
  parts.push(`${Number(seconds)}s`);

  return parts.join(" ");
}

export function formatBytes(value: number): string {
  if (value < 1024) {
    return `${value} B`;
  }

  const units = ["KB", "MB", "GB", "TB"];
  let current = value / 1024;
  let index = 0;
  while (current >= 1024 && index < units.length - 1) {
    current /= 1024;
    index += 1;
  }

  return `${current.toFixed(1)} ${units[index]}`;
}
