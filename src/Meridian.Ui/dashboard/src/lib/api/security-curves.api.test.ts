import { afterEach, describe, expect, it, vi } from "vitest";
import { defineSecurityCurve, listSecurityCurveVersions, publishSecurityCurve } from "./security-curves.api";

afterEach(() => vi.unstubAllGlobals());
describe("curve API contracts", () => {
  it("retains exact decimal input and immutable identity without supplying an actor", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response("{}", { status: 200 })); vi.stubGlobal("fetch", fetcher);
    const controller = new AbortController();
    const request = { curveId: "curve / id", asOf: "2026-11-01", points: [{ date: "2026-12-01", value: "0.1234567890123456789012345678", tenor: "1M" }], source: "Synthetic" };
    await publishSecurityCurve(request, { signal: controller.signal });
    const [path, options] = fetcher.mock.calls[0];
    expect(path).toBe("/api/security-master/curves/curve%20%2F%20id/versions");
    expect(JSON.parse(options.body)).toEqual(request); expect(options.signal).toBe(controller.signal);
    expect(JSON.parse(options.body)).not.toHaveProperty("publishedBy");
  });
  it("keeps historical decimal response text and propagates unavailable storage without fallback", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response('[{"points":[{"date":"2026-12-01","value":"0.1234567890123456789012345678"}]}]', { status: 200 }))
      .mockResolvedValueOnce(new Response('{"error":"Curve store unavailable"}', { status: 503 })); vi.stubGlobal("fetch", fetcher);
    expect((await listSecurityCurveVersions("curve"))[0].points[0].value).toBe("0.1234567890123456789012345678");
    await expect(defineSecurityCurve({ curveKey: "USD", kind: "Discount", currency: "USD", dayCount: "Actual365", interpolation: "Linear", source: "Synthetic" })).rejects.toMatchObject({ status: 503 });
    expect(fetcher).toHaveBeenCalledTimes(2);
  });
});
