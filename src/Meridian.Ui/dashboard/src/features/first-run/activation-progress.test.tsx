import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ActivationHeaderProgress } from "./activation-progress";
import { resetActivationProgressForTests } from "@/lib/first-run/activation";
import type { FirstRunStatus } from "./types";

vi.mock("@/lib/api", () => ({ apiPostJson: vi.fn().mockResolvedValue({}) }));

const navigate = vi.hoisted(() => vi.fn());
vi.mock("react-router-dom", async () => ({
  ...(await vi.importActual<typeof import("react-router-dom")>("react-router-dom")),
  useNavigate: () => navigate
}));

function status(overrides: Partial<FirstRunStatus> = {}): FirstRunStatus {
  return {
    isComplete: true,
    goal: "monitor-investments",
    starterKitId: "personal-portfolio",
    dataChoice: "upload",
    workspace: {
      id: "primary",
      name: "Meridian Workspace",
      isSample: false,
      badge: "LOCAL",
      safetyMessage: "Local workspace data.",
      samplePackVersion: ""
    },
    starterKits: [],
    outcomes: [
      { key: "workspace-opened", label: "Open or create a workspace", actionLabel: "Open workspace", route: "/portfolio", isComplete: true, completedAtUtc: "2026-08-01T10:00:00Z" },
      { key: "data-imported", label: "Import sample or real data", actionLabel: "Import data", route: "/accounting/statement-import", isComplete: false, completedAtUtc: null }
    ],
    recommendedActions: [],
    sampleWorkspace: null,
    ...overrides
  };
}

afterEach(() => {
  navigate.mockReset();
  resetActivationProgressForTests();
});

describe("ActivationHeaderProgress", () => {
  it("renders nothing before setup is finished", () => {
    const { container } = renderWithRouter(<ActivationHeaderProgress status={status({ isComplete: false })} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("names the next outstanding step on the masthead trigger", () => {
    renderWithRouter(<ActivationHeaderProgress status={status()} />);

    expect(screen.getByRole("button", { name: /Getting started 1\/2/ })).toHaveAccessibleName(
      /next: Import sample or real data/
    );
  });

  it("lists every step with its state when the checklist is opened", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ActivationHeaderProgress status={status()} />);

    await user.click(screen.getByRole("button", { name: /Getting started/ }));

    expect(await screen.findByText("Getting started")).toBeInTheDocument();
    expect(screen.getByText("Open or create a workspace")).toBeInTheDocument();
    expect(screen.getByText("Import sample or real data")).toBeInTheDocument();
    expect(screen.getByText("Next step")).toBeInTheDocument();
  });

  it("routes to the step the user picks and closes the checklist", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ActivationHeaderProgress status={status()} />);
    await user.click(screen.getByRole("button", { name: /Getting started/ }));

    await user.click(await screen.findByRole("button", { name: /Import data/ }));

    expect(navigate).toHaveBeenCalledWith("/accounting/statement-import");
    await waitFor(() => expect(screen.queryByText("Next step")).not.toBeInTheDocument());
  });

  it("keeps showing the sample-workspace badge alongside the checklist", () => {
    renderWithRouter(<ActivationHeaderProgress status={status({
      workspace: {
        id: "sample",
        name: "Meridian Sample Workspace",
        isSample: true,
        badge: "SAMPLE · PAPER",
        safetyMessage: "Sample data only. No live trading or production accounting.",
        samplePackVersion: "2026.1"
      }
    })} />);

    expect(screen.getByText("SAMPLE · PAPER")).toBeInTheDocument();
  });
});
