import { act, cleanup, render, renderHook, screen, waitFor } from "@testing-library/react";
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
  mapStatusTone,
  useEvidenceWorkbenchViewModel
} from "@/screens/evidence-workbench-screen.view-model";
import type {
  EvidenceCompleteness,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceSubject,
  WorkflowAction
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

const evidenceActions: WorkflowAction[] = [
  {
    actionId: "workflow.evidence.open-packet",
    label: "Open Evidence Packet",
    detail: "Open the reusable evidence packet for the selected workflow subject.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Primary",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.evidence.validate",
    label: "Validate Evidence",
    detail: "Validate evidence completeness without mutating source workflows.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Warning",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.evidence.export-manifest",
    label: "Export Evidence Manifest",
    detail: "Write a manifest-only evidence export for audit review.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Primary",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  }
];

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
  actions: evidenceActions,
  warnings: ["DK1 sample review is pending."]
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

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
    expect(vm.sourceWorkflowHref).toBe("/strategy?runId=run-1");
    expect(vm.packetActionsSummaryLabel).toBe("3 workflow actions");
    expect(vm.packetActions.map((action) => action.control)).toEqual(["link", "validate", "export"]);
    expect(vm.packetActions[0]).toMatchObject({
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1",
      tone: "primary",
      targetLabel: "Evidence Workbench"
    });
    expect(vm.packetActions[1]).toMatchObject({
      commandLabel: "Validate",
      ariaLabel: "Validate evidence for Momentum strategy run",
      tone: "warning"
    });
    expect(vm.validateCommand).toMatchObject({
      label: "Validate",
      ariaLabel: "Validate selected evidence packet for Momentum strategy run",
      disabled: false,
      disabledReason: null
    });
    expect(vm.exportCommand).toMatchObject({
      label: "Export manifest",
      ariaLabel: "Export selected evidence manifest for Momentum strategy run",
      disabled: false,
      disabledReason: null
    });
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
    expect(loading.showSubjectPicker).toBe(true);
    expect(loading.hasSelection).toBe(false);
    expect(loading.hasSubjects).toBe(false);
    expect(loading.loadingLabel).toBe("Loading evidence subjects.");
    expect(loading.subjectsSummaryLabel).toBe("0 subjects");
    expect(loading.subjectEmptyTitle).toBe("No evidence subjects returned");
    expect(loading.subjectEmptyActionHref).toBe("/trading/readiness");
    expect(loading.title).toBe("Evidence Workbench");
    expect(loading.reloadCommand).toMatchObject({
      label: "Retrying",
      ariaLabel: "Retry loading evidence subjects",
      busy: true,
      disabled: true,
      disabledReason: "Evidence load is already running."
    });
    expect(loading.openSubjectHref(subject)).toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");
  });

  it("keeps load failures recoverable without rendering them as empty evidence", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: null,
      selectedSubjectId: null,
      loading: false,
      error: "Evidence API unavailable",
      subjects: [],
      packet: null,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.showSubjectPicker).toBe(false);
    expect(vm.subjectEmptyTitle).toBe("No evidence subjects returned");
    expect(vm.reloadCommand).toMatchObject({
      label: "Retry",
      ariaLabel: "Retry loading evidence subjects",
      busy: false,
      disabled: false,
      disabledReason: null
    });
  });

  it("falls back to page-tag routing when evidence subjects omit a direct route", () => {
    const packetWithoutRoute: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: null,
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "report-pack",
      selectedSubjectId: "close-pack",
      loading: false,
      error: null,
      subjects: [packetWithoutRoute.subject],
      packet: packetWithoutRoute,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.sourceWorkflowHref).toBe("/reporting/report-packs");
    expect(vm.sourceWorkflowAriaLabel).toBe("Open source workflow for Momentum strategy run");
  });

  it("normalizes subject routes to canonical workstation paths before falling back to page tags", () => {
    const legacyRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "/workstation/governance/reconciliation?runId=run-1#break-7",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const unsafeRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "https://example.test/workstation/strategy",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const apiRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "/api/workstation/evidence/strategy-run/run-1",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };

    const build = (nextPacket: EvidencePacket) => buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: nextPacket.subject.subjectKind,
      selectedSubjectId: nextPacket.subject.subjectId,
      loading: false,
      error: null,
      subjects: [nextPacket.subject],
      packet: nextPacket,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(build(legacyRoutePacket).sourceWorkflowHref).toBe("/accounting/reconciliation?runId=run-1#break-7");
    expect(build(unsafeRoutePacket).sourceWorkflowHref).toBe("/reporting/report-packs");
    expect(build(apiRoutePacket).sourceWorkflowHref).toBe("/reporting/report-packs");
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

  it("ignores stale validation results after the selected subject changes", async () => {
    const validation = createDeferred<EvidenceCompleteness>();
    const nextSubject: EvidenceSubject = {
      ...subject,
      subjectId: "run-2",
      label: "Rebalanced strategy run"
    };
    const nextPacket: EvidencePacket = {
      ...packet,
      subject: nextSubject,
      completeness: { ...completeness, score: 80 }
    };
    const services = {
      getSubjects: vi.fn().mockResolvedValue([subject, nextSubject]),
      getPacket: vi.fn(async (_subjectKind: string, subjectId: string) => subjectId === "run-2" ? nextPacket : packet),
      validatePacket: vi.fn(() => validation.promise),
      exportManifest: vi.fn()
    };
    const { result, rerender } = renderHook(
      ({ search }) => useEvidenceWorkbenchViewModel(search, services),
      { initialProps: { search: "?subjectKind=strategy-run&subjectId=run-1" } }
    );

    await waitFor(() => expect(result.current.hasPacket).toBe(true));

    await act(async () => {
      void result.current.validatePacket();
    });

    expect(result.current.validateBusy).toBe(true);

    rerender({ search: "?subjectKind=strategy-run&subjectId=run-2" });

    await waitFor(() => expect(result.current.selectedSubjectId).toBe("run-2"));
    await waitFor(() => expect(result.current.hasPacket).toBe(true));
    expect(result.current.validateBusy).toBe(false);

    await act(async () => {
      validation.resolve({ ...completeness, score: 50 });
      await validation.promise;
    });

    expect(result.current.selectedSubjectId).toBe("run-2");
    expect(result.current.title).toBe("Rebalanced strategy run");
    expect(result.current.validationResult).toBeNull();
    expect(result.current.scoreLabel).toBe("80% complete");
  });

  it("keeps only the newest same-subject validation and export command results", async () => {
    const firstValidation = createDeferred<EvidenceCompleteness>();
    const secondValidation = createDeferred<EvidenceCompleteness>();
    const firstExport = createDeferred<EvidencePacketExportResponse>();
    const secondExport = createDeferred<EvidencePacketExportResponse>();
    const services = {
      getSubjects: vi.fn().mockResolvedValue([subject]),
      getPacket: vi.fn().mockResolvedValue(packet),
      validatePacket: vi.fn()
        .mockReturnValueOnce(firstValidation.promise)
        .mockReturnValueOnce(secondValidation.promise),
      exportManifest: vi.fn()
        .mockReturnValueOnce(firstExport.promise)
        .mockReturnValueOnce(secondExport.promise)
    };
    const { result } = renderHook(
      ({ search }) => useEvidenceWorkbenchViewModel(search, services),
      { initialProps: { search: "?subjectKind=strategy-run&subjectId=run-1" } }
    );

    await waitFor(() => expect(result.current.hasPacket).toBe(true));

    await act(async () => {
      void result.current.validatePacket();
      void result.current.validatePacket();
    });
    expect(services.validatePacket).toHaveBeenCalledTimes(2);

    await act(async () => {
      firstValidation.resolve({ ...completeness, score: 41 });
      await Promise.resolve();
    });
    expect(result.current.validationResult).toBeNull();
    expect(result.current.validateBusy).toBe(true);

    await act(async () => {
      secondValidation.resolve({ ...completeness, score: 91 });
      await secondValidation.promise;
    });
    expect(result.current.validationResult?.score).toBe(91);
    expect(result.current.validateBusy).toBe(false);

    await act(async () => {
      void result.current.exportManifest();
      void result.current.exportManifest();
    });
    expect(services.exportManifest).toHaveBeenCalledTimes(2);

    await act(async () => {
      firstExport.resolve({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        generatedAt: "2026-05-09T12:35:00Z",
        manifestPath: "stale-manifest.json",
        manifestRoute: "/workstation/evidence/stale-manifest.json",
        evidenceCount: 1,
        warningCount: 0,
        retained: true
      });
      await Promise.resolve();
    });
    expect(result.current.exportResult).toBeNull();
    expect(result.current.exportBusy).toBe(true);

    await act(async () => {
      secondExport.resolve({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        generatedAt: "2026-05-09T12:36:00Z",
        manifestPath: "fresh-manifest.json",
        manifestRoute: "/workstation/evidence/fresh-manifest.json",
        evidenceCount: 3,
        warningCount: 1,
        retained: true
      });
      await secondExport.promise;
    });
    expect(result.current.exportResult?.manifestPath).toBe("fresh-manifest.json");
    expect(result.current.exportBusy).toBe(false);
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
    expect(screen.getByRole("link", { name: /open source workflow for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/strategy?runId=run-1"
    );
    expect(screen.getByRole("region", { name: "Evidence packet actions" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open evidence packet for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );

    await user.click(screen.getByRole("button", { name: /validate selected evidence packet for momentum strategy run/i }));
    expect(await screen.findByText(/Validation returned 50% completeness/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export selected evidence manifest for momentum strategy run/i }));
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

  it("lets operators retry a failed subject load without showing empty evidence copy", async () => {
    vi.mocked(getEvidenceSubjects)
      .mockRejectedValueOnce(new Error("Evidence API unavailable"))
      .mockResolvedValueOnce([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByRole("alert")).toHaveTextContent("Evidence API unavailable");
    expect(screen.queryByText("No evidence subjects returned")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /retry loading evidence subjects/i }));

    expect(await screen.findByRole("link", { name: /Momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
    await waitFor(() => expect(getEvidenceSubjects).toHaveBeenCalledTimes(2));
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
