import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup } from "@testing-library/react";
import { EvidenceWorkbenchScreen } from "@/screens/evidence-workbench-screen";
import type {
  EvidenceAssurancePanelViewModel,
  EvidenceLineagePanelViewModel,
  EvidenceProofChainPanelViewModel,
  EvidenceWorkbenchViewModel
} from "@/screens/evidence-workbench-screen.view-model";
import { renderWithRouter } from "@/test/render";

vi.mock("@/screens/evidence-workbench-screen.view-model", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/screens/evidence-workbench-screen.view-model")>();
  return { ...actual, useEvidenceWorkbenchViewModel: vi.fn() };
});

import { useEvidenceWorkbenchViewModel } from "@/screens/evidence-workbench-screen.view-model";

const mockUseVm = vi.mocked(useEvidenceWorkbenchViewModel);

const emptyAssurancePanel: EvidenceAssurancePanelViewModel = {
  title: "Assurance",
  scoreLabel: "100%",
  statusLabel: "Pass",
  statusTone: "success",
  summaryLabel: "Assurance looks good.",
  componentRows: [],
  slaRows: [],
  breachedSlaRows: [],
  orphanEvidenceIds: [],
  orphanSummaryLabel: "No orphans",
  orphanTone: "success",
  noOrphanRuleLabel: "No orphan rule: pass",
  validationIssueLabel: "0 issues"
};

const emptyProofChainPanel: EvidenceProofChainPanelViewModel = {
  title: "Proof chain",
  summaryLabel: "0 layers",
  statusLabel: "No layers",
  statusTone: "muted",
  coverageLabel: "—",
  hasLayers: false,
  rows: []
};

const emptyLineagePanel: EvidenceLineagePanelViewModel = {
  title: "Lineage",
  description: "Lineage graph",
  tableLabel: "Lineage table",
  summaryLabel: "0 edges",
  detailPanelId: "lineage-detail",
  hasRows: false,
  defaultSelectedRowId: null,
  rows: [],
  emptyTitle: "No lineage",
  emptyDetail: "No edges found.",
  emptyRole: "status",
  emptyAriaLive: "polite"
};

const noopCommand = {
  commandLabel: "",
  label: "Reload",
  ariaLabel: "Reload evidence",
  busy: false,
  busyLabel: null,
  disabled: false,
  disabledReason: null
};

function makeVm(overrides: Partial<EvidenceWorkbenchViewModel> = {}): EvidenceWorkbenchViewModel {
  return {
    selectedSubjectKind: null,
    selectedSubjectId: null,
    title: "Evidence workbench",
    subtitle: "Review evidence packets.",
    loading: false,
    error: null,
    showSubjectPicker: false,
    hasSelection: false,
    hasPacket: false,
    hasSubjects: false,
    loadingLabel: "Loading evidence…",
    sourceWorkflowHref: null,
    sourceWorkflowLabel: null,
    sourceWorkflowAriaLabel: null,
    subjectsRegionLabel: "Evidence subjects",
    subjectsSummaryLabel: "0 subjects",
    subjectEmptyTitle: "No subjects",
    subjectEmptyDetail: "No evidence subjects found.",
    subjectEmptyActionLabel: "Go to strategy",
    subjectEmptyActionHref: "/strategy",
    subjectEmptyActionAriaLabel: "Navigate to strategy",
    subjects: [],
    packet: null,
    scoreLabel: "—",
    statusLabel: "No packet",
    statusTone: "muted",
    generatedLabel: "—",
    assurancePanel: emptyAssurancePanel,
    proofChainPanel: emptyProofChainPanel,
    lineagePanel: emptyLineagePanel,
    nodeGroups: [],
    hasPacketActions: false,
    packetActionsLabel: "Actions",
    packetActionsSummaryLabel: "0 actions",
    packetActions: [],
    missingEvidence: [],
    staleEvidence: [],
    orphanEvidence: [],
    slaBreaches: [],
    relatedWorkItemIds: [],
    warnings: [],
    canExport: false,
    reloadCommand: noopCommand,
    validateCommand: { ...noopCommand, label: "Validate", ariaLabel: "Validate packet" },
    exportCommand: { ...noopCommand, label: "Export", ariaLabel: "Export manifest" },
    exportBusy: false,
    exportResult: null,
    exportResultDetail: null,
    validateBusy: false,
    validationResult: null,
    openSubjectHref: () => "/evidence",
    reloadEvidence: vi.fn(),
    exportManifest: vi.fn().mockResolvedValue(undefined),
    validatePacket: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
});

describe("EvidenceWorkbenchScreen", () => {
  it("renders loading state with aria-busy", () => {
    mockUseVm.mockReturnValue(makeVm({ loading: true }));
    renderWithRouter(<EvidenceWorkbenchScreen />);
    const card = screen.getByRole("status");
    expect(card).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Loading evidence…")).toBeInTheDocument();
  });

  it("renders error alert with summary and reload button", () => {
    mockUseVm.mockReturnValue(
      makeVm({
        error: { summary: "Failed to load evidence packet.", details: ["Network timeout"] }
      })
    );
    renderWithRouter(<EvidenceWorkbenchScreen />);
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Failed to load evidence packet.");
    expect(screen.getByText("Network timeout")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reload evidence" })).toBeInTheDocument();
  });

  it("calls reloadEvidence when reload button is clicked", async () => {
    const reloadEvidence = vi.fn();
    mockUseVm.mockReturnValue(
      makeVm({
        error: { summary: "Load failed.", details: [] },
        reloadEvidence
      })
    );
    renderWithRouter(<EvidenceWorkbenchScreen />);
    await userEvent.click(screen.getByRole("button", { name: "Reload evidence" }));
    expect(reloadEvidence).toHaveBeenCalledOnce();
  });

  it("renders subject picker when showSubjectPicker is true with subjects", () => {
    mockUseVm.mockReturnValue(
      makeVm({
        showSubjectPicker: true,
        hasSubjects: true,
        subjectsSummaryLabel: "1 subject",
        subjects: [
          {
            subjectId: "run-1",
            subjectKind: "strategy-run",
            label: "Momentum run",
            workspace: "Strategy",
            route: "/strategy?runId=run-1",
            pageTag: "StrategyRuns"
          }
        ]
      })
    );
    renderWithRouter(<EvidenceWorkbenchScreen />);
    expect(screen.getByText("Evidence subjects")).toBeInTheDocument();
    expect(screen.getByText("Momentum run")).toBeInTheDocument();
    expect(screen.getByText("strategy-run/run-1")).toBeInTheDocument();
  });

  it("renders empty state within subject picker when hasSubjects is false", () => {
    mockUseVm.mockReturnValue(
      makeVm({
        showSubjectPicker: true,
        hasSubjects: false,
        subjectEmptyTitle: "No subjects yet",
        subjectEmptyDetail: "Run a strategy to generate evidence.",
        subjectEmptyActionLabel: "Go to strategy runs"
      })
    );
    renderWithRouter(<EvidenceWorkbenchScreen />);
    expect(screen.getByText("No subjects yet")).toBeInTheDocument();
    expect(screen.getByText("Run a strategy to generate evidence.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to strategy runs" })).toBeInTheDocument();
  });

  it("renders top-level context header with status badge", () => {
    mockUseVm.mockReturnValue(
      makeVm({
        title: "Strategy run #42",
        subtitle: "Completeness review for momentum run.",
        statusLabel: "Ready",
        statusTone: "success",
        scoreLabel: "98%"
      })
    );
    renderWithRouter(<EvidenceWorkbenchScreen />);
    expect(screen.getByText("Strategy run #42")).toBeInTheDocument();
    expect(screen.getByText("Completeness review for momentum run.")).toBeInTheDocument();
    expect(screen.getByText("Ready")).toBeInTheDocument();
    expect(screen.getByText("98%")).toBeInTheDocument();
  });
});
