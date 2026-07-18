import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LifecycleControlPanel } from "@/components/meridian/lifecycle-control-panel";
import type {
  LifecycleShutdownAccepted,
  LifecycleShutdownOperation,
  LifecycleShutdownReceipt,
  RuntimeLifecycleSnapshot
} from "@/types";

const apiMocks = vi.hoisted(() => ({
  getRuntimeLifecycle: vi.fn(),
  getLatestRuntimeShutdownReceipt: vi.fn(),
  getRuntimeShutdownOperation: vi.fn(),
  requestRuntimeShutdown: vi.fn()
}));

vi.mock("@/lib/api", () => apiMocks);

const lifecycle: RuntimeLifecycleSnapshot = {
  sessionId: "session-0123456789abcdef",
  state: "Ready",
  readiness: "Ready",
  startedAtUtc: "2026-07-17T16:00:00Z",
  stateChangedAtUtc: "2026-07-17T16:00:05Z",
  activePhase: "Serving",
  acceptingWork: true,
  shutdownRequested: false,
  shutdownReason: null,
  activeShutdownOperationId: null,
  processId: 4120,
  processName: "Meridian",
  port: 8080,
  configPath: "C:\\Meridian\\config.json",
  uptimeSeconds: 3723,
  checks: [
    {
      id: "database",
      displayName: "Dedicated database",
      requirement: "Required",
      status: "Passing",
      message: "PostgreSQL accepted the readiness probe.",
      checkedAtUtc: "2026-07-17T17:02:00Z",
      durationMilliseconds: 12
    }
  ]
};

const receipt: LifecycleShutdownReceipt = {
  sessionId: "previous-session",
  operationId: "previous-operation",
  reason: "Operator",
  outcome: "Succeeded",
  startedAtUtc: "2026-07-16T20:00:00Z",
  completedAtUtc: "2026-07-16T20:00:04Z",
  forcedTermination: false,
  participants: []
};

const accepted: LifecycleShutdownAccepted = {
  accepted: true,
  operationId: "operation-1234567890",
  operationUri: "/api/system/shutdown/operation-1234567890",
  state: "ShutdownRequested",
  requestedAtUtc: "2026-07-17T17:03:00Z"
};

const restartOperation: LifecycleShutdownOperation = {
  operationId: accepted.operationId,
  reason: "Restart",
  detail: "Restart requested from the browser lifecycle control panel.",
  requestedBy: "browser-workstation",
  currentStage: "Requested",
  outcome: "Pending",
  requestedAtUtc: accepted.requestedAtUtc,
  deadlineUtc: "2026-07-17T17:03:45Z",
  completedAtUtc: null,
  stages: []
};

describe("LifecycleControlPanel", () => {
  beforeEach(() => {
    apiMocks.getRuntimeLifecycle.mockReset().mockResolvedValue(lifecycle);
    apiMocks.getLatestRuntimeShutdownReceipt.mockReset().mockResolvedValue(receipt);
    apiMocks.getRuntimeShutdownOperation.mockReset().mockResolvedValue(restartOperation);
    apiMocks.requestRuntimeShutdown.mockReset().mockResolvedValue(accepted);
  });

  it("shows current readiness evidence and the latest shutdown receipt", async () => {
    render(<LifecycleControlPanel />);

    expect(await screen.findByRole("region", { name: "Meridian lifecycle control" })).toBeInTheDocument();
    expect(await screen.findByText("Dedicated database")).toBeInTheDocument();
    expect(screen.getByText("PostgreSQL accepted the readiness probe.")).toBeInTheDocument();
    expect(screen.getByText("1h 2m")).toBeInTheDocument();
    expect(screen.getByText("Succeeded")).toBeInTheDocument();
    expect(screen.getByText("The host is accepting operator work.")).toBeInTheDocument();
  });

  it("confirms and submits a typed supervised restart request", async () => {
    const user = userEvent.setup();
    render(<LifecycleControlPanel />);
    await screen.findByText("Dedicated database");

    await user.click(screen.getByRole("button", { name: "Restart Meridian" }));
    expect(screen.getByRole("dialog", { name: "Restart Meridian?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Confirm restart" }));

    await waitFor(() => expect(apiMocks.requestRuntimeShutdown).toHaveBeenCalledWith({
      reason: "Restart",
      detail: "Restart requested from the browser lifecycle control panel.",
      requestedBy: "browser-workstation"
    }));
    expect(await screen.findByText("Restart accepted")).toBeInTheDocument();
    expect(apiMocks.getRuntimeShutdownOperation).toHaveBeenCalledWith(accepted.operationUri);
  });

  it("requires confirmation before submitting an operator shutdown", async () => {
    const user = userEvent.setup();
    render(<LifecycleControlPanel />);
    await screen.findByText("Dedicated database");

    await user.click(screen.getByRole("button", { name: "Shut down Meridian" }));
    expect(apiMocks.requestRuntimeShutdown).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Confirm shutdown" }));

    await waitFor(() => expect(apiMocks.requestRuntimeShutdown).toHaveBeenCalledWith(expect.objectContaining({
      reason: "Operator",
      requestedBy: "browser-workstation"
    })));
  });

  it("reenables lifecycle controls after a supervised restart creates a new host session", async () => {
    const user = userEvent.setup();
    render(<LifecycleControlPanel />);
    await screen.findByText("Dedicated database");

    await user.click(screen.getByRole("button", { name: "Restart Meridian" }));
    await user.click(screen.getByRole("button", { name: "Confirm restart" }));
    expect(await screen.findByText("Restart accepted")).toBeInTheDocument();

    apiMocks.getRuntimeLifecycle.mockResolvedValue({
      ...lifecycle,
      sessionId: "replacement-session-1234",
      uptimeSeconds: 1
    });
    await user.click(screen.getByRole("button", { name: "Refresh" }));

    await waitFor(() => expect(screen.queryByText("Restart accepted")).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Restart Meridian" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Shut down Meridian" })).toBeEnabled();
  });
});
