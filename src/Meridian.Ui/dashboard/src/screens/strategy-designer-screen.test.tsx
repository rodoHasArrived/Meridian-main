import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import {
  getStrategyDesignerDraft,
  getStrategyDesignerDrafts,
  getStrategyDesignerTemplates,
  listEvidenceVaultDocuments,
  runStrategyDesignerBacktest,
  saveStrategyDesignerDraft
} from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import type { StrategyDesignDocument } from "@/types";

vi.mock("@/lib/api", () => ({
  getStrategyDesignerDraft: vi.fn(),
  getStrategyDesignerDrafts: vi.fn(),
  getStrategyDesignerTemplates: vi.fn(),
  listEvidenceVaultDocuments: vi.fn(),
  runStrategyDesignerBacktest: vi.fn(),
  saveStrategyDesignerDraft: vi.fn()
}));

const template: StrategyDesignDocument = {
  documentId: "price-template",
  name: "Equity price filter",
  description: "Daily price filter",
  version: "1",
  datasetReference: "provider-bars/equities/daily",
  universe: ["TESTX"],
  cells: [
    { cellId: "price-filter", label: "Filter", kind: "visual", purpose: "filter", source: "PRICE > 100",
      fieldRefs: ["PRICE"] },
    { cellId: "fixed-shares", label: "Buy", kind: "trade", purpose: "trade", source: "Buy",
      fieldRefs: [], parameters: { instrument: "Equity", direction: "Buy", sizingMethod: "FixedShares", sizingValue: "10" } }
  ],
  transitions: [],
  metadata: { proof: "retain this" },
  createdAt: "2024-01-02T00:00:00Z",
  updatedAt: "2024-01-02T00:00:00Z"
};
const validation = { isValid: true, summary: "Valid", messages: [] };

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getStrategyDesignerDrafts).mockResolvedValue([]);
  vi.mocked(getStrategyDesignerTemplates).mockResolvedValue([]);
  vi.mocked(listEvidenceVaultDocuments).mockResolvedValue([]);
});
import { StrategyDesignerScreen } from "@/screens/strategy-designer-screen";

describe("StrategyDesignerScreen", () => {
  it("reserves the primary desktop width for the cell authoring canvas", () => {
    render(<StrategyDesignerScreen />);

    expect(screen.getByTestId("strategy-designer-primary-workbench"))
      .toHaveClass("xl:grid-cols-[280px_minmax(0,1fr)]");
  });

  it("keeps proof and analysis secondary until requested", async () => {
    const user = userEvent.setup();
    render(<StrategyDesignerScreen />);

    const proofDisclosure = screen.getByText("Templates and backtest proof").closest("details");
    const analysisDisclosure = screen.getByText("Transition and payoff analysis").closest("details");
    expect(proofDisclosure).not.toHaveAttribute("open");
    expect(analysisDisclosure).not.toHaveAttribute("open");

    await user.click(screen.getByText("Templates and backtest proof"));
    expect(proofDisclosure).toHaveAttribute("open");

    const panel = screen.getByTestId("strategy-builder-backtest-proof");

    expect(
      within(panel).getByRole("link", {
        name: "GET /api/workstation/strategy/designer/templates for Templates"
      })
    ).toHaveAttribute("href", "/api/workstation/strategy/designer/templates");

    expect(
      within(panel).getByRole("link", {
        name: "GET /api/workstation/strategy/designer/field-catalog for Field catalog"
      })
    ).toHaveAttribute("href", "/api/workstation/strategy/designer/field-catalog");

    expect(
      within(panel).queryByRole("link", {
        name: "POST /api/workstation/strategy/designer/run-backtest for Run backtest"
      })
    ).toBeNull();

    expect(
      within(panel).getByRole("group", {
        name: "Reference-only POST /api/workstation/strategy/designer/run-backtest for Run backtest"
      })
    ).toHaveTextContent("Reference");
  });

  it("makes draft saving available and explains the backtest prerequisite", () => {
    render(<StrategyDesignerScreen />);

    expect(screen.getByRole("button", { name: "Save strategy design draft" })).toBeEnabled();
    const run = screen.getByRole("button", { name: "Run backtest proof" });
    expect(run).toBeDisabled();
    expect(run).toHaveAccessibleDescription("Load the equity price filter template for this backtest.");
  });

  it("saves and reopens the exact revision, blocks stale saves, and opens evidence from a retained run", async () => {
    const user = userEvent.setup();
    const account = "da6b82e6-2dd9-4733-8e77-a079e25be637";
    const evidence = "evidence://evidence-vault/ev-0123456789abcdef01234567";
    vi.mocked(getStrategyDesignerTemplates).mockResolvedValue([{
      templateId: "equity-price-filter", name: template.name, description: template.description,
      category: "Equity", sourcePrototype: "Designer", tags: [], document: template
    }]);
    vi.mocked(saveStrategyDesignerDraft).mockImplementationOnce(async ({ document }) => {
      const saved = { ...document, revision: "revision-1" };
      vi.mocked(getStrategyDesignerDraft).mockResolvedValue(saved);
      return {
        document: saved,
        summary: { ...saved, cellCount: 2, transitionCount: 0, validationSummary: "Valid" },
        validation, trace: []
      };
    }).mockRejectedValueOnce(new ApiError({
      path: "/api/workstation/strategy/designer/drafts", status: 409,
      detail: "The saved draft changed. Reopen the latest revision before saving."
    }));
    vi.mocked(runStrategyDesignerBacktest).mockImplementation(async ({ document }) => ({
      success: true, runId: "retained-designer-run", strategyId: document.documentId, strategyName: document.name,
      validation, compiled: { source: "", datasetFingerprint: "", fieldRefs: [], disabledFieldRefs: [] },
      trace: [], previewRows: [], metrics: { "Net P&L": "19" }, runtimeError: null,
      promotionCandidatePath: null, reviewPacketPath: null, dataProvenanceToken: "simulated",
      strategyVersionSha256: "version-1", dataSnapshotSha256: "dataset-1"
    }));

    render(<MemoryRouter><StrategyDesignerScreen fundAccountId={account} /></MemoryRouter>);
    await user.click(screen.getByText("Templates and backtest proof"));
    await user.click(await screen.findByRole("button", { name: "New equity price filter draft" }));
    await user.click(screen.getByRole("button", { name: "Save strategy design draft" }));
    expect(await screen.findByText("Draft saved.")).toBeInTheDocument();
    const savedId = vi.mocked(saveStrategyDesignerDraft).mock.calls[0][0].document.documentId;

    await user.click(screen.getByRole("button", { name: "New equity price filter draft" }));
    await user.selectOptions(screen.getByLabelText("Open saved draft"), savedId);
    expect(await screen.findByText("Saved draft opened.")).toBeInTheDocument();
    const run = screen.getByRole("button", { name: "Run backtest proof" });
    expect(run).toBeDisabled();

    await user.type(screen.getByLabelText("From date"), "2024-01-02");
    await user.type(screen.getByLabelText("To date"), "2024-01-05");
    await user.type(screen.getByLabelText("Acceptance criterion"), "One fill with an independently verified cost.");
    await user.type(screen.getByLabelText("Evidence Vault reference"), evidence);
    await user.click(run);
    const link = await screen.findByRole("link", { name: "Open run evidence" });
    expect(link).toHaveAttribute("href", expect.stringContaining("subjectId=retained-designer-run"));
    expect(screen.getByText("Simulated")).toBeInTheDocument();
    expect(runStrategyDesignerBacktest).toHaveBeenCalledWith(expect.objectContaining({
      document: expect.objectContaining({ revision: "revision-1", metadata: { proof: "retain this" } }),
      fundAccountId: account, retainedEvidenceReferences: [evidence]
    }));

    await user.click(screen.getByRole("button", { name: "Save strategy design draft" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Reopen the latest revision");
    expect(saveStrategyDesignerDraft).toHaveBeenLastCalledWith(expect.objectContaining({ expectedRevision: "revision-1" }));
  });
});
