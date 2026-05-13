// @vitest-environment node

import { describe, expect, it } from "vitest";
import config, {
  createMeridianApiFallbackBypass,
  createMeridianApiProxy,
  defaultMeridianApiBaseUrl,
  meridianDevFixtureHeader,
  resolveMeridianApiBaseUrl
} from "../vite.config";
import type { ProxyOptions, UserConfig } from "vite";
import type { IncomingMessage, ServerResponse } from "node:http";
import {
  EXECUTION_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  executionAuditEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  historicalBarsEndpoint,
  marketDataQuoteEndpoint,
  marketDataQuotesSnapshotEndpoint,
  securityMasterCorporateActionsEndpoint,
  securityMasterOperatorOverridesEndpoint,
  securityMasterTradingParametersEndpoint,
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
