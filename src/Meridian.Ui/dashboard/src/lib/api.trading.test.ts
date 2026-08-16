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
  getAccountingWorkspace,
  getBrokerageHouseholdPortfolio,
  getDataWorkspace,
  developmentFixtureHeader,
  getExecutionControls,
  getPaperSessionDetail,
  getPortfolioWorkspace,
  getPortfolioAggregate,
  getPortfolioExposure,
  getPortfolioSymbolExposure,
  getPrivateCapitalCloseCockpit,
  getPrivateCapitalFundEventCommandCenter,
  activateProviderIntegration,
  checkProviderIntegrationSchemaDrift,
  createProviderIntegrationReconciliationHandoff,
  getProviderIntegrationConnectionMonitor,
  getProviderIntegrationConnectionSyncPlan,
  getProviderIntegrationConnectionSyncRuns,
  getProviderIntegrationIdentityResolution,
  getProviderIntegrationPromotionReadiness,
  getProviderIntegrationQuarantineReview,
  getProviderIntegrationReadiness,
  getProviderIntegrationReconciliationHandoffHistory,
  getProviderIntegrationStagingReview,
  getProviderIntegrationTemplate,
  getProviderIntegrationTemplates,
  getProviderRoutingBindings,
  getProviderRoutingConnections,
  getProviderRoutingTrustSnapshots,
  getReportingWorkspace,
  getReplayStatus,
  getReconciliationBreakAudit,
  getReconciliationRun,
  getReconciliationStatementExceptions,
  getReconciliationStatementRun,
  getReconciliationStatementRuns,
  getRunContinuity,
  getRunHistory,
  getRunLedgerJournal,
  getRunTrialBalance,
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
  getStrategyBriefing,
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
  previewProviderRoute,
  importProviderIntegrationOpenApi,
  replayProviderIntegrationQuarantineRecords,
  resolveProviderIntegrationQuarantineRecord,
  runDueProviderIntegrationSync,
  runManualCsvProviderIntegrationDryRun,
  runRestProviderIntegrationDryRun,
  runReconciliation,
  runAnalysisExport,
  saveProviderIntegrationSetup,
  resetDevelopmentFixtureUsage,
  resumeReplay,
  revokeAlpacaConnection,
  saveWorkflowPreset,
  searchEvidenceVault,
  searchSecurities,
  seekReplay,
  setReplaySpeed,
  startReplay,
  stopReplay,
  supersedeReconciliationBreak,
  submitOrder,
  updateExecutionDefaultPositionLimit,
  updateExecutionSymbolPositionLimit,
  updateWorkflowPreset,
  waiveReconciliationBreak
} from "@/lib/api";

describe("trading endpoint wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("wires governed reconciliation waive and supersede actions with disposition evidence", async () => {
    const waiver = {
      breakId: "break / 1",
      action: "Waive" as const,
      actor: "operator-1",
      commandId: "cmd-waive-1",
      correlationId: "corr-1",
      source: "accounting-workstation",
      expectedVersion: 4,
      reason: "Reviewed immaterial difference",
      evidenceLinks: ["evidence://waiver/1"],
      actionOrigin: "HumanOperator" as const,
      approvalActor: "controller-1",
      approvalReference: "approval://waiver/1"
    };
    const supersede = {
      ...waiver,
      action: "Supersede" as const,
      commandId: "cmd-supersede-1",
      supersedingBreakId: "break-2"
    };

    await waiveReconciliationBreak(waiver);
    await supersedeReconciliationBreak(supersede);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/waive",
      expect.objectContaining({ method: "POST", body: JSON.stringify(waiver) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/supersede",
      expect.objectContaining({ method: "POST", body: JSON.stringify(supersede) })
    );
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
    await updateExecutionDefaultPositionLimit({ maxPositionSize: 75, reason: "desk risk cap" });
    await updateExecutionSymbolPositionLimit("AAPL", { maxPositionSize: 10, reason: "event risk" });
    await getTradingReadiness({ signal: controller.signal });
    await createExecutionManualOverride({
      kind: "BypassOrderControls",
      reason: "maintenance",
      symbol: "AAPL"
    });
    await clearExecutionManualOverride("ovr-1");
    await closePosition("paper:AAPL", "53bf0251-17f6-4fb7-8dbe-6fb4966e2749");

    expect(fetchMock).toHaveBeenCalledWith("/api/execution/controls", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/position-limits/default",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ maxPositionSize: 75, reason: "desk risk cap" })
      })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/controls/position-limits/AAPL",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ maxPositionSize: 10, reason: "event risk" })
      })
    );
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
        body: JSON.stringify({
          positionKey: "paper:AAPL",
          fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749"
        })
      })
    );
  });

  it("omits non-GUID fund account labels from position action mutations", async () => {
    await closePosition("paper:AAPL", "brokerage-account-label");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/positions/actions/close",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ positionKey: "paper:AAPL" })
      })
    );
  });



  it("keeps API methods aligned to contract-manual override paths", async () => {
    const CONTRACT_EXECUTION_CONTROLS = "/api/execution/controls" as const;
    const CONTRACT_EXECUTION_DEFAULT_POSITION_LIMIT = "/api/execution/controls/position-limits/default" as const;
    const CONTRACT_EXECUTION_SYMBOL_POSITION_LIMIT = "/api/execution/controls/position-limits/MSFT" as const;
    const CONTRACT_EXECUTION_MANUAL_OVERRIDES = "/api/execution/controls/manual-overrides" as const;
    const CONTRACT_EXECUTION_MANUAL_OVERRIDE_CLEAR =
      "/api/execution/controls/manual-overrides/ovr-contract/clear" as const;

    await getExecutionControls();
    await updateExecutionDefaultPositionLimit({ maxPositionSize: null, reason: "clear" });
    await updateExecutionSymbolPositionLimit("MSFT", { maxPositionSize: 25 });
    await createExecutionManualOverride({ kind: "BypassOrderControls", reason: "contract-check" });
    await clearExecutionManualOverride("ovr-contract");

    expect(fetchMock).toHaveBeenNthCalledWith(1, CONTRACT_EXECUTION_CONTROLS, expect.anything());
    expect(fetchMock).toHaveBeenNthCalledWith(2, CONTRACT_EXECUTION_DEFAULT_POSITION_LIMIT, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, CONTRACT_EXECUTION_SYMBOL_POSITION_LIMIT, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(4, CONTRACT_EXECUTION_MANUAL_OVERRIDES, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(5, CONTRACT_EXECUTION_MANUAL_OVERRIDE_CLEAR, expect.objectContaining({ method: "POST" }));
  });

  it("uses dev fixtures for workstation bootstrap GETs when the API host is missing", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => ({}), text: async () => "" });

    await expect(getSession()).resolves.toMatchObject({ displayName: "Ops Desk" });
    await expect(getSystemStatus()).resolves.toMatchObject({ providersTotal: 4, recentEvents: [] });
    await expect(getStrategyWorkspace()).resolves.toMatchObject({ runs: expect.any(Array) });
    await expect(getStrategyBriefing()).resolves.toMatchObject({ workspace: expect.any(Object) });
    await expect(getTradingWorkspace()).resolves.toMatchObject({ openOrders: expect.any(Array) });
    await expect(getPortfolioWorkspace()).resolves.toMatchObject({ positions: expect.any(Array) });
    await expect(getDataWorkspace()).resolves.toMatchObject({
      backfills: expect.any(Array),
      exports: [expect.objectContaining({ target: "strategy pack" })]
    });
    await expect(getAccountingWorkspace()).resolves.toMatchObject({ reconciliationQueue: expect.any(Array) });
    await expect(getReportingWorkspace()).resolves.toMatchObject({
      profileCount: expect.any(Number),
      profiles: expect.any(Array),
      summary: expect.any(String)
    });
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/strategy", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/strategy/briefing", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/portfolio", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/data", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/accounting", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reporting", expect.anything());
    expect(hasDevelopmentFixtureUsage()).toBe(true);
  });

  it("passes account scope through the trading workspace endpoint", async () => {
    await getTradingWorkspace({ fundAccountId: "fund account/1" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/trading?fundAccountId=fund+account%2F1",
      expect.anything()
    );
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

  it("wires private-capital fund-event command-center drill-throughs", async () => {
    const controller = new AbortController();

    await getPrivateCapitalFundEventCommandCenter(
      {
        fundProfileId: "fund-alpha",
        ledgerBookId: "11111111-1111-1111-1111-111111111111",
        fundEventId: "fund-event:fund-alpha:capital-call"
      },
      { signal: controller.signal }
    );

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&ledgerBookId=11111111-1111-1111-1111-111111111111&fundEventId=fund-event%3Afund-alpha%3Acapital-call",
      expect.objectContaining({ signal: controller.signal })
    );
  });

  it("wires private-capital close cockpit drill-throughs", async () => {
    const controller = new AbortController();

    await getPrivateCapitalCloseCockpit(
      {
        fundProfileId: "fund-alpha",
        ledgerBookId: "11111111-1111-1111-1111-111111111111",
        fundAccountId: "fund-account:lp-1",
        periodId: "2026-06",
        entityId: "entity-master"
      },
      { signal: controller.signal }
    );

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/private-capital-close-cockpit?fundProfileId=fund-alpha&ledgerBookId=11111111-1111-1111-1111-111111111111&fundAccountId=fund-account%3Alp-1&periodId=2026-06&entityId=entity-master",
      expect.objectContaining({ signal: controller.signal })
    );
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

  it("does not invent a current heartbeat when the typed status payload omits timestamps", async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({
        systemStatus: "Degraded",
        providersOnline: 0,
        providersTotal: 1,
        storageHealth: "Warning"
      }),
      text: async () => "{}"
    });

    await expect(getSystemStatus()).resolves.toMatchObject({
      systemStatus: "Degraded",
      lastHeartbeatUtc: null
    });
  });

  it("does not fabricate a legacy status event timestamp when the host omits freshness", async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({
        isConnected: false,
        metrics: {},
        pipeline: {}
      }),
      text: async () => "{}"
    });

    await expect(getSystemStatus()).resolves.toMatchObject({
      systemStatus: "Offline",
      lastHeartbeatUtc: null,
      recentEvents: []
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

  it("posts order mutations with selected fund account scope", async () => {
    await submitOrder({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 1,
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749"
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/execution/orders/submit",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          symbol: "AAPL",
          side: "Buy",
          type: "Market",
          quantity: 1,
          fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749"
        })
      })
    );
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
    const controller = new AbortController();
    await runAnalysisExport("audit-pack", { signal: controller.signal });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        signal: controller.signal,
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
  });

  it("wires workstation workflow library and preset endpoints", async () => {
    await getWorkstationWorkflowSummary({
      hasOperatingContext: true,
      fundProfileId: "fund-1",
      fundAccountId: "account-1"
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
      "/api/workstation/workflow-summary?hasOperatingContext=true&fundProfileId=fund-1&fundAccountId=account-1",
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
    await getRunLedgerJournal("run-1", {
      from: "2026-01-01",
      to: "2026-01-31",
      fundId: "fund-alpha",
      entityId: "entity-a",
      instrumentId: "loan-1",
      counterpartyId: "borrower-1",
      externalGlDimensions: { Department: "InvestmentOps" }
    });
    await getRunTrialBalance("run-1", {
      accountType: "Asset",
      fundId: "fund-alpha",
      entityId: "entity-a",
      sleeveId: "sleeve-a",
      strategyId: "strategy-a",
      investorId: "investor-1",
      capitalAccountId: "capital-1",
      instrumentId: "loan-1",
      taxLotId: "taxlot-1",
      costCenterId: "ops",
      counterpartyId: "borrower-1",
      externalGlDimensions: { Department: "InvestmentOps" }
    });
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
    await getReconciliationStatementRuns();
    await getReconciliationStatementRun("statement-run-1");
    await getReconciliationStatementExceptions();
    await getReconciliationBreakAudit("break-1");
    await getPortfolioAggregate();
    await getPortfolioExposure();
    await getPortfolioSymbolExposure("AAPL");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/runs/run-1/ledger/journal?from=2026-01-01&to=2026-01-31&fundId=fund-alpha&entityId=entity-a&instrumentId=loan-1&counterpartyId=borrower-1&externalGl.Department=InvestmentOps",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/runs/run-1/ledger/trial-balance?accountType=Asset&fundId=fund-alpha&entityId=entity-a&sleeveId=sleeve-a&strategyId=strategy-a&investorId=investor-1&capitalAccountId=capital-1&instrumentId=loan-1&taxLotId=taxlot-1&costCenterId=ops&counterpartyId=borrower-1&externalGl.Department=InvestmentOps",
      expect.anything()
    );
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
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/statement-runs", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/statement-runs/statement-run-1", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/statement-exceptions", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/workstation/reconciliation/break-queue/break-1/audit", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/aggregate", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/exposure", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/portfolio/symbols/AAPL/exposure", expect.anything());
  });

  it("wires evidence vault search by accounting record linkage", async () => {
    await searchEvidenceVault({
      evidenceSubject: "accounting-record/workflow-2026-05",
      runId: null,
      periodId: "2026-05",
      reportPackId: "report-pack-2026-05",
      reconciliationCaseId: null,
      accountingRecordId: "workflow-2026-05"
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/evidence/vault/search",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          evidenceSubject: "accounting-record/workflow-2026-05",
          runId: null,
          periodId: "2026-05",
          reportPackId: "report-pack-2026-05",
          reconciliationCaseId: null,
          accountingRecordId: "workflow-2026-05"
        })
      })
    );
  });

  it("wires provider-routing endpoint group", async () => {
    await getProviderRoutingConnections();
    await getProviderRoutingBindings();
    await getProviderRoutingTrustSnapshots();
    await previewProviderRoute({
      capability: "RealtimeMarketData",
      symbol: "AAPL",
      requireProductionReady: false
    });

    expect(fetchMock).toHaveBeenCalledWith("/api/provider-routing/connections", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/provider-routing/bindings", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith("/api/provider-routing/trust-snapshots", expect.anything());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/provider-routing/preview",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          capability: "RealtimeMarketData",
          symbol: "AAPL",
          requireProductionReady: false
        })
      })
    );
  });

  it("wires provider-integration runtime endpoint group", async () => {
    const controller = new AbortController();
    const openApiRequest = {
      manifestId: "custodian / v1",
      providerId: "custodian",
      displayName: "Custodian",
      environment: "sandbox",
      authType: "ApiKey",
      tokenUrl: null,
      scopes: [],
      capabilities: ["Positions"],
      openApiDocumentJson: "{}",
      importedBy: "ops",
      importedAt: "2026-06-16T12:00:00Z",
      changeReason: "Import provider template"
    } as Parameters<typeof importProviderIntegrationOpenApi>[0];
    const setupRequest = {
      manifest: { manifestId: "custodian / v1" },
      connection: { connectionId: "connection / 1" },
      savedBy: "ops",
      savedAt: "2026-06-16T12:01:00Z",
      changeReason: "Save draft"
    } as Parameters<typeof saveProviderIntegrationSetup>[0];
    const manualDryRun = {
      syncRunId: "sync / 1",
      manifestId: "custodian / v1",
      connectionId: "connection / 1",
      capability: "Positions",
      fileName: "positions.csv",
      csvContent: "symbol,quantity\nAAPL,1",
      requestedBy: "ops",
      requestedAt: "2026-06-16T12:02:00Z"
    } as Parameters<typeof runManualCsvProviderIntegrationDryRun>[0];
    const restDryRun = {
      syncRunId: "sync / 2",
      manifestId: "custodian / v1",
      connectionId: "connection / 1",
      capability: "Positions",
      endpointKey: "positions",
      pathParameters: {},
      queryParameters: {},
      requestedBy: "ops",
      requestedAt: "2026-06-16T12:03:00Z",
      maxPages: 1
    } as Parameters<typeof runRestProviderIntegrationDryRun>[0];
    const activationRequest = {
      manifestId: "custodian / v1",
      connectionId: "connection / 1",
      approvedBy: "ops",
      approvedAt: "2026-06-16T12:04:00Z",
      approvalEvidenceId: "approval / 1",
      changeReason: "Approved dry run"
    } as Parameters<typeof activateProviderIntegration>[0];
    const runDueRequest = {
      connectionId: "connection / 1",
      requestedAt: "2026-06-16T12:05:00Z",
      requestedBy: "ops",
      maxPages: 2,
      pathParametersByCapability: {},
      queryParametersByCapability: {}
    } as Parameters<typeof runDueProviderIntegrationSync>[1];
    const schemaDriftRequest = {
      manifestId: "custodian / v1",
      connectionId: "connection / 1",
      capability: "Positions",
      endpointKey: "positions",
      syncRunId: "sync / 2",
      rawPayloadId: "payload / 1",
      checkedBy: "ops",
      checkedAt: "2026-06-16T12:06:00Z"
    } as Parameters<typeof checkProviderIntegrationSchemaDrift>[0];
    const handoffRequest = {
      connectionId: "connection / 1",
      stagingRecordIds: ["staging / 1"],
      requestedBy: "ops",
      requestedAt: "2026-06-16T12:07:00Z",
      approvalEvidenceId: "approval / 2",
      note: "Promote ready rows",
      recentRunLimit: 5
    } as Parameters<typeof createProviderIntegrationReconciliationHandoff>[0];
    const quarantineResolution = {
      connectionId: "connection / 1",
      syncRunId: "sync / 2",
      quarantineRecordId: "quarantine / 1",
      action: "ReviewOnly",
      reviewedBy: "ops",
      reviewedAt: "2026-06-16T12:08:00Z",
      note: "Reviewed"
    } as Parameters<typeof resolveProviderIntegrationQuarantineRecord>[0];
    const quarantineReplay = {
      replaySyncRunId: "sync / replay",
      sourceSyncRunId: "sync / 2",
      manifestId: "custodian / v1",
      connectionId: "connection / 1",
      capability: "Positions",
      quarantineRecordIds: ["quarantine / 1"],
      requestedBy: "ops",
      requestedAt: "2026-06-16T12:09:00Z"
    } as Parameters<typeof replayProviderIntegrationQuarantineRecords>[0];

    await getProviderIntegrationTemplates({ signal: controller.signal });
    await getProviderIntegrationTemplate("custodian / v1");
    await importProviderIntegrationOpenApi(openApiRequest);
    await saveProviderIntegrationSetup(setupRequest);
    await getProviderIntegrationReadiness("custodian / v1", "connection / 1");
    await runManualCsvProviderIntegrationDryRun(manualDryRun);
    await runRestProviderIntegrationDryRun(restDryRun);
    await activateProviderIntegration(activationRequest);
    await getProviderIntegrationConnectionMonitor("connection / 1", 3);
    await getProviderIntegrationConnectionSyncRuns("connection / 1", 4);
    await getProviderIntegrationConnectionSyncPlan("connection / 1", "2026-06-16T12:10:00Z");
    await runDueProviderIntegrationSync("connection / 1", runDueRequest);
    await checkProviderIntegrationSchemaDrift(schemaDriftRequest);
    await getProviderIntegrationStagingReview("connection / 1", 5);
    await getProviderIntegrationIdentityResolution("connection / 1", 6);
    await getProviderIntegrationPromotionReadiness("connection / 1", 7);
    await getProviderIntegrationReconciliationHandoffHistory("connection / 1");
    await createProviderIntegrationReconciliationHandoff(handoffRequest);
    await getProviderIntegrationQuarantineReview("connection / 1", 8);
    await resolveProviderIntegrationQuarantineRecord(quarantineResolution);
    await replayProviderIntegrationQuarantineRecords(quarantineReplay);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/templates",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/templates/custodian%20%2F%20v1",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/import/openapi",
      expect.objectContaining({ method: "POST", body: JSON.stringify(openApiRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/setup",
      expect.objectContaining({ method: "POST", body: JSON.stringify(setupRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/manifests/custodian%20%2F%20v1/readiness?connectionId=connection+%2F+1",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/dry-runs/manual-csv",
      expect.objectContaining({ method: "POST", body: JSON.stringify(manualDryRun) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/dry-runs/rest",
      expect.objectContaining({ method: "POST", body: JSON.stringify(restDryRun) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/activate",
      expect.objectContaining({ method: "POST", body: JSON.stringify(activationRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/monitor?recentRunLimit=3",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/sync-runs?recentRunLimit=4",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/sync-plan?evaluatedAt=2026-06-16T12%3A10%3A00Z",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/sync/run-due",
      expect.objectContaining({ method: "POST", body: JSON.stringify(runDueRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/schema-drift/check",
      expect.objectContaining({ method: "POST", body: JSON.stringify(schemaDriftRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/staging?recentRunLimit=5",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/identity-resolution?recentRunLimit=6",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/promotion-readiness?recentRunLimit=7",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/reconciliation-handoffs",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/reconciliation/handoff",
      expect.objectContaining({ method: "POST", body: JSON.stringify(handoffRequest) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/connections/connection%20%2F%201/quarantine?recentRunLimit=8",
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/quarantine/resolve",
      expect.objectContaining({ method: "POST", body: JSON.stringify(quarantineResolution) })
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/provider-integrations/quarantine/replay",
      expect.objectContaining({ method: "POST", body: JSON.stringify(quarantineReplay) })
    );
  });
});
