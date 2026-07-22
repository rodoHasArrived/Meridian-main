// @vitest-environment node

import { describe, expect, it } from "vitest";
import config, {
  createMeridianApiFallbackBypass,
  createMeridianApiProxy,
  defaultMeridianApiBaseUrl,
  meridianDevFixtureHeader,
  meridianScreenshotCaptureEnv,
  resolveMeridianApiBaseUrl,
  resolveViteHmrConfig
} from "../vite.config";
import type { ProxyOptions, UserConfig } from "vite";
import type { IncomingMessage, ServerResponse } from "node:http";
import {
  AUTH_API_ENDPOINTS,
  COVERED_CALL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  PROVIDER_ROUTING_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  RISK_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  STRATEGY_DESIGNER_API_ENDPOINTS,
  STRATEGY_ENGINE_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  executionAuditEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  historicalBarsEndpoint,
  marketDataQuoteEndpoint,
  marketDataQuotesSnapshotEndpoint,
  promotionEvaluateEndpoint,
  riskRuleConfigEndpoint,
  securityMasterCorporateActionsEndpoint,
  securityMasterAssetProfilesEndpoint,
  securityMasterOperatorOverridesEndpoint,
  securityMasterTradingParametersEndpoint,
  workstationEvidenceExportManifestEndpoint,
  workstationEvidencePacketEndpoint,
  workstationEvidenceValidateEndpoint,
  workstationProviderIntegrationConnectionMonitorEndpoint,
  workstationProviderIntegrationConnectionSyncPlanEndpoint,
  workstationProviderIntegrationConnectionSyncRunsEndpoint,
  workstationProviderIntegrationIdentityResolutionEndpoint,
  workstationProviderIntegrationPromotionReadinessEndpoint,
  workstationProviderIntegrationQuarantineReviewEndpoint,
  workstationProviderIntegrationReconciliationHandoffHistoryEndpoint,
  workstationProviderIntegrationStagingReviewEndpoint,
  workstationFinancialRecordExplorerEndpoint,
  workstationWorkflowSummaryEndpoint,
  workstationOperatorInboxEndpoint,
  workstationSecurityMasterIdentityEndpoint,
  workstationSecurityMasterSearchEndpoint
} from "./lib/workstation-endpoints";

function getApiProxyTarget(proxy: Record<string, string | ProxyOptions> | undefined): string | undefined {
  const apiProxy = proxy?.["/api"];
  return typeof apiProxy === "string" ? apiProxy : apiProxy?.target?.toString();
}

describe("Vite Meridian API proxy", () => {
  it("defaults to the local Meridian host", () => {
    expect(resolveMeridianApiBaseUrl({})).toBe(defaultMeridianApiBaseUrl);
  });

  it("normalizes configured Meridian API targets", () => {
    expect(resolveMeridianApiBaseUrl({ MERIDIAN_API_BASE_URL: " http://localhost:9090/// " })).toBe(
      "http://localhost:9090"
    );
    expect(resolveMeridianApiBaseUrl({ VITE_MERIDIAN_API_BASE_URL: "http://localhost:7070/" })).toBe(
      "http://localhost:7070"
    );
  });

  it("proxies /api in both dev and preview instead of letting Vite serve it", () => {
    const userConfig = config as UserConfig;

    expect(getApiProxyTarget(userConfig.server?.proxy)).toBe(defaultMeridianApiBaseUrl);
    expect(getApiProxyTarget(userConfig.preview?.proxy)).toBe(defaultMeridianApiBaseUrl);
  });

  it("disables Vite HMR for screenshot-capture runs only", () => {
    expect(resolveViteHmrConfig({})).toBeUndefined();
    expect(resolveViteHmrConfig({ [meridianScreenshotCaptureEnv]: "true" })).toBe(false);
    expect(resolveViteHmrConfig({ [meridianScreenshotCaptureEnv]: "1" })).toBe(false);
  });

  it("builds /api proxy options for a custom Meridian host", () => {
    const proxy = createMeridianApiProxy("http://localhost:8181");
    const apiProxy = proxy["/api"];

    expect(apiProxy.target).toBe("http://localhost:8181");
    expect(apiProxy.changeOrigin).toBe(true);
    expect(apiProxy.secure).toBe(false);
    expect(apiProxy.bypass).toBeTypeOf("function");
  });

  it("serves typed dev fixtures before proxying GETs when the API host is unavailable", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const response = new FakeResponse();
    const breakQueueResponse = new FakeResponse();
    const readinessResponse = new FakeResponse();
    const scopedInboxResponse = new FakeResponse();

    const result = await bypass(
      { method: "GET", url: WORKSTATION_API_ENDPOINTS.session, headers: { accept: "application/json" } } as IncomingMessage,
      response as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(result).toBe(WORKSTATION_API_ENDPOINTS.session);
    expect(response.statusCode).toBe(200);
    expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
    expect(response.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(response.body)).toMatchObject({ displayName: "Ops Desk" });

    await bypass(
      { method: "GET", url: RECONCILIATION_API_ENDPOINTS.breakQueue, headers: { accept: "application/json" } } as IncomingMessage,
      breakQueueResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(breakQueueResponse.statusCode).toBe(200);
    expect(breakQueueResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(breakQueueResponse.body)).toEqual([
      expect.objectContaining({ breakId: "run-42:cash", status: "Open" })
    ]);

    await bypass(
      {
        method: "GET",
        url: `${WORKSTATION_API_ENDPOINTS.tradingReadiness}?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749`,
        headers: { accept: "application/json" }
      } as IncomingMessage,
      readinessResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      {
        method: "GET",
        url: workstationOperatorInboxEndpoint("53bf0251-17f6-4fb7-8dbe-6fb4966e2749"),
        headers: { accept: "application/json" }
      } as IncomingMessage,
      scopedInboxResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(readinessResponse.statusCode).toBe(200);
    expect(readinessResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(readinessResponse.body)).toMatchObject({
      activeSession: expect.objectContaining({ sessionId: "paper-dev-42" })
    });
    expect(scopedInboxResponse.statusCode).toBe(200);
    expect(scopedInboxResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(scopedInboxResponse.body)).toMatchObject({
      summary: expect.stringContaining("operator review items")
    });
  });

  it("serves seeded market-data fixtures for the no-host quote demo path", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const quoteResponse = new FakeResponse();
    const historyResponse = new FakeResponse();
    const symbolsResponse = new FakeResponse();
    const snapshotResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: marketDataQuoteEndpoint("AAPL"), headers: { accept: "application/json" } } as IncomingMessage,
      quoteResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      {
        method: "GET",
        url: historicalBarsEndpoint("AAPL", { intervalMinutes: 5 }),
        headers: { accept: "application/json" }
      } as IncomingMessage,
      historyResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: SYMBOL_API_ENDPOINTS.symbols, headers: { accept: "application/json" } } as IncomingMessage,
      symbolsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      {
        method: "GET",
        url: marketDataQuotesSnapshotEndpoint(["AAPL", "MSFT"]),
        headers: { accept: "application/json" }
      } as IncomingMessage,
      snapshotResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(quoteResponse.statusCode).toBe(200);
    expect(quoteResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(quoteResponse.body)).toMatchObject({
      symbol: "AAPL",
      quote: {
        bidPrice: 188.05,
        askPrice: 188.07,
        venue: "NASDAQ"
      }
    });
    expect(JSON.parse(historyResponse.body)).toMatchObject({
      symbol: "AAPL",
      intervalMinutes: 5,
      totalBars: 12
    });
    expect(JSON.parse(symbolsResponse.body)).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ symbol: "AAPL", status: "Active" }),
        expect.objectContaining({ symbol: "MSFT", status: "Active" })
      ])
    );
    expect(JSON.parse(snapshotResponse.body)).toMatchObject({
      count: 2,
      quotes: [
        expect.objectContaining({ symbol: "AAPL", lastPrice: 188.06 }),
        expect.objectContaining({ symbol: "MSFT", lastPrice: 421.15 })
      ]
    });
  });

  it("serves first-render Accounting and Portfolio shared fixtures in no-host mode", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const portfolioExplorerResponse = new FakeResponse();
    const ledgerExplorerResponse = new FakeResponse();
    const securityExplorerResponse = new FakeResponse();
    const accountingConfigurationResponse = new FakeResponse();
    const statementRunsResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: workstationFinancialRecordExplorerEndpoint("portfolio"), headers: { accept: "application/json" } } as IncomingMessage,
      portfolioExplorerResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: workstationFinancialRecordExplorerEndpoint("ledger"), headers: { accept: "application/json" } } as IncomingMessage,
      ledgerExplorerResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: workstationFinancialRecordExplorerEndpoint("security-instrument"), headers: { accept: "application/json" } } as IncomingMessage,
      securityExplorerResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: WORKSTATION_API_ENDPOINTS.accountingConfiguration, headers: { accept: "application/json" } } as IncomingMessage,
      accountingConfigurationResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: RECONCILIATION_API_ENDPOINTS.statementRuns, headers: { accept: "application/json" } } as IncomingMessage,
      statementRunsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    for (const response of [
      portfolioExplorerResponse,
      ledgerExplorerResponse,
      securityExplorerResponse,
      accountingConfigurationResponse,
      statementRunsResponse
    ]) {
      expect(response.statusCode).toBe(200);
      expect(response.headers.get(meridianDevFixtureHeader)).toBe("true");
    }

    expect(JSON.parse(portfolioExplorerResponse.body)).toMatchObject({
      explorerId: "portfolio",
      rows: expect.arrayContaining([expect.objectContaining({ recordId: "portfolio:portfolio-run-dev-1:AAPL" })])
    });
    expect(JSON.parse(ledgerExplorerResponse.body)).toMatchObject({
      explorerId: "ledger",
      rows: expect.arrayContaining([expect.objectContaining({ recordId: "ledger:run-42:cash" })])
    });
    expect(JSON.parse(securityExplorerResponse.body)).toMatchObject({
      explorerId: "security-instrument",
      rows: expect.arrayContaining([expect.objectContaining({ recordId: "security-instrument:sec-dev-001" })])
    });
    expect(JSON.parse(accountingConfigurationResponse.body)).toMatchObject({
      fundProfileId: "default-fund",
      status: "Active",
      ledgerBooks: [expect.objectContaining({ ledgerBookId: "ledger-book-default" })]
    });
    expect(JSON.parse(statementRunsResponse.body)).toEqual([
      expect.objectContaining({ runId: "stmt-run-42", openExceptionCount: 1 })
    ]);
  });

  it("covers first-run workstation support endpoints with no-host fixtures", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const endpoints = [
      PROVIDER_ROUTING_API_ENDPOINTS.connections,
      PROVIDER_ROUTING_API_ENDPOINTS.bindings,
      PROVIDER_ROUTING_API_ENDPOINTS.trustSnapshots,
      securityMasterAssetProfilesEndpoint(),
      workstationWorkflowSummaryEndpoint({ hasOperatingContext: false }),
      WORKSTATION_API_ENDPOINTS.featureCapabilities,
      AUTH_API_ENDPOINTS.accessAssignments,
      workstationProviderIntegrationConnectionMonitorEndpoint("databento", 5),
      workstationProviderIntegrationConnectionSyncRunsEndpoint("databento", 5),
      workstationProviderIntegrationConnectionSyncPlanEndpoint("databento", "2026-06-16T12:35:00Z"),
      workstationProviderIntegrationStagingReviewEndpoint("databento", 5),
      workstationProviderIntegrationIdentityResolutionEndpoint("databento", 5),
      workstationProviderIntegrationPromotionReadinessEndpoint("databento", 5),
      workstationProviderIntegrationReconciliationHandoffHistoryEndpoint("databento"),
      workstationProviderIntegrationQuarantineReviewEndpoint("databento", 5),
      RISK_API_ENDPOINTS.rules,
      riskRuleConfigEndpoint("DrawdownCircuitBreaker")
    ];

    const responses = await Promise.all(
      endpoints.map(async (endpoint) => {
        const response = new FakeResponse();

        await bypass(
          { method: "GET", url: endpoint, headers: { accept: "application/json" } } as IncomingMessage,
          response as unknown as ServerResponse,
          {} as ProxyOptions
        );

        return response;
      })
    );

    for (const response of responses) {
      expect(response.statusCode).toBe(200);
      expect(response.headers.get(meridianDevFixtureHeader)).toBe("true");
    }

    expect(JSON.parse(responses[0]?.body ?? "[]")).toEqual([
      expect.objectContaining({ connectionId: "provider-alpaca-paper", productionReady: false }),
      expect.objectContaining({ connectionId: "provider-reference", productionReady: false })
    ]);
    expect(JSON.parse(responses[1]?.body ?? "[]")).toEqual([
      expect.objectContaining({ bindingId: "provider-alpaca-paper-RealtimeMarketData" }),
      expect.objectContaining({ bindingId: "provider-reference-ReferenceData" })
    ]);
    expect(JSON.parse(responses[2]?.body ?? "[]")).toEqual([
      expect.objectContaining({ connectionId: "provider-alpaca-paper", isProductionReady: false }),
      expect.objectContaining({ connectionId: "provider-reference", isProductionReady: false })
    ]);
    expect(JSON.parse(responses[3]?.body ?? "[]")).toEqual([
      expect.objectContaining({ profileId: "fixture-public-equity", status: "Approved" })
    ]);
    expect(JSON.parse(responses[4]?.body ?? "{}")).toMatchObject({
      hasOperatingContext: false,
      operatingContextLabel: "No-host fixture workspace"
    });
    expect(JSON.parse(responses[5]?.body ?? "{}")).toMatchObject({
      capabilities: expect.arrayContaining([
        expect.objectContaining({
          capabilityKey: "desktop.settings.provider-connection-center-inline-management",
          isEnabled: true
        })
      ])
    });
    expect(JSON.parse(responses[6]?.body ?? "[]")).toEqual([]);
    expect(JSON.parse(responses[7]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      recentRecordsQuarantined: 2
    });
    expect(JSON.parse(responses[8]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      totalSyncRuns: 1
    });
    expect(JSON.parse(responses[9]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      dueCount: 1
    });
    expect(JSON.parse(responses[10]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      totalStagedRecords: 1
    });
    expect(JSON.parse(responses[11]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      totalRows: 1
    });
    expect(JSON.parse(responses[12]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      readyForReconciliationCount: 1
    });
    expect(JSON.parse(responses[13]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      handoffCount: 1
    });
    expect(JSON.parse(responses[14]?.body ?? "{}")).toMatchObject({
      connectionId: "databento",
      totalQuarantinedRecords: 1
    });
    expect(JSON.parse(responses[15]?.body ?? "[]")).toEqual([
      expect.objectContaining({ ruleName: "DrawdownCircuitBreaker", state: "Observe" }),
      expect.objectContaining({ ruleName: "PositionLimit", state: "Healthy" })
    ]);
    expect(JSON.parse(responses[16]?.body ?? "{}")).toMatchObject({
      ruleName: "DrawdownCircuitBreaker",
      maxDrawdownPercent: 8
    });
  });

  it("serves accounting-record evidence fixtures for no-host Evidence Workbench demos", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const subjectId = "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6";
    const subjectsResponse = new FakeResponse();
    const packetResponse = new FakeResponse();
    const validationResponse = new FakeResponse();
    const exportResponse = new FakeResponse();
    const vaultSearchResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: WORKSTATION_API_ENDPOINTS.evidenceSubjects, headers: { accept: "application/json" } } as IncomingMessage,
      subjectsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: workstationEvidencePacketEndpoint("accounting-record", subjectId), headers: { accept: "application/json" } } as IncomingMessage,
      packetResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "POST", url: workstationEvidenceValidateEndpoint("accounting-record", subjectId), headers: { accept: "application/json" } } as IncomingMessage,
      validationResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "POST", url: workstationEvidenceExportManifestEndpoint("accounting-record", subjectId), headers: { accept: "application/json" } } as IncomingMessage,
      exportResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "POST", url: WORKSTATION_API_ENDPOINTS.evidenceVaultSearch, headers: { accept: "application/json" } } as IncomingMessage,
      vaultSearchResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(JSON.parse(subjectsResponse.body)).toEqual([
      expect.objectContaining({ subjectKind: "accounting-record", subjectId, workspace: "Accounting" })
    ]);
    expect(JSON.parse(packetResponse.body)).toMatchObject({
      subject: { subjectKind: "accounting-record", subjectId },
      completeness: { status: "ReviewRequired", score: 63 }
    });
    expect(JSON.parse(validationResponse.body)).toMatchObject({
      status: "ReviewRequired",
      missingIds: expect.arrayContaining(["accounting-record:exports"])
    });
    expect(JSON.parse(exportResponse.body)).toMatchObject({
      subjectKind: "accounting-record",
      vaultIdentity: expect.objectContaining({ subjectId })
    });
    expect(JSON.parse(vaultSearchResponse.body)).toEqual([
      expect.objectContaining({ subjectKind: "accounting-record", subjectId })
    ]);
    expect(vaultSearchResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
  });

  it("serves Security Master search and drill-in fixtures for no-host preview", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const searchResponse = new FakeResponse();
    const identityResponse = new FakeResponse();
    const overridesResponse = new FakeResponse();
    const actionsResponse = new FakeResponse();
    const parametersResponse = new FakeResponse();

    await bypass(
      {
        method: "GET",
        url: workstationSecurityMasterSearchEndpoint({ query: "AAPL", take: 25, activeOnly: true }),
        headers: { accept: "application/json" }
      } as IncomingMessage,
      searchResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: workstationSecurityMasterIdentityEndpoint("sec-dev-001"), headers: { accept: "application/json" } } as IncomingMessage,
      identityResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: securityMasterOperatorOverridesEndpoint("sec-dev-001"), headers: { accept: "application/json" } } as IncomingMessage,
      overridesResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: securityMasterCorporateActionsEndpoint("sec-dev-001"), headers: { accept: "application/json" } } as IncomingMessage,
      actionsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: securityMasterTradingParametersEndpoint("sec-dev-001"), headers: { accept: "application/json" } } as IncomingMessage,
      parametersResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(searchResponse.statusCode).toBe(200);
    expect(searchResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(searchResponse.body)).toEqual([
      expect.objectContaining({ securityId: "sec-dev-001", displayName: "Apple Inc." })
    ]);
    expect(JSON.parse(identityResponse.body)).toMatchObject({
      securityId: "sec-dev-001",
      identifiers: expect.arrayContaining([expect.objectContaining({ kind: "Ticker", value: "AAPL" })])
    });
    expect(JSON.parse(overridesResponse.body)).toMatchObject({
      securityId: "sec-dev-001",
      values: expect.objectContaining({ issuer: "Apple Inc.", couponRate: "0.25" })
    });
    expect(JSON.parse(actionsResponse.body)).toEqual(expect.arrayContaining([
      expect.objectContaining({ securityId: "sec-dev-001", eventType: "Dividend" })
    ]));
    expect(JSON.parse(parametersResponse.body)).toMatchObject({ securityId: "sec-dev-001", lotSize: 1 });
  });

  it("serves Quant Lab bootstrap fixtures for no-host preview without opening mutation routes", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const templatesResponse = new FakeResponse();
    const parametersResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: QUANT_API_ENDPOINTS.templates, headers: { accept: "application/json" } } as IncomingMessage,
      templatesResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "POST", url: QUANT_API_ENDPOINTS.parameters, headers: { accept: "application/json" } } as IncomingMessage,
      parametersResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(templatesResponse.statusCode).toBe(200);
    expect(templatesResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(templatesResponse.body)).toMatchObject({
      templates: [
        expect.objectContaining({ id: "hello-quant-lab" }),
        expect.objectContaining({ id: "parameter-sweep-preview" })
      ]
    });
    expect(parametersResponse.statusCode).toBe(200);
    expect(parametersResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(parametersResponse.body)).toMatchObject({
      parameters: [
        expect.objectContaining({ name: "lookback", typeName: "int" }),
        expect.objectContaining({ name: "includeFees", typeName: "bool" })
      ]
    });
  });

  it("serves Strategy Designer evidence fixtures for no-host browser previews", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const templatesResponse = new FakeResponse();
    const fieldCatalogResponse = new FakeResponse();
    const draftsResponse = new FakeResponse();
    const draftResponse = new FakeResponse();
    const runBacktestResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: STRATEGY_DESIGNER_API_ENDPOINTS.templates, headers: { accept: "application/json" } } as IncomingMessage,
      templatesResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog, headers: { accept: "application/json" } } as IncomingMessage,
      fieldCatalogResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: STRATEGY_DESIGNER_API_ENDPOINTS.drafts, headers: { accept: "application/json" } } as IncomingMessage,
      draftsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: `${STRATEGY_DESIGNER_API_ENDPOINTS.drafts}/strategy-designer-fixture-1`, headers: { accept: "application/json" } } as IncomingMessage,
      draftResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    const mutationResult = await bypass(
      { method: "POST", url: STRATEGY_DESIGNER_API_ENDPOINTS.runBacktest, headers: { accept: "application/json" } } as IncomingMessage,
      runBacktestResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(templatesResponse.statusCode).toBe(200);
    expect(templatesResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(templatesResponse.body)).toEqual([
      expect.objectContaining({
        templateId: "quality-momentum-rotation",
        document: expect.objectContaining({ documentId: "strategy-designer-fixture-1" })
      })
    ]);
    expect(fieldCatalogResponse.statusCode).toBe(200);
    expect(JSON.parse(fieldCatalogResponse.body)).toEqual(expect.arrayContaining([
      expect.objectContaining({ fieldId: "MOMENTUM_63D", isEnabled: true }),
      expect.objectContaining({ fieldId: "AMX_PRIVATE_SCORE", disabledReason: "No Meridian canonical source" })
    ]));
    expect(draftsResponse.statusCode).toBe(200);
    expect(JSON.parse(draftsResponse.body)).toEqual([
      expect.objectContaining({ documentId: "strategy-designer-fixture-1", validationSummary: "Fixture draft passes no-host validation." })
    ]);
    expect(draftResponse.statusCode).toBe(200);
    expect(JSON.parse(draftResponse.body)).toMatchObject({
      documentId: "strategy-designer-fixture-1",
      cells: expect.arrayContaining([expect.objectContaining({ cellId: "momentum-score" })])
    });
    expect(mutationResult).toBeUndefined();
    expect(runBacktestResponse.writableEnded).toBe(false);
  });

  it("exposes Strategy Engine endpoints through the typed catalog", () => {
    expect(STRATEGY_ENGINE_API_ENDPOINTS.definitions).toBe("/api/workstation/strategy/engine/definitions");
    expect(STRATEGY_ENGINE_API_ENDPOINTS.validateRun).toBe("/api/workstation/strategy/engine/validate-run");
  });

  it("serves Covered Call preview fixtures for no-host strategy demos without opening run mutations", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const runsResponse = new FakeResponse();
    const runResultResponse = new FakeResponse();
    const chainPreviewResponse = new FakeResponse();
    const startRunResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: `${COVERED_CALL_API_ENDPOINTS.runs}?limit=50`, headers: { accept: "application/json" } } as IncomingMessage,
      runsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: COVERED_CALL_API_ENDPOINTS.runResult("covered-call-dev-1"), headers: { accept: "application/json" } } as IncomingMessage,
      runResultResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "POST", url: COVERED_CALL_API_ENDPOINTS.chainPreview, headers: { accept: "application/json" } } as IncomingMessage,
      chainPreviewResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    const mutationResult = await bypass(
      { method: "POST", url: COVERED_CALL_API_ENDPOINTS.runs, headers: { accept: "application/json" } } as IncomingMessage,
      startRunResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(runsResponse.statusCode).toBe(200);
    expect(runsResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(runsResponse.body)).toEqual([
      expect.objectContaining({ runId: "covered-call-dev-1", underlyingSymbol: "SPY" })
    ]);
    expect(runResultResponse.statusCode).toBe(200);
    expect(runResultResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(runResultResponse.body)).toMatchObject({
      runId: "covered-call-dev-1",
      metrics: expect.objectContaining({ sharpeRatio: 1.18 }),
      openPositionsAtEnd: expect.arrayContaining([
        expect.objectContaining({ positionId: "covered-call-dev-open-1" })
      ])
    });
    expect(chainPreviewResponse.statusCode).toBe(200);
    expect(chainPreviewResponse.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(chainPreviewResponse.body)).toMatchObject({
      underlyingSymbol: "SPY",
      filtersPassed: 2,
      candidates: expect.arrayContaining([
        expect.objectContaining({ strike: 515, meetsAllFilters: true }),
        expect.objectContaining({ rejectReason: "Open interest below minimum" })
      ])
    });
    expect(mutationResult).toBeUndefined();
    expect(startRunResponse.writableEnded).toBe(false);
  });

  it("serves Trading cockpit support fixtures for no-host demo smoke", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const sessionsResponse = new FakeResponse();
    const detailResponse = new FakeResponse();
    const replayResponse = new FakeResponse();
    const replayFilesResponse = new FakeResponse();
    const auditResponse = new FakeResponse();
    const controlsResponse = new FakeResponse();
    const promotionEvaluateResponse = new FakeResponse();
    const promotionHistoryResponse = new FakeResponse();

    await bypass(
      { method: "GET", url: EXECUTION_API_ENDPOINTS.sessions, headers: { accept: "application/json" } } as IncomingMessage,
      sessionsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: executionSessionEndpoint("paper-dev-42"), headers: { accept: "application/json" } } as IncomingMessage,
      detailResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: executionSessionReplayEndpoint("paper-dev-42"), headers: { accept: "application/json" } } as IncomingMessage,
      replayResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: REPLAY_API_ENDPOINTS.files, headers: { accept: "application/json" } } as IncomingMessage,
      replayFilesResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: executionAuditEndpoint(8), headers: { accept: "application/json" } } as IncomingMessage,
      auditResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: EXECUTION_API_ENDPOINTS.controls, headers: { accept: "application/json" } } as IncomingMessage,
      controlsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: promotionEvaluateEndpoint("run-dev-2"), headers: { accept: "application/json" } } as IncomingMessage,
      promotionEvaluateResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: PROMOTION_API_ENDPOINTS.history, headers: { accept: "application/json" } } as IncomingMessage,
      promotionHistoryResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(JSON.parse(sessionsResponse.body)).toEqual([
      expect.objectContaining({ sessionId: "paper-dev-42", isActive: true })
    ]);
    expect(JSON.parse(detailResponse.body)).toMatchObject({
      summary: expect.objectContaining({ sessionId: "paper-dev-42" }),
      symbols: ["AAPL", "MSFT", "NVDA"]
    });
    expect(JSON.parse(replayResponse.body)).toMatchObject({
      isConsistent: true,
      verificationAuditId: "audit-replay-dev-42"
    });
    expect(JSON.parse(replayFilesResponse.body)).toMatchObject({
      total: 1,
      files: [expect.objectContaining({ name: "paper-dev-42.jsonl" })]
    });
    expect(JSON.parse(auditResponse.body)).toEqual([
      expect.objectContaining({ auditId: "audit-replay-dev-42" })
    ]);
    expect(JSON.parse(controlsResponse.body)).toMatchObject({
      circuitBreaker: { isOpen: false },
      manualOverrides: [expect.objectContaining({ overrideId: "override-fixture-1" })]
    });
    expect(JSON.parse(promotionEvaluateResponse.body)).toMatchObject({
      runId: "run-dev-2",
      isEligible: true,
      reason: "Promotion gates passed."
    });
    expect(JSON.parse(promotionHistoryResponse.body)).toEqual([
      expect.objectContaining({ promotionId: "promo-dev-1", targetRunId: "paper-dev-42" })
    ]);
  });

  it("keeps live API proxying when the Meridian host is available", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => true
    });
    const response = new FakeResponse();

    const result = await bypass(
      { method: "GET", url: WORKSTATION_API_ENDPOINTS.session, headers: { accept: "application/json" } } as IncomingMessage,
      response as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(result).toBeUndefined();
    expect(response.writableEnded).toBe(false);
  });

  it("does not fixture mutation requests when the API host is unavailable", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => false
    });
    const response = new FakeResponse();

    const result = await bypass(
      { method: "POST", url: WORKSTATION_API_ENDPOINTS.workflowPresets, headers: { accept: "application/json" } } as IncomingMessage,
      response as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(result).toBeUndefined();
    expect(response.writableEnded).toBe(false);
  });
});

class FakeResponse {
  statusCode = 0;
  headers = new Map<string, string>();
  body = "";
  headersSent = false;
  writableEnded = false;

  setHeader(name: string, value: number | string | readonly string[]) {
    this.headers.set(name.toLowerCase(), Array.isArray(value) ? value.join(",") : String(value));
    return this;
  }

  end(body?: string) {
    this.body = body ?? "";
    this.headersSent = true;
    this.writableEnded = true;
    return this;
  }
}
