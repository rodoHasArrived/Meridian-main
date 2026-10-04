import { describe, expect, it } from "vitest";
import {
  buildMaintenanceExecutionRow,
  buildMaintenanceMetrics,
  formatDuration,
  maintenanceExecutionStatusLabel,
  maintenanceTaskTypeLabel
} from "@/screens/data-operations-maintenance.view-model";
import type {
  ArchiveMaintenanceExecution,
  MaintenanceScheduleSummary,
  MaintenanceServiceStatus,
  MaintenanceStatistics,
  MaintenanceTaskTypeOption
} from "@/types/archive-maintenance.types";

// The catalog arrives in MaintenanceTaskType declaration order, so index === ordinal.
const taskTypes: MaintenanceTaskTypeOption[] = [
  { value: "HealthCheck", name: "HealthCheck", description: "Run health checks on storage files." },
  { value: "Cleanup", name: "Cleanup", description: "Clean up orphaned and temporary files." },
  { value: "Defragmentation", name: "Defragmentation", description: "Defragment small files." }
];

function execution(overrides: Partial<ArchiveMaintenanceExecution> = {}): ArchiveMaintenanceExecution {
  return {
    executionId: "exec-1",
    scheduleId: "schedule-1",
    scheduleName: "Nightly health check",
    taskType: 0,
    status: 2,
    manualTrigger: false,
    startedAt: "2026-05-29T02:00:00Z",
    completedAt: "2026-05-29T02:04:30Z",
    duration: "00:04:30.1234567",
    filesProcessed: 412,
    issuesFound: 3,
    issuesResolved: 2,
    bytesProcessed: 5_242_880,
    bytesSaved: 1_048_576,
    errorMessage: null,
    logMessages: [],
    ...overrides
  };
}

describe("maintenance execution rows", () => {
  it("resolves an ordinal task type against the catalog the server published", () => {
    expect(maintenanceTaskTypeLabel(1, taskTypes)).toBe("Cleanup");
  });

  it("names an ordinal the catalog does not cover instead of mislabelling it", () => {
    expect(maintenanceTaskTypeLabel(9, taskTypes)).toBe("Task type 9");
    expect(maintenanceExecutionStatusLabel(42)).toBe("Unknown status (42)");
  });

  it("projects an execution onto the history row", () => {
    expect(buildMaintenanceExecutionRow(execution(), taskTypes)).toMatchObject({
      taskTypeLabel: "HealthCheck",
      statusLabel: "Completed",
      statusTone: "success",
      triggerLabel: "Scheduled",
      scheduleLabel: "Nightly health check",
      durationLabel: "4m 30s",
      issuesLabel: "2/3",
      bytesSavedLabel: "1.0 MB",
      cancellable: false
    });
  });

  it("offers cancellation only while a run is pending or running", () => {
    expect(buildMaintenanceExecutionRow(execution({ status: 0 }), taskTypes).cancellable).toBe(true);
    expect(buildMaintenanceExecutionRow(execution({ status: 1 }), taskTypes).cancellable).toBe(true);
    expect(buildMaintenanceExecutionRow(execution({ status: 4 }), taskTypes).cancellable).toBe(false);
  });

  it("carries the failure message and a danger tone through to the row", () => {
    const row = buildMaintenanceExecutionRow(
      execution({ status: 4, errorMessage: "Checksum mismatch in shard 12", duration: null }),
      taskTypes
    );

    expect(row).toMatchObject({
      statusLabel: "Failed",
      statusTone: "danger",
      errorMessage: "Checksum mismatch in shard 12",
      durationLabel: "—"
    });
  });
});

describe("formatDuration", () => {
  it("renders .NET TimeSpan shapes compactly", () => {
    expect(formatDuration("00:00:07.5")).toBe("7s");
    expect(formatDuration("00:12:00")).toBe("12m 0s");
    expect(formatDuration("2.03:04:05")).toBe("2d 3h 4m 5s");
  });

  it("passes an unrecognized value through rather than inventing a duration", () => {
    expect(formatDuration("not-a-timespan")).toBe("not-a-timespan");
    expect(formatDuration(null)).toBe("—");
  });
});

describe("buildMaintenanceMetrics", () => {
  const status: MaintenanceServiceStatus = {
    isRunning: true,
    queuedExecutions: 2,
    currentExecution: null,
    nextScheduledExecution: "2026-05-30T02:00:00Z",
    activeSchedules: 4,
    totalExecutionsToday: 3,
    uptime: "1.02:00:00"
  };
  const statistics: MaintenanceStatistics = {
    generatedAt: "2026-05-29T12:00:00Z",
    totalSchedules: 5,
    enabledSchedules: 4,
    disabledSchedules: 1,
    totalExecutions: 40,
    successfulExecutions: 38,
    failedExecutions: 2,
    executionsLast24Hours: 3,
    executionsLast7Days: 21,
    totalBytesProcessed: 10_485_760,
    totalBytesSaved: 2_097_152,
    totalIssuesFound: 12,
    totalIssuesResolved: 11,
    averageExecutionDuration: "00:03:20",
    lastExecutionAt: "2026-05-29T02:04:30Z",
    nextScheduledExecution: "2026-05-30T02:00:00Z"
  };
  const summary: MaintenanceScheduleSummary = {
    totalSchedules: 5,
    enabledSchedules: 4,
    disabledSchedules: 1,
    byTaskType: { "0": 2, "1": 3 },
    nextDueSchedule: "2026-05-30T02:00:00Z",
    nextDueScheduleName: "Nightly health check"
  };

  it("reports server-side posture and flags failures as a warning", () => {
    const metrics = buildMaintenanceMetrics(status, statistics, summary);

    expect(metrics.map((metric) => [metric.id, metric.value])).toEqual([
      ["service", "Running"],
      ["success-rate", "95.0%"],
      ["recent-activity", "3"],
      ["schedules", "4/5"]
    ]);
    expect(metrics.find((metric) => metric.id === "success-rate")?.tone).toBe("warning");
    expect(metrics.find((metric) => metric.id === "schedules")?.detail).toBe("Next due: Nightly health check");
  });

  it("shows an unloaded read as unknown rather than as zero activity", () => {
    const metrics = buildMaintenanceMetrics(null, null, null);

    expect(metrics.every((metric) => metric.value === "—")).toBe(true);
    expect(metrics.find((metric) => metric.id === "service")?.detail).toBe("Service status has not loaded.");
  });

  it("distinguishes a fleet that has never run from a zero success rate", () => {
    const metrics = buildMaintenanceMetrics(status, { ...statistics, totalExecutions: 0 }, summary);

    expect(metrics.find((metric) => metric.id === "success-rate")?.value).toBe("No runs yet");
    expect(metrics.find((metric) => metric.id === "success-rate")?.tone).toBe("default");
  });
});
