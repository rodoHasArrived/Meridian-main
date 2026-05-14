import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  approvePromotion,
  bulkResolveSecurityConflicts,
  clearExecutionManualOverride,
  closePosition,
  connectAlpacaConnection,
  createExecutionManualOverride,
  deleteWorkflowPreset,
  evaluatePromotion,
  getAlpacaConnectionStatus,
  getBrokerageHouseholdPortfolio,
  getDataOperationsWorkspace,
  developmentFixtureHeader,
  getExecutionControls,
  getGovernanceWorkspace,
  getPaperSessionDetail,
  getPortfolioWorkspace,
  getPortfolioAggregate,
  getPortfolioExposure,
  getPortfolioSymbolExposure,
  getReportingWorkspace,
  getReplayStatus,
  getReconciliationBreakAudit,
  getReconciliationRun,
  getRunContinuity,
  getRunHistory,
  getRunLedgerJournal,
  getRunReconciliation,
  getRunReconciliationHistory,
  getRunReviewPacket,
  getRunSweeps,
  getRunTimeline,
  getSecurityEconomicDefinition,
  getSecurityHistory,
  getSecurityIdentity,
  getSecurityTrustSnapshot,
  getSession,
  getSystemStatus,
  getStrategyWorkspace,
  getTradingReadiness,
  getTradingWorkspace,
  getWorkflowLibrary,
  getWorkflowPresets,
  getWorkstationWorkflowSummary,
  hasDevelopmentFixtureUsage,
  markWorkflowPresetUsed,
  pinWorkflowPreset,
  pauseReplay,
  runReconciliation,
  runAnalysisExport,
  resetDevelopmentFixtureUsage,
  resumeReplay,
  revokeAlpacaConnection,
  saveWorkflowPreset,
  searchSecurities,
  seekReplay,
  setReplaySpeed,
  startReplay,
  stopReplay,
  submitOrder,
  updateWorkflowPreset
} from "@/lib/api";

describe("trading endpoint wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("wires promotion endpoints", async () => {
    await evaluatePromotion("run-123");
    await approvePromotion({
      runId: "run-123",
      approvedBy: "ops-1",
      approvalReason: "Risk and quality checks passed",
      reviewNotes: "Reviewed by trading desk",
      manualOverrideId: "override-42"
    });
    expect(fetchMock).toHaveBeenCalledWith("/api/promotion/evaluate/run-123", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/promotion/approve",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          runId: "run-123",
          approvedBy: "ops-1",
          approvalReason: "Risk and quality checks passed",
          reviewNotes: "Reviewed by trading desk",
          manualOverrideId: "override-42"
        })
      })
    );
  });

  it("wires execution/replay endpoints", async () => {
    await getPaperSessionDetail("sess-1");
    await startReplay("/tmp/file.jsonl", 2);
    await pauseReplay("rep-1");
    await resumeReplay("rep-1");
    await stopReplay("rep-1");
    await seekReplay("rep-1", 5000);
    await setReplaySpeed("rep-1", 3);
    await getReplayStatus("rep-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/sessions/sess-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/start", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/pause", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/resume", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/stop", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/seek", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/speed", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenCalledWith("/api/replay/rep-1/status", expect.anything());
  });

  it("wires execution controls and manual override endpoints", async () => {
    const controller = new AbortController();
    await getExecutionControls();
    await getTradingReadiness({ signal: controller.signal });
    await createExecutionManualOverride({
      kind: "BypassOrderControls",
      reason: "maintenance",
      symbol: "AAPL"
    });
    await clearExecutionManualOverride("ovr-1");
    await closePosition("paper:AAPL");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/controls", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/trading/readiness",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/manual-overrides/ovr-1/clear",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/positions/actions/close",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ positionKey: "paper:AAPL" })
      })
    );
  });

  it("uses dev fixtures for workstation bootstrap GETs when the API host is missing", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => ({}), text: async () => "" });

    await expect(getSession()).resolves.toMatchObject({ displayName: "Ops Desk" });
    await expect(getSystemStatus()).resolves.toMatchObject({ providersTotal: 4, recentEvents: [] });
    await expect(getStrategyWorkspace()).resolves.toMatchObject({ runs: expect.any(Array) });
    await expect(getTradingWorkspace()).resolves.toMatchObject({ openOrders: expect.any(Array) });
    await expect(getPortfolioWorkspace()).resolves.toMatchObject({ positions: expect.any(Array) });
    await expect(getDataOperationsWorkspace()).resolves.toMatchObject({ backfills: expect.any(Array) });
    await expect(getGovernanceWorkspace()).resolves.toMatchObject({ reconciliationQueue: expect.any(Array) });
    await expect(getReportingWorkspace()).resolves.toMatchObject({ reporting: expect.any(Object) });
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/strategy", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/portfolio", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/data", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/accounting", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reporting", expect.anything());
    expect(hasDevelopmentFixtureUsage()).toBe(true);
  });

  it("tracks proxy-served development fixtures from the response header", async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: (name: string) => (name === developmentFixtureHeader ? "true" : null) },
      json: async () => ({
        activeWorkspace: "trading",
        commandCount: 1,
        displayName: "Demo Desk",
        environment: "paper",
        role: "Operator"
      }),
      text: async () => "{}"
    });

    expect(hasDevelopmentFixtureUsage()).toBe(false);
    await expect(getSession()).resolves.toMatchObject({ displayName: "Demo Desk" });
    expect(hasDevelopmentFixtureUsage()).toBe(true);
  });

  it("normalizes the host status endpoint into the overview dashboard contract", async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({
        isConnected: true,
        timestampUtc: "2026-05-09T17:35:53.0689515+00:00",
        uptime: "00:08:28.1481500",
        metrics: {
          published: 9703,
          dropped: 0,
          historicalBars: 0,
          eventsPerSecond: 19.158524,
          dropRate: 0,
          trades: 0,
          depthUpdates: 0,
          sourceProvider: null,
          isStale: false
        },
        pipeline: {
          currentQueueSize: 0,
          queueCapacity: 50000,
          queueUtilization: 0
        }
      }),
      text: async () => "{}"
    });

    await expect(getSystemStatus()).resolves.toMatchObject({
      systemStatus: "Healthy",
      providersOnline: 1,
      providersTotal: 1,
      storageHealth: "Healthy",
      lastHeartbeatUtc: "2026-05-09T17:35:53.0689515+00:00",
      metrics: expect.arrayContaining([
        expect.objectContaining({ id: "events", label: "Events Published" }),
        expect.objectContaining({ id: "queue", label: "Pipeline Queue" })
      ]),
      recentEvents: [
        expect.objectContaining({
          id: "host-status",
          type: "info",
          source: "Meridian host"
        })
      ]
    });
  });

  it("wires Alpaca brokerage connection endpoints", async () => {
    const controller = new AbortController();
    await getAlpacaConnectionStatus();
    await connectAlpacaConnection({
      keyId: "paper-key",
      secretKey: "paper-secret",
      environment: "paper"
    }, { signal: controller.signal });
    await revokeAlpacaConnection({ signal: controller.signal });
    await getBrokerageHouseholdPortfolio();

    expect(fetchMock).toHaveBeenCalledWith("/api/brokerage-connections/alpaca/status", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/brokerage-connections/alpaca/connect",
      expect.objectContaining({
        method: "POST",
        signal: controller.signal,
        body: JSON.stringify({
          keyId: "paper-key",
          secretKey: "paper-secret",
          environment: "paper"
        })
      })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/brokerage-connections/alpaca",
      expect.objectContaining({ method: "DELETE", signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/household?provider=alpaca", expect.anything());
  });

  it("uses dev security fixtures when the search endpoint responds with an empty set", async () => {
    fetchMock.mockResolvedValue({ ok: true, status: 200, json: async () => [], text: async () => "[]" });

    await expect(searchSecurities("pcg")).resolves.toMatchObject([
      expect.objectContaining({
        securityId: "sec-dev-002",
        displayName: "PG&E Corporation"
      })
    ]);
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities?query=pcg&take=25&activeOnly=true",
      expect.anything()
    );
  });

  it("uses dev security identity fixtures when the drill-in endpoint is missing", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => ({}), text: async () => "" });

    await expect(getSecurityIdentity("sec-dev-002")).resolves.toMatchObject({
      securityId: "sec-dev-002",
      displayName: "PG&E Corporation",
      identifiers: expect.any(Array)
    });
  });

  it("does not use dev fixtures for order mutations", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, text: async () => "not found" });

    await expect(
      submitOrder({
        symbol: "AAPL",
        side: "Buy",
        type: "Market",
        quantity: 1
      })
    ).rejects.toThrow("Request failed for /api/execution/orders/submit (404) - not found");
  });

  it("preserves backend problem details for every HTTP verb", async () => {
    fetchMock
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        text: async () => JSON.stringify({ detail: "Promotion gate still has open blockers." })
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 422,
        text: async () => JSON.stringify({ message: "Preset name is required." })
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 503,
        text: async () => "Workflow preset store unavailable"
      });

    await expect(evaluatePromotion("run-123")).rejects.toThrow(
      "Request failed for /api/promotion/evaluate/run-123 (409) - Promotion gate still has open blockers."
    );
    await expect(
      updateWorkflowPreset("preset-1", {
        presetId: "preset-1",
        name: "",
        description: "",
        workflowId: "paper-trading-readiness",
        actionId: "workflow.trading.review-paper-candidate",
        tags: [],
        isPinned: false
      })
    ).rejects.toThrow(
      "Request failed for /api/workstation/workflows/presets/preset-1 (422) - Preset name is required."
    );
    await expect(deleteWorkflowPreset("preset-1")).rejects.toThrow(
      "Request failed for /api/workstation/workflows/presets/preset-1 (503) - Workflow preset store unavailable"
    );
  });

  it("includes validation problem field errors in mutation diagnostics", async () => {
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({
        title: "One or more validation errors occurred.",
        errors: {
          approvedBy: ["Approved by is required."],
          approvalReason: ["Approval reason must explain the promotion evidence."]
        }
      })
    });

    await expect(
      approvePromotion({
        runId: "run-123",
        approvedBy: "",
        approvalReason: ""
      })
    ).rejects.toThrow(
      "Request failed for /api/promotion/approve (400) - One or more validation errors occurred. approvedBy: Approved by is required.; approvalReason: Approval reason must explain the promotion evidence."
    );
  });

  it("accepts empty success bodies from no-content mutations", async () => {
    fetchMock
      .mockResolvedValueOnce({ ok: true, status: 200, text: async () => "" })
      .mockResolvedValueOnce({ ok: true, status: 204, text: async () => "" });

    await expect(deleteWorkflowPreset("preset-1")).resolves.toBeNull();
    await expect(deleteWorkflowPreset("preset-2")).resolves.toBeNull();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-1",
      expect.objectContaining({ method: "DELETE" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-2",
      expect.objectContaining({ method: "DELETE" })
    );
  });

  it("wires analysis export as a POST mutation", async () => {
    await runAnalysisExport("audit-pack");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
  });

  it("wires workstation workflow library and preset endpoints", async () => {
    await getWorkstationWorkflowSummary({
      hasOperatingContext: true,
      fundProfileId: "fund-1"
    });
    await getWorkflowLibrary();
    await getWorkflowPresets();
    await saveWorkflowPreset({
      name: "Daily desk",
      description: "Open paper readiness review",
      workflowId: "paper-trading-readiness",
      actionId: "workflow.trading.review-paper-candidate",
      tags: ["paper"],
      isPinned: true
    });
    await updateWorkflowPreset("preset-1", {
      presetId: "preset-1",
      name: "Updated desk",
      description: "Open paper readiness review",
      workflowId: "paper-trading-readiness",
      actionId: "workflow.trading.review-paper-candidate",
      tags: ["paper"],
      isPinned: true
    });
    await pinWorkflowPreset("preset-1", true);
    await markWorkflowPresetUsed("preset-1");
    await deleteWorkflowPreset("preset-1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflow-summary?hasOperatingContext=true&fundProfileId=fund-1",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/workflows", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/workflows/presets", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-1",
      expect.objectContaining({ method: "PUT" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-1/pin",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ isPinned: true }) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-1/used",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/workflows/presets/preset-1",
      expect.objectContaining({ method: "DELETE" })
    );
  });

  it("wires run continuity, reconciliation, security, and portfolio workstation endpoints", async () => {
    await getRunLedgerJournal("run-1", { from: "2026-01-01", to: "2026-01-31" });
    await getRunContinuity("run-1");
    await getRunReviewPacket("run-1", "fund-1");
    await getRunReconciliation("run-1");
    await getRunReconciliationHistory("run-1");
    await getRunHistory({ mode: "paper", limit: 25 });
    await getRunTimeline({ mode: "paper", status: "Completed", strategyId: "strategy-1", limit: 5 });
    await getRunSweeps(3);
    await getSecurityHistory("00000000-0000-0000-0000-000000000001");
    await getSecurityEconomicDefinition("00000000-0000-0000-0000-000000000001");
    await getSecurityTrustSnapshot("00000000-0000-0000-0000-000000000001");
    await bulkResolveSecurityConflicts({ conflictIds: ["conflict-1"], resolvedBy: "ops" });
    await runReconciliation({ runId: "run-1" });
    await getReconciliationRun("recon-1");
    await getReconciliationBreakAudit("break-1");
    await getPortfolioAggregate();
    await getPortfolioExposure();
    await getPortfolioSymbolExposure("AAPL");

    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/run-1/ledger/journal?from=2026-01-01&to=2026-01-31", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/run-1/continuity", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/run-1/review-packet?fundAccountId=fund-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/run-1/reconciliation", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/run-1/reconciliation/history", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/history?mode=paper&limit=25", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/timeline?mode=paper&status=Completed&strategyId=strategy-1&limit=5", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/runs/sweeps?limit=3", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities/00000000-0000-0000-0000-000000000001/history",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities/00000000-0000-0000-0000-000000000001/economic-definition",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/securities/00000000-0000-0000-0000-000000000001/trust-snapshot",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/security-master/conflicts/bulk-resolve",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/reconciliation/runs",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/runs/recon-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/break-queue/break-1/audit", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/aggregate", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/exposure", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/symbols/AAPL/exposure", expect.anything());
  });
});
