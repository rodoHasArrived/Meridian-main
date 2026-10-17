import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  developmentFixtureHeader,
  getSession,
  hasDevelopmentFixtureUsage,
  resetDevelopmentFixtureUsage,
  searchSecurities
} from "./api";

describe("API client development modes", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    vi.stubEnv("DEV", true);
    resetDevelopmentFixtureUsage();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it.each([404, 500, 502, 503, 504])("keeps connected backend HTTP %s errors visible", async (status) => {
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "backend-connected");
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: "Backend unavailable" }), { status }));

    await expect(getSession()).rejects.toMatchObject({ status, message: expect.stringContaining("Backend unavailable") });
    expect(hasDevelopmentFixtureUsage()).toBe(false);
  });

  it("keeps connected network failures visible", async () => {
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "backend-connected");
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));

    await expect(getSession()).rejects.toThrow("Failed to fetch");
    expect(hasDevelopmentFixtureUsage()).toBe(false);
  });

  it("preserves empty connected search results instead of inventing securities", async () => {
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "backend-connected");
    fetchMock.mockResolvedValue(new Response("[]"));

    await expect(searchSecurities("pcg")).resolves.toEqual([]);
  });

  it("retains the proxy fixture provenance in fixture-only mode", async () => {
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "fixture-only");
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ displayName: "Fixture Desk" }), {
      headers: { [developmentFixtureHeader]: "true" }
    }));

    await expect(getSession()).resolves.toMatchObject({ displayName: "Fixture Desk" });
    expect(hasDevelopmentFixtureUsage()).toBe(true);
  });

  it("does not replace fixture-only errors with a browser fixture", async () => {
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "fixture-only");
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: "Fixture unavailable" }), { status: 500 }));

    await expect(getSession()).rejects.toMatchObject({ status: 500 });
    expect(hasDevelopmentFixtureUsage()).toBe(false);
  });
});
