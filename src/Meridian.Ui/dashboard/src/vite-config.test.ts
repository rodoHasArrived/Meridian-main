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

    const result = await bypass(
      { method: "GET", url: "/api/workstation/session", headers: { accept: "application/json" } } as IncomingMessage,
      response as unknown as ServerResponse,
      {} as ProxyOptions
    );

    expect(result).toBe("/api/workstation/session");
    expect(response.statusCode).toBe(200);
    expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
    expect(response.headers.get(meridianDevFixtureHeader)).toBe("true");
    expect(JSON.parse(response.body)).toMatchObject({ displayName: "Ops Desk" });
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
      { method: "GET", url: "/api/data/quotes/AAPL", headers: { accept: "application/json" } } as IncomingMessage,
      quoteResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: "/api/historical/AAPL/bars?intervalMinutes=5", headers: { accept: "application/json" } } as IncomingMessage,
      historyResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: "/api/symbols", headers: { accept: "application/json" } } as IncomingMessage,
      symbolsResponse as unknown as ServerResponse,
      {} as ProxyOptions
    );
    await bypass(
      { method: "GET", url: "/api/data/quotes-snapshot?symbols=AAPL,MSFT", headers: { accept: "application/json" } } as IncomingMessage,
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

  it("keeps live API proxying when the Meridian host is available", async () => {
    const bypass = createMeridianApiFallbackBypass("http://localhost:8080", {
      isAvailable: async () => true
    });
    const response = new FakeResponse();

    const result = await bypass(
      { method: "GET", url: "/api/workstation/session", headers: { accept: "application/json" } } as IncomingMessage,
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
      { method: "POST", url: "/api/workstation/workflows/presets", headers: { accept: "application/json" } } as IncomingMessage,
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
