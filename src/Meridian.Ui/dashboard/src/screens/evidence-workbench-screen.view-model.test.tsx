import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  validateEvidencePacket
} from "@/lib/api";
import { EvidenceWorkbenchScreen } from "@/screens/evidence-workbench-screen";
import {
  buildEvidenceWorkbenchViewModel,
  groupNodes,
  mapStatusTone
} from "@/screens/evidence-workbench-screen.view-model";
import type {
  EvidenceCompleteness,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceSubject
} from "@/types";

vi.mock("@/lib/api", () => ({
  getEvidenceSubjects: vi.fn(),
  getEvidencePacket: vi.fn(),
  validateEvidencePacket: vi.fn(),
  exportEvidenceManifest: vi.fn()
}));

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
});

const subject: EvidenceSubject = {
  subjectId: "run-1",
  subjectKind: "strategy-run",
  label: "Momentum strategy run",
  workspace: "Strategy",
  route: "/strategy?runId=run-1",
  pageTag: "StrategyRuns"
};

const readyNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:detail",
  subject,
  kind: "strategy-run-detail",
  status: "Ready",
  freshness: { asOf: "2026-05-09T12:00:00Z", isStale: false, reason: null },
  sourceSystem: "StrategyRunReadService",
  summary: "Run detail is available.",
  artifactRefs: [],
  relatedWorkItemIds: []
};

const staleNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:replay",
  subject,
  kind: "paper-replay",
  status: "Stale",
  freshness: { asOf: "2026-04-30T12:00:00Z", isStale: true, reason: "Evidence is older than seven days." },
  sourceSystem: "TradingOperatorReadinessService",
  summary: "Replay verification is stale.",
  artifactRefs: [],
  relatedWorkItemIds: []
};

const blockedNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:provider-trust",
  subject,
  kind: "provider-trust",
  status: "ReviewRequired",
  freshness: { asOf: "2026-05-09T12:00:00Z", isStale: false, reason: null },
  sourceSystem: "Dk1TrustGateReadinessService",
  summary: "Provider sample review is pending.",
  artifactRefs: [],
  relatedWorkItemIds: ["provider-trust:sample-review", "provider-trust:sample-review"]
};

const completeness: EvidenceCompleteness = {
  score: 33,
  status: "Blocked",
  requiredIds: [readyNode.evidenceId, staleNode.evidenceId, blockedNode.evidenceId, "strategy-run:run-1:ledger"],
  readyIds: [readyNode.evidenceId],
  missingIds: ["strategy-run:run-1:ledger"],
  staleIds: [staleNode.evidenceId],
  blockingWorkItemIds: ["provider-trust:sample-review"]
};

const packet: EvidencePacket = {
  subject,
  generatedAt: "2026-05-09T12:30:00Z",
  nodes: [readyNode, staleNode, blockedNode],
  edges: [
    {
      fromId: readyNode.evidenceId,
      toId: staleNode.evidenceId,
      relationship: "requires",
      reason: "Replay evidence supports the run."
    }
  ],
  completeness,
  actions: [],
  warnings: ["DK1 sample review is pending."]
};

describe("Evidence Workbench view model", () => {
  it("groups evidence by lifecycle stage and surfaces blocked packet gaps", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.title).toBe("Momentum strategy run");
    expect(vm.scoreLabel).toBe("33% complete");
    expect(vm.statusTone).toBe("danger");
    expect(vm.missingEvidence).toEqual(["strategy-run:run-1:ledger"]);
    expect(vm.staleEvidence).toEqual([staleNode.evidenceId]);
    expect(vm.relatedWorkItemIds).toEqual(["provider-trust:sample-review"]);
    expect(vm.nodeGroups.map((group) => group.id)).toEqual(["run-lifecycle", "readiness", "provider-trust"]);
  });

  it("keeps loading and empty subject states explicit", () => {
    const loading = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: null,
      selectedSubjectId: null,
      loading: true,
      error: null,
      subjects: [],
      packet: null,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(loading.loading).toBe(true);
    expect(loading.hasSelection).toBe(false);
    expect(loading.hasSubjects).toBe(false);
    expect(loading.loadingLabel).toBe("Loading evidence subjects.");
    expect(loading.subjectsSummaryLabel).toBe("0 subjects");
    expect(loading.subjectEmptyTitle).toBe("No evidence subjects returned");
    expect(loading.subjectEmptyActionHref).toBe("/trading/readiness");
    expect(loading.title).toBe("Evidence Workbench");
    expect(loading.openSubjectHref(subject)).toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");
  });

  it("maps ready, review, blocked, missing, stale, and unknown statuses to accessible tones", () => {
    expect(mapStatusTone("Ready")).toBe("success");
    expect(mapStatusTone("ReviewRequired")).toBe("warning");
    expect(mapStatusTone("Stale")).toBe("warning");
    expect(mapStatusTone("Blocked")).toBe("danger");
    expect(mapStatusTone("Missing")).toBe("danger");
    expect(mapStatusTone("Unknown")).toBe("muted");
  });

  it("counts ready and review evidence inside stage groups", () => {
    const groups = groupNodes([readyNode, blockedNode]);

    expect(groups).toEqual([
      expect.objectContaining({ id: "run-lifecycle", readyCount: 1, reviewCount: 0 }),
      expect.objectContaining({ id: "provider-trust", readyCount: 0, reviewCount: 1 })
    ]);
  });
});

describe("EvidenceWorkbenchScreen", () => {
  it("renders subject query route, validation result, and manifest export result", async () => {
    const validation: EvidenceCompleteness = { ...completeness, score: 50 };
    const exportResponse: EvidencePacketExportResponse = {
      subjectKind: "strategy-run",
      subjectId: "run-1",
      generatedAt: "2026-05-09T12:35:00Z",
      manifestPath: "workstation/evidence/strategy-run/run-1/manifest.json",
      manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
      evidenceCount: 3,
      warningCount: 1,
      retained: true
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    vi.mocked(validateEvidencePacket).mockResolvedValue(validation);
    vi.mocked(exportEvidenceManifest).mockResolvedValue(exportResponse);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    expect(screen.getByText("Missing evidence")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /validate/i }));
    expect(await screen.findByText(/Validation returned 50% completeness/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export manifest/i }));
    expect(await screen.findByText("Manifest retained")).toBeInTheDocument();
    expect(screen.getByText("workstation/evidence/strategy-run/run-1/manifest.json")).toBeInTheDocument();
  });

  it("renders broad subject selection route without loading a packet", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByRole("link", { name: /Momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });

  it("renders recoverable empty subject guidance when no subjects are available", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByText("No evidence subjects returned")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open readiness console/i })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });
});

function renderEvidenceRoute(initialEntry: string) {
  render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/reporting/evidence" element={<EvidenceWorkbenchScreen />} />
      </Routes>
    </MemoryRouter>
  );
}
