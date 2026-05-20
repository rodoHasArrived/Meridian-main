import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import * as api from "@/lib/api";
import { OperatorReadinessConsole } from "@/screens/operator-readiness-console";
import { renderWithRouter } from "@/test/render";
import type { OperatorInbox, OperatorWorkItem, TradingWorkspaceResponse } from "@/types";

const inbox: OperatorInbox = {
  asOf: "2026-04-29T12:01:00Z",
  items: [
    {
      workItemId: "promotion-review-run-1",
      kind: "PromotionReview",
      label: "Promotion checklist incomplete",
      detail: "Finish continuity review.",
      tone: "Warning",
      createdAt: "2026-04-29T12:00:00Z",
      runId: "run-1",
      fundAccountId: "fund-1",
      auditReference: "audit-promotion-1",
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "TradingReadinessConsole"
    }
  ],
  criticalCount: 0,
  warningCount: 1,
  reviewCount: 1,
  summary: "1 review item needs attention."
};

const warningItems: OperatorWorkItem[] = Array.from({ length: 6 }, (_, index) => ({
  ...inbox.items[0],
  workItemId: `warning-${index}`,
  kind: "ReportPackApproval",
  label: `Warning item ${index}`,
  detail: `Warning detail ${index}`,
  tone: "Warning",
  createdAt: `2026-04-29T12:0${index}:00Z`,
  workspace: "Reporting",
  targetRoute: "/reporting",
  targetPageTag: "ReportPackApproval"
}));

const criticalItem: OperatorWorkItem = {
  ...inbox.items[0],
  workItemId: "critical-security-gap",
  kind: "SecurityMasterCoverage",
  label: "Critical security coverage gap",
  detail: "Resolve missing identifier coverage before accepting readiness.",
  tone: "Critical",
  createdAt: "2026-04-29T11:00:00Z",
  workspace: "Accounting",
  targetRoute: "/accounting/security-master",
  targetPageTag: "SecurityMaster"
};

const scopedTrading: TradingWorkspaceResponse = {
  metrics: [],
  positions: [],
  openOrders: [],
  fills: [],
  risk: {
    state: "Healthy",
    summary: "Healthy",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    buyingPowerUsed: "0%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-1",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "now",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: "ok"
  },
  readiness: {
    asOf: "2026-04-29T12:00:00Z",
    overallStatus: "Ready",
    readyForPaperOperation: true,
    acceptanceGates: [],
    activeSession: null,
    sessions: [],
    replay: null,
    controls: {
      circuitBreakerOpen: false,
      circuitBreakerReason: null,
      circuitBreakerChangedBy: null,
      circuitBreakerChangedAt: null,
      manualOverrideCount: 0,
      symbolLimitCount: 0,
      defaultMaxPositionSize: 0
    },
    promotion: null,
    trustGate: {
      gateId: "dk1",
      status: "signed",
      readyForOperatorReview: true,
      operatorSignoffRequired: false,
      operatorSignoffStatus: "signed",
      generatedAt: null,
      packetPath: null,
      sourceSummary: null,
      requiredSampleCount: 0,
      readySampleCount: 0,
      validatedEvidenceDocumentCount: 0,
      requiredOwners: [],
      blockers: [],
      detail: "",
      operatorSignoff: null
    },
    brokerageSync: {
      fundAccountId: "fund-1",
      providerId: "alpaca",
      externalAccountId: "PA-1",
      health: "Healthy",
      isLinked: true,
      isStale: false,
      lastAttemptedSyncAt: null,
      lastSuccessfulSyncAt: null,
      lastError: null,
      positionCount: 0,
      openOrderCount: 0,
      fillCount: 0,
      cashTransactionCount: 0,
      securityMissingCount: 0,
      warnings: []
    },
    workItems: [],
    warnings: []
  }
};

describe("OperatorReadinessConsole", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders operator inbox work-item routes as accessible next actions", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce(inbox);

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const actions = await screen.findAllByRole("link", {
      name: "Open promotion review: Promotion checklist incomplete"
    });

    expect(actions[0]).toHaveAttribute("href", "/trading/readiness");
    expect(screen.getByRole("group", { name: /Primary next action: Promotion checklist incomplete/i })).toBeInTheDocument();
    expect(screen.getAllByText("Promotion checklist incomplete").length).toBeGreaterThan(0);
    expect(screen.getByRole("region", { name: "Readiness control strip" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Shared readiness API sources" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Latest runs readiness evidence" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Full-console readiness checkpoints" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: /Replay verified: Review required/i })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: /Brokerage sync healthy: Unavailable/i })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Prioritized operator work items table" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Selected operator work item detail" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Promotion blockers readiness evidence table" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Selected promotion blockers evidence detail" })).toBeInTheDocument();
    await waitFor(() => expect(api.getOperatorInbox).toHaveBeenCalledWith(
      undefined,
      expect.objectContaining({ signal: expect.any(Object) })
    ));
  });


  it("scopes operator inbox refresh to the active fund account when trading context is available", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce(inbox);

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={scopedTrading}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    await screen.findByRole("table", { name: "Prioritized operator work items table" });
    await waitFor(() => expect(api.getOperatorInbox).toHaveBeenCalledWith(
      "fund-1",
      expect.objectContaining({ signal: expect.any(Object) })
    ));
  });

  it("shows critical operator inbox items before lower-priority overflow", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce({
      ...inbox,
      items: [...warningItems, criticalItem],
      criticalCount: 1,
      warningCount: 6,
      reviewCount: 7,
      summary: "7 review items need attention."
    });

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const criticalActions = await screen.findAllByRole("link", {
      name: "Open Security Master: Critical security coverage gap"
    });

    expect(criticalActions[0]).toHaveAttribute("href", "/accounting/security-master");
    expect(screen.getByRole("group", { name: /Primary next action: Critical security coverage gap/i })).toBeInTheDocument();
    expect(screen.getByText("Showing 6 of 7 operator work items; 1 critical item, 6 warnings. Critical items sort first.")).toBeInTheDocument();
    expect(screen.getByText("1 additional work item hidden from this view after priority sorting.")).toBeInTheDocument();
  });

  it("uses a selectable dense table and updates the detail panel for operator work items", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce({
      ...inbox,
      items: [criticalItem, ...warningItems],
      criticalCount: 1,
      warningCount: 6,
      reviewCount: 7,
      summary: "7 review items need attention."
    });

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const detail = await screen.findByRole("region", { name: "Selected operator work item detail" });
    expect(detail).toHaveAttribute("id", "operator-readiness-selected-work-item-detail");
    expect(within(detail).getByRole("heading", { name: "Critical security coverage gap" })).toBeInTheDocument();

    const warningRow = screen.getByRole("row", { name: "Select operator work item Warning item 5" });
    expect(warningRow).toHaveAttribute("aria-controls", "operator-readiness-selected-work-item-detail");
    expect(warningRow).toHaveAttribute("aria-expanded", "false");
    fireEvent.keyDown(warningRow, { key: "Enter" });

    expect(warningRow).toHaveAttribute("aria-expanded", "true");
    expect(within(detail).getByRole("heading", { name: "Warning item 5" })).toBeInTheDocument();
    expect(within(detail).getByRole("link", { name: "Open report packs: Warning item 5" })).toHaveAttribute("href", "/reporting/report-packs");
  });

  it("uses selectable dense tables and detail panels for evidence panel rows", async () => {
    const secondPromotionItem: OperatorWorkItem = {
      ...inbox.items[0],
      workItemId: "promotion-review-run-2",
      label: "Second promotion review",
      detail: "Confirm replay evidence before promotion.",
      createdAt: "2026-04-29T12:05:00Z",
      auditReference: "audit-promotion-2"
    };
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce({
      ...inbox,
      items: [inbox.items[0], secondPromotionItem],
      warningCount: 2,
      reviewCount: 2,
      summary: "2 review items need attention."
    });

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const detail = await screen.findByRole("region", { name: "Selected promotion blockers evidence detail" });
    expect(detail).toHaveAttribute("id", "operator-readiness-promotion-blockers-evidence-detail");
    expect(within(detail).getByRole("heading", { name: "Promotion checklist incomplete" })).toBeInTheDocument();

    const secondRow = screen.getByRole("row", { name: "Select promotion blockers evidence Second promotion review" });
    expect(secondRow).toHaveAttribute("aria-controls", "operator-readiness-promotion-blockers-evidence-detail");
    expect(secondRow).toHaveAttribute("aria-expanded", "false");
    fireEvent.keyDown(secondRow, { key: "Enter" });

    expect(secondRow).toHaveAttribute("aria-expanded", "true");
    expect(within(detail).getByRole("heading", { name: "Second promotion review" })).toBeInTheDocument();
    expect(within(detail).getByRole("link", { name: "Open promotion review: Second promotion review" })).toHaveAttribute("href", "/trading/readiness");
  });

  it("lets operators retry a failed inbox load from the console", async () => {
    vi.spyOn(api, "getOperatorInbox")
      .mockRejectedValueOnce(new Error("Operator inbox 503"))
      .mockResolvedValueOnce(inbox);

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
        reporting={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const recovery = await screen.findByRole("alert");
    expect(recovery).toHaveTextContent("Operator inbox 503");
    expect(within(recovery).getByRole("button", { name: "Retry loading operator inbox work items after failure" })).toHaveTextContent("Retry inbox");

    fireEvent.click(within(recovery).getByRole("button", { name: "Retry loading operator inbox work items after failure" }));

    await waitFor(() => expect(screen.getAllByText("1 review item needs attention.").length).toBeGreaterThan(0));
    await waitFor(() => expect(api.getOperatorInbox).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
