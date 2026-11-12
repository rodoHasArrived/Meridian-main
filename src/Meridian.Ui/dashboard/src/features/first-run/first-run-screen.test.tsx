import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { renderWithRouter } from "@/test/render";
import { FirstRunScreen } from "./first-run-screen";
import type { FirstRunStatus } from "./types";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));

const status: FirstRunStatus = {
  isComplete: false,
  goal: null,
  starterKitId: null,
  dataChoice: null,
  workspace: { id: "setup", name: "Workspace", isSample: false, badge: "", safetyMessage: "", samplePackVersion: "" },
  starterKits: [],
  outcomes: [],
  recommendedActions: [],
  sampleWorkspace: null
};

beforeEach(() => vi.resetAllMocks());

describe("FirstRunScreen status loading", () => {
  it("shows an accessible failure and retries into the welcome screen", async () => {
    const user = userEvent.setup();
    vi.mocked(apiGetJson).mockRejectedValueOnce(new Error("Host unavailable")).mockResolvedValueOnce(status);
    const { container } = renderWithRouter(<FirstRunScreen />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Host unavailable");
    expect(screen.queryByText("Preparing Meridian…")).not.toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
    await user.click(screen.getByRole("button", { name: "Retry" }));

    expect(await screen.findByRole("heading", { name: "Welcome to Meridian" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(apiGetJson).toHaveBeenCalledTimes(2);
    expect(apiGetJson).toHaveBeenLastCalledWith(WORKSTATION_API_ENDPOINTS.firstRunStatus, { signal: expect.any(AbortSignal) });
  });

  it("shows progress during retry and permits another retry after a repeated failure", async () => {
    const user = userEvent.setup();
    let rejectRetry!: (reason: Error) => void;
    vi.mocked(apiGetJson)
      .mockRejectedValueOnce(new Error("First failure"))
      .mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectRetry = reject; }));
    renderWithRouter(<FirstRunScreen />);

    await user.click(await screen.findByRole("button", { name: "Retry" }));
    expect(screen.getByRole("status")).toHaveTextContent("Loading workspace setup");
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
    await act(async () => rejectRetry(new Error("Second failure")));
    expect(await screen.findByRole("alert")).toHaveTextContent("Second failure");
    expect(screen.getByRole("button", { name: "Retry" })).toBeEnabled();
    expect(apiGetJson).toHaveBeenCalledTimes(2);
  });

  it("uses supplied status without making another status request", () => {
    renderWithRouter(<FirstRunScreen initialStatus={status} />);
    expect(screen.getByRole("heading", { name: "Welcome to Meridian" })).toBeInTheDocument();
    expect(apiGetJson).not.toHaveBeenCalled();
  });

  it("aborts a pending status read on unmount", async () => {
    vi.mocked(apiGetJson).mockImplementation(() => new Promise(() => {}));
    const { unmount } = renderWithRouter(<FirstRunScreen />);
    await waitFor(() => expect(apiGetJson).toHaveBeenCalledTimes(1));
    const options = vi.mocked(apiGetJson).mock.calls[0][1];
    expect(options?.signal?.aborted).toBe(false);
    unmount();
    expect(options?.signal?.aborted).toBe(true);
  });
});
