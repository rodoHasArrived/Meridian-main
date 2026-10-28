// @vitest-environment node

import { EventEmitter } from "node:events";
import type { IncomingMessage, ServerResponse } from "node:http";
import type { ViteDevServer } from "vite";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createMeridianScenarioPlugin } from "../vite.config";
import { accountingPayload, getScenario, scenarioPreviewUrl } from "./scenarios";

class ResponseRecorder extends EventEmitter {
  statusCode = 0;
  destroyed = false;
  body = "";
  headers = new Map<string, string>();
  setHeader(name: string, value: string) { this.headers.set(name.toLowerCase(), value); }
  end(body = "") { this.body = body; }
}

async function previewMiddleware() {
  const use = vi.fn();
  const configure = createMeridianScenarioPlugin().configureServer;
  if (typeof configure !== "function") throw new Error("Expected scenario configureServer hook.");
  await configure.call({} as never, { middlewares: { use } } as unknown as ViteDevServer);
  return use.mock.calls[0][0] as (req: IncomingMessage, res: ServerResponse, next: () => void) => Promise<void>;
}

async function serve(id?: string, method = "GET", url = "/api/workstation/accounting") {
  const middleware = await previewMiddleware();
  const res = new ResponseRecorder();
  const next = vi.fn();
  const referer = id === undefined ? "http://localhost/workstation/accounting" :
    `http://localhost${scenarioPreviewUrl(getScenario(id))}`;
  await middleware({ method, url, headers: { referer } } as IncomingMessage, res as unknown as ServerResponse, next);
  return { res, next };
}

afterEach(() => vi.useRealTimers());

describe("URL-selected Vite scenarios", () => {
  it.each([
    ["accounting.normal", 200], ["accounting.empty", 200],
    ["accounting.forbidden", 403], ["accounting.failed", 500]
  ])("serves %s with HTTP %s before proxying", async (id, status) => {
    const { res, next } = await serve(String(id));
    expect(res.statusCode).toBe(status);
    expect(res.headers.get("x-meridian-scenario")).toBe(id);
    expect(next).not.toHaveBeenCalled();
    if (id === "accounting.normal") expect(JSON.parse(res.body)).toEqual(accountingPayload);
    if (id === "accounting.empty") expect(JSON.parse(res.body).reconciliationQueue).toEqual([]);
  });

  it("keeps delays per request and releases disconnected requests", async () => {
    vi.useFakeTimers();
    const middleware = await previewMiddleware();
    const res = new ResponseRecorder();
    const pending = middleware({ method: "GET", url: "/api/workstation/accounting", headers: {
      referer: "http://localhost/workstation/accounting/reconciliation?scenario=accounting.delayed"
    } } as unknown as IncomingMessage, res as unknown as ServerResponse, vi.fn());
    expect(res.body).toBe("");
    expect(vi.getTimerCount()).toBe(1);
    res.destroyed = true;
    res.emit("close");
    await pending;
    expect(vi.getTimerCount()).toBe(0);
    expect(res.body).toBe("");
  });

  it.each([["GET", "/api/workstation/accounting/typo"], ["POST", "/api/workstation/accounting"]])(
    "diagnoses unexpected %s %s instead of forwarding it", async (method, url) => {
      const { res, next } = await serve("accounting.normal", method, url);
      expect(res.statusCode).toBe(501);
      expect(JSON.parse(res.body).detail).toContain(`${method} ${url}`);
      expect(JSON.parse(res.body).detail).toContain("accounting.normal");
      expect(next).not.toHaveBeenCalled();
    }
  );

  it("keeps failed HEAD status while omitting the body", async () => {
    const { res } = await serve("accounting.failed", "HEAD");
    expect(res.statusCode).toBe(500);
    expect(res.body).toBe("");
  });

  it("keeps simultaneous selectors independent and honors an explicit request selector", async () => {
    const [normal, failed] = await Promise.all([serve("accounting.normal"), serve("accounting.failed")]);
    expect(normal.res.statusCode).toBe(200);
    expect(failed.res.statusCode).toBe(500);
    const middleware = await previewMiddleware();
    const res = new ResponseRecorder();
    await middleware({ method: "GET", url: "/api/workstation/accounting", headers: {
      referer: "http://localhost/workstation/accounting?scenario=accounting.normal",
      "x-meridian-scenario": "accounting.forbidden"
    } } as unknown as IncomingMessage, res as unknown as ServerResponse, vi.fn());
    expect(res.statusCode).toBe(403);
  });

  it("leaves ordinary API calls and source imports to Vite", async () => {
    expect((await serve()).next).toHaveBeenCalledOnce();
    expect((await serve("accounting.normal", "GET", "/workstation/src/lib/api/example.ts")).next).toHaveBeenCalledOnce();
  });

  it("identifies an invalid scenario instead of silently selecting normal", async () => {
    const middleware = await previewMiddleware();
    const res = new ResponseRecorder();
    await middleware({ method: "GET", url: "/api/workstation/accounting", headers: {
      referer: "http://localhost/workstation/accounting?scenario=accounting.typo"
    } } as IncomingMessage, res as unknown as ServerResponse, vi.fn());
    expect(res.statusCode).toBe(400);
    expect(JSON.parse(res.body).detail).toContain("Unknown Meridian scenario");
  });

  it("diagnoses malformed preview selectors without an unhandled middleware rejection", async () => {
    const middleware = await previewMiddleware();
    const res = new ResponseRecorder();
    await middleware({ method: "GET", url: "/api/workstation/accounting", headers: {
      referer: "http://[invalid"
    } } as IncomingMessage, res as unknown as ServerResponse, vi.fn());
    expect(res.statusCode).toBe(400);
    expect(JSON.parse(res.body).title).toBe("Invalid preview scenario");
  });
});
