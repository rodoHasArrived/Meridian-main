import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { appendViewStateToRoute } from "@/lib/view-state-envelope";
import {
  buildOperationsRecordReleaseViewStateEnvelope
} from "@/screens/operations-record-release-screen.view-model";
import { OperationsRecordReleaseScreen } from "@/screens/operations-record-release-screen";
import { renderWithRouter } from "@/test/render";
import type { DataWorkspaceResponse } from "@/types";

vi.mock("@/screens/operations-continuity-screen.view-model", async () => {
  const actual = await vi.importActual<typeof import("@/screens/operations-continuity-screen.view-model")>(
    "@/screens/operations-continuity-screen.view-model"
  );

  return {
    ...actual,
    useOperationsContinuityScreenViewModel: () => actual.buildOperationsContinuityScreenViewModel({
      workflows: [],
      selectedWorkflowId: null,
      detail: null,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    })
  };
});

afterEach(() => {
  cleanup();
});

const data: DataWorkspaceResponse = {
  metrics: [],
  providers: [
    {
      provider: "Custodian",
      status: "Healthy",
      capability: "Activity files",
      latency: "10ms",
      note: "Ready"
    }
  ],
  backfills: [],
  exports: [
    {
      exportId: "export-1",
      profile: "source-data",
      target: "accounting",
      status: "Ready",
      rows: "10k",
      updatedAt: "1m ago"
    }
  ]
};

describe("OperationsRecordReleaseScreen", () => {
  it("renders the release path as the central selectable work surface and updates the inspector", () => {
    renderWithRouter(<OperationsRecordReleaseScreen data={data} reporting={null} />, {
      initialEntries: ["/reporting/operations-record"]
    });

    expect(screen.getByRole("region", { name: "Operations record release workbench" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Operations record release workbench work surface" })).toHaveTextContent(
      "Source data to report pack"
    );
    expect(screen.getByText("Release route details").closest("details")).not.toHaveAttribute("open");

    const sourceStep = screen.getByRole("button", { name: /Select release step Source data/i });
    expect(sourceStep).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("region", { name: "Selected release step inspector" })).toHaveTextContent(
      "Provider posture"
    );

    fireEvent.click(screen.getByRole("button", { name: /Select release step Reconcile/i }));

    const reconcileStep = screen.getByRole("button", { name: /Select release step Reconcile/i });
    expect(reconcileStep).toHaveAttribute("aria-pressed", "true");
    const inspector = screen.getByRole("region", { name: "Selected release step inspector" });
    expect(inspector).toHaveTextContent("Reconcile");
    expect(inspector).toHaveTextContent("/accounting/reconciliation");
    expect(within(inspector).getByText("Step route details").closest("details")).not.toHaveAttribute("open");
    expect(within(inspector).getByRole("link", { name: "Open route for Reconcile" })).toHaveAttribute(
      "href",
      "/accounting/reconciliation"
    );
  });

  it("hydrates the selected release step from the screen-local view-state envelope", () => {
    const route = appendViewStateToRoute(
      "/reporting/operations-record",
      buildOperationsRecordReleaseViewStateEnvelope({ selectedStepId: "report" })
    );

    renderWithRouter(<OperationsRecordReleaseScreen data={data} reporting={null} />, {
      initialEntries: [route]
    });

    expect(screen.getByRole("button", { name: /Select release step Report pack/i })).toHaveAttribute(
      "aria-pressed",
      "true"
    );
    expect(screen.getByRole("region", { name: "Selected release step inspector" })).toHaveTextContent(
      "Report pack"
    );
  });
});
