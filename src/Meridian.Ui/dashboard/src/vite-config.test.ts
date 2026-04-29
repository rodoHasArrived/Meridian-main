// @vitest-environment node

import { describe, expect, it } from "vitest";
import config, {
  createMeridianApiProxy,
  defaultMeridianApiBaseUrl,
  resolveMeridianApiBaseUrl
} from "../vite.config";
import type { ProxyOptions, UserConfig } from "vite";

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
  });
});
