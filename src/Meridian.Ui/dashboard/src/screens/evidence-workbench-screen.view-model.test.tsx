import { act, cleanup, render, renderHook, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ApiError } from "@/lib/api-errors";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  validateEvidencePacket
} from "@/lib/api";
import { EvidenceWorkbenchScreen } from "@/screens/evidence-workbench-screen";
import {
  buildEvidenceLineageDetail,
  buildEvidenceLineagePanel,
  buildEvidenceNodeDetail,
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
  artifactRefs: [
    {
      artifactId: "artifact-run-detail",
      kind: "json",
      path: "artifacts/evidence/strategy-run/run-1/detail.json",
      route: null,
      generatedAt: "2026-05-09T12:01:00Z",
      hash: "sha256:run-detail",
      retained: true
    }
  ],
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
    expect(vm.generatedLabel).toBe("May 9, 12:30 UTC");
    expect(vm.missingEvidence).toEqual(["strategy-run:run-1:ledger"]);
    expect(vm.staleEvidence).toEqual([staleNode.evidenceId]);
    expect(vm.relatedWorkItemIds).toEqual(["provider-trust:sample-review"]);
    expect(vm.nodeGroups.map((group) => group.id)).toEqual(["run-lifecycle", "readiness", "provider-trust"]);
    expect(vm.nodeGroups[0]).toMatchObject({
      tableLabel: "Run Lifecycle evidence nodes",
      detailPanelId: "evidence-node-run-lifecycle-selected-detail",
      defaultSelectedNodeId: readyNode.evidenceId,
      summaryLabel: "1 node; select a row to inspect retained artifacts, freshness, and work items."
    });
    expect(vm.nodeGroups[0].rows[0]).toMatchObject({
      kindLabel: "Strategy Run Detail",
      statusLabel: "Ready",
      freshnessLabel: "Fresh as of May 9, 12:00 UTC",
      artifactCountLabel: "1 artifact",
      workItemCountLabel: "0 work items",
      selectAriaLabel: "Inspect evidence node Strategy Run Detail strategy-run:run-1:detail"
    });
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
    expect(vm.exportResultDetail).toBeNull();
    expect(vm.lineagePanel).toMatchObject({
      hasRows: true,
      defaultSelectedRowId: "strategy-run:run-1:detail:requires:strategy-run:run-1:replay:0",
      detailPanelId: "evidence-lineage-selected-edge-detail",
      summaryLabel: "1 edge",
      tableLabel: "Evidence lineage edges for Momentum strategy run"
    });
    expect(vm.lineagePanel.rows[0]).toMatchObject({
      relationshipLabel: "Requires",
      ariaLabel: "Requires from strategy-run:run-1:detail to strategy-run:run-1:replay. Replay evidence supports the run.",
      selectAriaLabel: "Inspect lineage edge: Requires from strategy-run:run-1:detail to strategy-run:run-1:replay"
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
      error: { summary: "Evidence API unavailable", details: [] },
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

  it("preserves operating scope in source-workflow, subject, and packet-action links", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      operatingScope: {
        label: "Operating scope",
        summary: "Subject: MSFT / Account: fund-1 / Run: run-1 / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15",
        subjectSymbol: "MSFT",
        fundAccountId: "fund-1",
        runId: "run-1",
        provider: "Alpaca",
        hasScope: true,
        clearAriaLabel: "Clear operating scope",
        items: [],
        queryParams: [
          { key: "symbol", value: "MSFT", scopeKey: "symbol" },
          { key: "fundAccountId", value: "fund-1", scopeKey: "fundAccountId" },
          { key: "runId", value: "run-1", scopeKey: "runId" },
          { key: "provider", value: "Alpaca", scopeKey: "provider" },
          { key: "from", value: "2026-05-01", scopeKey: "window" },
          { key: "to", value: "2026-05-15", scopeKey: "window" }
        ]
      },
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

    expect(vm.sourceWorkflowHref).toBe("/strategy?runId=run-1&symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15");
    expect(vm.openSubjectHref(subject))
      .toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15");
    expect(vm.packetActions[0]).toMatchObject({
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
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
      expect.objectContaining({
        id: "run-lifecycle",
        readyCount: 1,
        reviewCount: 0,
        tableLabel: "Run Lifecycle evidence nodes",
        hasRows: true,
        defaultSelectedNodeId: readyNode.evidenceId
      }),
      expect.objectContaining({
        id: "provider-trust",
        readyCount: 0,
        reviewCount: 1,
        tableLabel: "Provider Trust evidence nodes",
        hasRows: true,
        defaultSelectedNodeId: blockedNode.evidenceId
      })
    ]);

    expect(groups[1].rows[0]).toMatchObject({
      kindLabel: "Provider Trust",
      statusLabel: "Review Required",
      workItemCountLabel: "1 work item"
    });
    expect(buildEvidenceNodeDetail(groups[0].rows[0])).toMatchObject({
      eyebrow: "Selected evidence node",
      title: "Strategy Run Detail",
      subtitle: readyNode.evidenceId,
      artifactRows: [
        expect.objectContaining({
          kind: "Json",
          target: "artifacts/evidence/strategy-run/run-1/detail.json",
          retainedLabel: "Retained",
          hashLabel: "sha256:run-detail"
        })
      ],
      workItemEmptyText: "No related operator work items are attached to this node."
    });
  });

  it("builds accessible empty and populated lineage presentation state", () => {
    const emptyPanel = buildEvidenceLineagePanel([], subject);

    expect(emptyPanel).toMatchObject({
      hasRows: false,
      defaultSelectedRowId: null,
      detailPanelId: "evidence-lineage-selected-edge-detail",
      summaryLabel: "0 edges",
      tableLabel: "Evidence lineage edges for Momentum strategy run",
      emptyTitle: "No lineage edges",
      emptyRole: "status",
      emptyAriaLive: "polite"
    });

    const populatedPanel = buildEvidenceLineagePanel([
      {
        fromId: "provider-trust:sample-review",
        relationship: "blocks_readiness",
        toId: "strategy-run:run-1:promotion",
        reason: "Provider evidence must be reviewed first."
      }
    ], subject);

    expect(populatedPanel.hasRows).toBe(true);
    expect(populatedPanel.rows[0]).toMatchObject({
      relationshipLabel: "Blocks Readiness",
      reason: "Provider evidence must be reviewed first."
    });
    expect(buildEvidenceLineageDetail(populatedPanel.rows[0])).toMatchObject({
      eyebrow: "Selected lineage edge",
      title: "Blocks Readiness",
      subtitle: "provider-trust:sample-review to strategy-run:run-1:promotion",
      description: "Provider evidence must be reviewed first.",
      fields: [
        { label: "From node", value: "provider-trust:sample-review" },
        { label: "Relationship", value: "Blocks Readiness" },
        { label: "To node", value: "strategy-run:run-1:promotion" },
        {
          label: "Edge ID",
          value: "provider-trust:sample-review:blocks_readiness:strategy-run:run-1:promotion:0"
        }
      ]
    });
  });

  it("serializes validation and export commands for the selected evidence packet", () => {
    const validating = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: false,
      exportResult: null,
      validateBusy: true,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(validating.validateCommand).toMatchObject({
      busy: true,
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });
    expect(validating.exportCommand).toMatchObject({
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });
    expect(validating.packetActions.find((action) => action.control === "export")).toMatchObject({
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });

    const exporting = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: true,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(exporting.exportCommand).toMatchObject({
      busy: true,
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
    expect(exporting.validateCommand).toMatchObject({
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
    expect(exporting.packetActions.find((action) => action.control === "validate")).toMatchObject({
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
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
    const validationCalls = services.validatePacket.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(validationCalls[0]?.[2]?.signal?.aborted).toBe(true);

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
    const validationCalls = services.validatePacket.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(validationCalls[0]?.[2]?.signal?.aborted).toBe(true);
    expect(validationCalls[1]?.[2]?.signal?.aborted).toBe(false);

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
    const exportCalls = services.exportManifest.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(exportCalls[0]?.[2]?.signal?.aborted).toBe(true);
    expect(exportCalls[1]?.[2]?.signal?.aborted).toBe(false);

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
    expect(result.current.exportResultDetail).toMatchObject({
      title: "Manifest retained",
      manifestPath: "fresh-manifest.json",
      summaryLabel: "3 nodes, 1 warning",
      routeHref: "/workstation/evidence/fresh-manifest.json",
      routeLabel: "Open manifest",
      routeAriaLabel: "Open retained evidence manifest at fresh-manifest.json"
    });
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
    expect(screen.getByRole("table", { name: "Run Lifecycle evidence nodes" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Selected evidence node: Strategy Run Detail" })).toHaveTextContent(
      "Run detail is available."
    );
    expect(screen.getByText("artifacts/evidence/strategy-run/run-1/detail.json")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: /evidence lineage edges for momentum strategy run/i })).toBeInTheDocument();
    expect(screen.getAllByText("Requires").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("region", { name: "Selected lineage edge: Requires" })).toHaveTextContent(
      "Replay evidence supports the run."
    );
    expect(screen.getByRole("link", { name: /open evidence packet for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );

    await user.click(screen.getByRole("button", { name: /validate selected evidence packet for momentum strategy run/i }));
    expect(await screen.findByText(/Validation returned 50% completeness/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export selected evidence manifest for momentum strategy run/i }));
    expect(await screen.findByText("Manifest retained")).toBeInTheDocument();
    expect(screen.getByText("workstation/evidence/strategy-run/run-1/manifest.json")).toBeInTheDocument();
    expect(screen.getByText("3 nodes, 1 warning")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open retained evidence manifest/i })).toHaveAttribute(
      "href",
      "/workstation/evidence/strategy-run/run-1/manifest.json"
    );
  });

  it("lets keyboard and pointer users inspect lineage edge detail", async () => {
    const packetWithTwoEdges: EvidencePacket = {
      ...packet,
      edges: [
        ...packet.edges,
        {
          fromId: blockedNode.evidenceId,
          toId: "strategy-run:run-1:promotion",
          relationship: "blocks_readiness",
          reason: "Provider evidence must be reviewed first."
        }
      ]
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packetWithTwoEdges);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    const secondEdge = await screen.findByRole("row", {
      name: /inspect lineage edge: blocks readiness from strategy-run:run-1:provider-trust/i
    });
    expect(secondEdge).toHaveAttribute("aria-controls", "evidence-lineage-selected-edge-detail");
    expect(secondEdge).toHaveAttribute("aria-expanded", "false");

    await user.click(secondEdge);

    expect(secondEdge).toHaveAttribute("aria-selected", "true");
    expect(secondEdge).toHaveAttribute("aria-expanded", "true");
    const detail = screen.getByRole("region", { name: "Selected lineage edge: Blocks Readiness" });
    expect(detail).toHaveTextContent("Provider evidence must be reviewed first.");
    expect(detail).toHaveTextContent("strategy-run:run-1:provider-trust");
  });

  it("lets keyboard and pointer users inspect evidence node detail", async () => {
    const promotionNode: EvidenceNode = {
      ...readyNode,
      evidenceId: "strategy-run:run-1:promotion",
      kind: "promotion-review",
      status: "ReviewRequired",
      summary: "Promotion review is waiting on provider trust evidence.",
      artifactRefs: [],
      relatedWorkItemIds: ["promotion:review"]
    };
    const packetWithTwoRunNodes: EvidencePacket = {
      ...packet,
      nodes: [readyNode, promotionNode, staleNode, blockedNode]
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packetWithTwoRunNodes);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    const promotionRow = await screen.findByRole("row", {
      name: /inspect evidence node promotion review strategy-run:run-1:promotion/i
    });
    expect(promotionRow).toHaveAttribute("aria-controls", "evidence-node-run-lifecycle-selected-detail");
    expect(promotionRow).toHaveAttribute("aria-expanded", "false");

    promotionRow.focus();
    await user.keyboard("{Enter}");

    expect(promotionRow).toHaveAttribute("aria-selected", "true");
    expect(promotionRow).toHaveAttribute("aria-expanded", "true");
    const promotionDetail = screen.getByRole("region", { name: "Selected evidence node: Promotion Review" });
    expect(promotionDetail).toHaveTextContent("Promotion review is waiting on provider trust evidence.");
    expect(promotionDetail).toHaveTextContent("promotion:review");

    const readyRow = screen.getByRole("row", {
      name: /inspect evidence node strategy run detail strategy-run:run-1:detail/i
    });
    await user.click(readyRow);

    expect(readyRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Selected evidence node: Strategy Run Detail" })).toHaveTextContent(
      "artifacts/evidence/strategy-run/run-1/detail.json"
    );
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
      .mockRejectedValueOnce(new ApiError({
        path: "/api/workstation/evidence/subjects",
        status: 503,
        detail: "Evidence API unavailable",
        responseBody: "Evidence API unavailable"
      }))
      .mockResolvedValueOnce([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByRole("alert")).toHaveTextContent("Evidence API unavailable");
    expect(screen.getByText("Endpoint returned 503 for /api/workstation/evidence/subjects.")).toBeInTheDocument();
    expect(screen.queryByText("No evidence subjects returned")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /retry loading evidence subjects/i }));

    expect(await screen.findByRole("link", { name: /Momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
    await waitFor(() => expect(getEvidenceSubjects).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });

  it("keeps operating scope in evidence workbench links rendered from the route", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open source workflow for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/strategy?runId=run-1&symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );
    expect(screen.getByRole("link", { name: /open evidence packet for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );
  });

  it("renders structured validation errors for failed manifest export", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    vi.mocked(exportEvidenceManifest).mockRejectedValue(
      new ApiError({
        path: "/api/workstation/evidence/strategy-run/run-1/export-manifest",
        status: 422,
        detail: "One or more validation errors occurred.",
        validationIssues: [
          {
            field: "includeWarnings",
            label: "includeWarnings",
            messages: ["Manifest-only export must keep warnings enabled."]
          }
        ],
        responseBody: "{\"detail\":\"One or more validation errors occurred.\"}"
      })
    );
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export selected evidence manifest for momentum strategy run/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("One or more validation errors occurred.");
    expect(screen.getByText("Endpoint returned 422 for /api/workstation/evidence/strategy-run/run-1/export-manifest.")).toBeInTheDocument();
    expect(screen.getByText("includeWarnings: Manifest-only export must keep warnings enabled.")).toBeInTheDocument();
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
