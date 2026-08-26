import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as maintenanceApi from "@/lib/api/archive-maintenance.api";
import { ArchiveMaintenanceOperations } from "@/screens/data-operations-maintenance";
import type { ArchiveMaintenanceExecution } from "@/types/archive-maintenance.types";

vi.mock("@/lib/api/archive-maintenance.api", () => ({
  getMaintenanceServiceStatus: vi.fn(),
  getMaintenanceStatistics: vi.fn(),
  getMaintenanceScheduleSummary: vi.fn(),
  getMaintenanceTaskTypes: vi.fn(),
  getMaintenanceExecutions: vi.fn(),
  getFailedMaintenanceExecutions: vi.fn(),
  executeMaintenanceTask: vi.fn(),
  cancelMaintenanceExecution: vi.fn()
}));

const api = vi.mocked(maintenanceApi);

const runningExecution: ArchiveMaintenanceExecution = {
  executionId: "exec-running",
  scheduleId: null,
  scheduleName: null,
  taskType: 1,
  status: 1,
  manualTrigger: true,
  startedAt: "2026-05-29T02:00:00Z",
  completedAt: null,
  duration: null,
  filesProcessed: 12,
  issuesFound: 0,
  issuesResolved: 0,
  bytesProcessed: 0,
  bytesSaved: 0,
  errorMessage: null,
  logMessages: []
};

const failedExecution: ArchiveMaintenanceExecution = {
  ...runningExecution,
  executionId: "exec-failed",
  taskType: 0,
  status: 4,
  manualTrigger: false,
  scheduleName: "Nightly health check",
  errorMessage: "Checksum mismatch in shard 12"
};

function primeReads() {
  api.getMaintenanceServiceStatus.mockResolvedValue({
    isRunning: true,
    queuedExecutions: 1,
    currentExecution: null,
    nextScheduledExecution: "2026-05-30T02:00:00Z",
    activeSchedules: 3,
    totalExecutionsToday: 2,
    uptime: "1.02:00:00"
  });
  api.getMaintenanceStatistics.mockResolvedValue({
    generatedAt: "2026-05-29T12:00:00Z",
    totalSchedules: 4,
    enabledSchedules: 3,
    disabledSchedules: 1,
    totalExecutions: 20,
    successfulExecutions: 19,
    failedExecutions: 1,
    executionsLast24Hours: 2,
    executionsLast7Days: 9,
    totalBytesProcessed: 1_048_576,
    totalBytesSaved: 524_288,
    totalIssuesFound: 4,
    totalIssuesResolved: 4,
    averageExecutionDuration: "00:02:00",
    lastExecutionAt: "2026-05-29T02:00:00Z",
    nextScheduledExecution: "2026-05-30T02:00:00Z"
  });
  api.getMaintenanceScheduleSummary.mockResolvedValue({
    totalSchedules: 4,
    enabledSchedules: 3,
    disabledSchedules: 1,
    byTaskType: { "0": 2, "1": 2 },
    nextDueSchedule: "2026-05-30T02:00:00Z",
    nextDueScheduleName: "Nightly health check"
  });
  api.getMaintenanceTaskTypes.mockResolvedValue([
    { value: "HealthCheck", name: "HealthCheck", description: "Run health checks." },
    { value: "Cleanup", name: "Cleanup", description: "Clean up orphans." }
  ]);
  api.getMaintenanceExecutions.mockResolvedValue([runningExecution, failedExecution]);
  api.getFailedMaintenanceExecutions.mockResolvedValue([failedExecution]);
}

afterEach(() => {
  vi.resetAllMocks();
});

describe("ArchiveMaintenanceOperations", () => {
  it("renders service posture and execution history from the maintenance endpoints", async () => {
    primeReads();
    render(<ArchiveMaintenanceOperations />);

    const posture = await screen.findByLabelText("Archive maintenance posture");
    expect(within(posture).getByText("Running")).toBeInTheDocument();
    expect(within(posture).getByText("95.0%")).toBeInTheDocument();
    expect(within(posture).getByText("Next due: Nightly health check")).toBeInTheDocument();

    const history = screen.getByLabelText("Maintenance execution history");
    expect(within(history).getByText("Cleanup")).toBeInTheDocument();
    expect(within(history).getByText("Checksum mismatch in shard 12")).toBeInTheDocument();
  });

  it("starts a manual run for the selected task type and refreshes", async () => {
    primeReads();
    api.executeMaintenanceTask.mockResolvedValue({ ...runningExecution, executionId: "exec-new" });
    const user = userEvent.setup();
    render(<ArchiveMaintenanceOperations />);

    await screen.findByLabelText("Archive maintenance posture");
    await user.selectOptions(screen.getByLabelText("Maintenance task type"), "Cleanup");
    await user.click(screen.getByRole("button", { name: "Run maintenance now" }));

    await waitFor(() => expect(api.executeMaintenanceTask).toHaveBeenCalledWith({ taskType: "Cleanup" }));
    expect(await screen.findByText("Started Cleanup as execution exec-new.")).toBeInTheDocument();
    expect(api.getMaintenanceExecutions).toHaveBeenCalledTimes(2);
  });

  it("cancels a running execution", async () => {
    primeReads();
    api.cancelMaintenanceExecution.mockResolvedValue({ message: "Execution 'exec-running' cancelled" });
    const user = userEvent.setup();
    render(<ArchiveMaintenanceOperations />);

    await screen.findByLabelText("Archive maintenance posture");
    const cancelButtons = screen.getAllByRole("button", { name: "Cancel" });
    expect(cancelButtons).toHaveLength(1);

    await user.click(cancelButtons[0]);
    await waitFor(() => expect(api.cancelMaintenanceExecution).toHaveBeenCalledWith("exec-running"));
    expect(await screen.findByText("Execution 'exec-running' cancelled")).toBeInTheDocument();
  });

  it("switches the history to the failed-only read", async () => {
    primeReads();
    const user = userEvent.setup();
    render(<ArchiveMaintenanceOperations />);

    await screen.findByLabelText("Archive maintenance posture");
    await user.click(screen.getByRole("button", { name: "Show failed only" }));

    await waitFor(() => expect(api.getFailedMaintenanceExecutions).toHaveBeenCalledWith(25));
    const history = screen.getByLabelText("Maintenance execution history");
    await waitFor(() => expect(within(history).queryAllByRole("row")).toHaveLength(2));
  });

  it("keeps the panel readable when one read fails and says how many failed", async () => {
    primeReads();
    api.getMaintenanceStatistics.mockRejectedValue(new Error("statistics unavailable"));
    render(<ArchiveMaintenanceOperations />);

    expect(await screen.findByText(/1 of 5 maintenance reads failed/)).toBeInTheDocument();
    const posture = screen.getByLabelText("Archive maintenance posture");
    expect(within(posture).getByText("Running")).toBeInTheDocument();
    // Both statistics-backed metrics fall back together; neither reports a zero.
    expect(within(posture).getAllByText("Execution statistics have not loaded.")).toHaveLength(2);
  });
});
