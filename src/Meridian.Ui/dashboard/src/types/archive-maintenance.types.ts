/**
 * Archive-maintenance read-model types.
 *
 * Mirrors the `/api/maintenance/*` contract in
 * `Meridian.Ui.Shared.Endpoints.ArchiveMaintenanceEndpoints`. That endpoint group
 * serializes with camelCase names and **no** string-enum converter, so
 * `MaintenanceTaskType` and `MaintenanceExecutionStatus` arrive as their numeric
 * ordinals rather than names — hence the `number` fields below.
 */

/** Ordinal of `MaintenanceTaskType`; resolve to a label with the task-type catalog. */
export type MaintenanceTaskTypeOrdinal = number;

/** Ordinal of `MaintenanceExecutionStatus`. */
export type MaintenanceExecutionStatusOrdinal = number;

/** One entry of `GET /api/maintenance/task-types`, in enum declaration order. */
export interface MaintenanceTaskTypeOption {
  value: string;
  name: string;
  description: string;
}

/** `GET /api/maintenance/status` — live state of the scheduled maintenance service. */
export interface MaintenanceServiceStatus {
  isRunning: boolean;
  queuedExecutions: number;
  currentExecution: ArchiveMaintenanceExecution | null;
  nextScheduledExecution?: string | null;
  activeSchedules: number;
  totalExecutionsToday: number;
  /** .NET TimeSpan, serialized as `d.hh:mm:ss(.fffffff)`. */
  uptime: string;
}

/** `GET /api/maintenance/statistics`, enriched with the schedule counts. */
export interface MaintenanceStatistics {
  generatedAt: string;
  totalSchedules: number;
  enabledSchedules: number;
  disabledSchedules: number;
  totalExecutions: number;
  successfulExecutions: number;
  failedExecutions: number;
  executionsLast24Hours: number;
  executionsLast7Days: number;
  totalBytesProcessed: number;
  totalBytesSaved: number;
  totalIssuesFound: number;
  totalIssuesResolved: number;
  /** .NET TimeSpan. */
  averageExecutionDuration: string;
  lastExecutionAt?: string | null;
  nextScheduledExecution?: string | null;
}

/** `GET /api/maintenance/schedules/summary`. */
export interface MaintenanceScheduleSummary {
  totalSchedules: number;
  enabledSchedules: number;
  disabledSchedules: number;
  /** Keyed by `MaintenanceTaskType` ordinal, rendered as a string by the serializer. */
  byTaskType: Record<string, number>;
  nextDueSchedule?: string | null;
  nextDueScheduleName?: string | null;
}

/**
 * One maintenance run, scheduled or manually triggered.
 *
 * Named apart from the barrel's `MaintenanceExecution` (`types/workstation-8.ts`)
 * deliberately: that one models the same server object with `status` and `taskType`
 * as strings, which is not what this endpoint group puts on the wire. Sharing the
 * name would let an import resolve to the wrong representation silently.
 */
export interface ArchiveMaintenanceExecution {
  executionId: string;
  scheduleId?: string | null;
  scheduleName?: string | null;
  taskType: MaintenanceTaskTypeOrdinal;
  status: MaintenanceExecutionStatusOrdinal;
  manualTrigger: boolean;
  startedAt: string;
  completedAt?: string | null;
  /** .NET TimeSpan, absent while the run is still in flight. */
  duration?: string | null;
  filesProcessed: number;
  issuesFound: number;
  issuesResolved: number;
  bytesProcessed: number;
  bytesSaved: number;
  errorMessage?: string | null;
  logMessages: string[];
}

/** Body of `POST /api/maintenance/execute`. */
export interface ExecuteMaintenanceRequest {
  /** `MaintenanceTaskType` **name**, not ordinal — the endpoint parses it by name. */
  taskType: string;
}
