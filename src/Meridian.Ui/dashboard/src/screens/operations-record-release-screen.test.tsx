import { screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup } from "@testing-library/react";
import { OperationsRecordReleaseScreen } from "@/screens/operations-record-release-screen";
import { renderWithRouter } from "@/test/render";
import type { DataWorkspaceResponse, AccountingWorkspaceResponse } from "@/types";

vi.mock("@/screens/operations-continuity-screen.view-model", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/screens/operations-continuity-screen.view-model")>();
  return { ...actual, useOperationsContinuityScreenViewModel: vi.fn() };
});

vi.mock("@/screens/reporting-screen.view-model", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/screens/reporting-screen.view-model")>();
  return { ...actual, useReportingScreenViewModel: vi.fn() };
});

import { useOperationsContinuityScreenViewModel } from "@/screens/operations-continuity-screen.view-model";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";

const mockUseContinuity = vi.mocked(useOperationsContinuityScreenViewModel);
const mockUseReporting = vi.mocked(useReportingScreenViewModel);

function makeContinuityVm() {
  return {
    tone: "success" as const,
    summaryLabel: "All systems operational.",
    lastUpdatedLabel: "Just now",
    metrics: [],
    subsystems: [],
    recentEvents: [],
    isLoading: false,
    refresh: vi.fn(),
  };
}

function makeReportingVm() {
  return {
    reportPacks: [],
    templates: [],
    recentRuns: [],
    reportPacksLabel: "Report packs",
    templatesLabel: "Templates",
    recentRunsLabel: "Recent runs",
    reportPacksEmptyText: "No report packs.",
    templatesEmptyText: "No templates.",
    recentRunsEmptyText: "No recent runs.",
  };
}

const minimalData: DataWorkspaceResponse = {
  metrics: [],
  providers: [],
  backfills: [],
  exports: [],
};

const minimalReporting: AccountingWorkspaceResponse = {
  reporting: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUseContinuity.mockReturnValue(makeContinuityVm() as ReturnType<typeof useOperationsContinuityScreenViewModel>);
  mockUseReporting.mockReturnValue(makeReportingVm() as ReturnType<typeof useReportingScreenViewModel>);
});

afterEach(() => {
  cleanup();
});

describe("OperationsRecordReleaseScreen", () => {
  it("renders top-level overview section with eyebrow and title", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={minimalData} reporting={minimalReporting} />
    );
    expect(screen.getByText("W1-W5 release path")).toBeInTheDocument();
    expect(screen.getByText("Operations record release")).toBeInTheDocument();
  });

  it("renders metrics section with aria-label", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={minimalData} reporting={minimalReporting} />
    );
    const metricsSection = screen.getByRole("region", { name: "Operations record release metrics" });
    expect(metricsSection).toBeInTheDocument();
  });

  it("renders status badge reflecting overall tone", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={null} reporting={minimalReporting} />
    );
    const overviewSection = screen.getByRole("region", { name: "Operations record release overview" });
    expect(overviewSection).toBeInTheDocument();
    const badge = within(overviewSection).getByText(/Release/i);
    expect(badge).toBeInTheDocument();
  });

  it("renders step navigation list with aria-label", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={minimalData} reporting={minimalReporting} />
    );
    const stepsSection = screen.getByRole("list");
    expect(stepsSection).toBeInTheDocument();
  });

  it("renders three release panels (source, accounting, report pack)", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={minimalData} reporting={minimalReporting} />
    );
    expect(screen.getByText("Source data")).toBeInTheDocument();
    expect(screen.getByText("Accounting record")).toBeInTheDocument();
    expect(screen.getByText("Report pack")).toBeInTheDocument();
  });

  it("renders with null data props without crashing", () => {
    renderWithRouter(
      <OperationsRecordReleaseScreen data={null} reporting={null} />
    );
    expect(screen.getByText("Operations record release")).toBeInTheDocument();
  });
});
