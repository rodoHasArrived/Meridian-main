import { afterEach, describe, expect, it, vi } from "vitest";
import { getBondAnalytics } from "./bond-analytics.api";
import { previewBondMigration, previewBondMigrationBatch, stageBondMigration } from "./bond-migration.api";

describe("bond calculation API adapters", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("retains decimal query text, explicit dates and cancellation without fixture fallback", async () => {
    const controller = new AbortController();
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await getBondAnalytics(
      "bond /id",
      {
        asOf: "2026-10-29T00:00:00Z",
        settlementDate: "2026-10-30",
        knownAt: "2026-10-29T12:00:00Z",
        cleanPrice: "99.123456789012345678",
        yieldConvention: "AnnualEffective"
      },
      { signal: controller.signal }
    );
    const [path, options] = fetchMock.mock.calls[0];
    expect(path).toContain("/api/reference-data/bonds/bond%20%2Fid/analytics?");
    const params = new URL(String(path), "https://meridian.invalid").searchParams;
    expect(params.get("cleanPrice")).toBe("99.123456789012345678");
    expect(params.has("yield")).toBe(false);
    expect(params.get("knownAt")).toBe("2026-10-29T12:00:00Z");
    expect(options.signal).toBe(controller.signal);
  });

  it("passes authoritative errors through instead of returning seeded analytics", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response('{"detail":"Historical input unavailable"}', {
          status: 503
        })
      )
    );
    await expect(
      getBondAnalytics("bond", {
        asOf: "2026-10-29T00:00:00Z",
        settlementDate: "2026-10-30",
        yield: "-0.002"
      })
    ).rejects.toMatchObject({ status: 503 });
  });

  it("posts a read-only preview with reviewed conventions and expected terms version", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const request = {
      securityId: "bond",
      expectedVersion: 7,
      context: { asOf: "2026-10-29T00:00:00Z" },
      scheduleConventions: {
        couponRateUnit: "Percent" as const,
        couponCashBasis: "DayCount" as const,
        accrualAdjusted: false,
        businessDayConvention: "Unadjusted",
        endOfMonthRule: true
      }
    };
    await previewBondMigration(request);
    const [path, options] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/reference-data/bonds/migration-preview");
    expect(options.method).toBe("POST");
    expect(JSON.parse(options.body)).toEqual(request);
  });

  it("stages only reviewed request and opaque hashes without round-tripping financial projections", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const request = {
      securityId: "bond",
      expectedVersion: 7,
      context: { asOf: "2026-10-29T00:00:00Z" },
      scheduleConventions: {
        couponRateUnit: "Percent" as const,
        couponCashBasis: "DayCount" as const,
        accrualAdjusted: false,
        businessDayConvention: "Unadjusted",
        endOfMonthRule: true
      }
    };
    await stageBondMigration({
      request,
      previewSha256: "preview-hash",
      securityContentSha256: "security-hash",
      justification: "Reviewed contractual evidence."
    });
    const [path, options] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/reference-data/bonds/migration-submit");
    const body = JSON.parse(options.body);
    expect(body.request).toEqual(request);
    expect(body.previewSha256).toBe("preview-hash");
    expect(body).not.toHaveProperty("preview");
    expect(body).not.toHaveProperty("cashDifferences");
    expect(body).not.toHaveProperty("actor");
  });

  it("supports bounded bulk previews and rejects empty or oversized batches before sending", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("[]", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const request = {
      securityId: "bond",
      expectedVersion: 7,
      context: { asOf: "2026-10-29T00:00:00Z" },
      scheduleConventions: {
        couponRateUnit: "Percent" as const,
        couponCashBasis: "DayCount" as const,
        accrualAdjusted: false,
        businessDayConvention: "Unadjusted",
        endOfMonthRule: false
      }
    };
    await previewBondMigrationBatch([request]);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/reference-data/bonds/migration-preview/batch");
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
      bonds: [request]
    });
    await expect(previewBondMigrationBatch([])).rejects.toThrow("between 1 and 100");
    await expect(previewBondMigrationBatch(Array(101).fill(request))).rejects.toThrow("between 1 and 100");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
