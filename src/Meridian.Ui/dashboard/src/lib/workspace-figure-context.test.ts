import { describe, expect, it } from "vitest";
import { deriveWorkspaceFigureContext, resolveFigureEvidence, resolveFigureScope } from "@/lib/workspace-figure-context";

const loaded = { phase: "succeeded", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:05:00Z" } as const;

describe("deriveWorkspaceFigureContext", () => {
  it("reports a loaded real-data session as fresh", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "real" }))
      .toEqual({ freshness: "fresh", dataLabel: null, staleSources: [], sourceLoadedAt: {}, sourceScope: {} });
  });

  it("leaves staleness to each payload's own reload outcome when a refresh failed", () => {
    // Every request of the 18:10 refresh failed. The overview is retained from 18:05 and stale;
    // Trading was reloaded by its own poll at 18:12 and is current again.
    const context = deriveWorkspaceFigureContext({
      status: { ...loaded, phase: "failed", settledAt: "2026-06-30T18:10:00Z" },
      provenance: "real",
      staleSources: ["overview"],
      sourceLoadedAt: { overview: "2026-06-30T18:05:00Z", trading: "2026-06-30T18:12:00Z" }
    });

    expect(resolveFigureEvidence(context, "overview")).toEqual({ asOf: "2026-06-30T18:05:00Z", freshness: "stale" });
    expect(resolveFigureEvidence(context, "trading")).toEqual({ asOf: "2026-06-30T18:12:00Z", freshness: "fresh" });
  });

  it("keeps every figure unverified while the session's data source is unconfirmed", () => {
    const context = deriveWorkspaceFigureContext({
      status: loaded,
      provenance: "unknown",
      staleSources: ["accounting"],
      sourceLoadedAt: { accounting: "2026-06-30T17:40:00Z", trading: "2026-06-30T18:05:00Z" }
    });

    expect(resolveFigureEvidence(context, "trading")).toEqual({ asOf: "2026-06-30T18:05:00Z", freshness: "unverified" });
    expect(resolveFigureEvidence(context, "accounting").freshness).toBe("stale");
  });

  it("marks figures unverified when the data source is unconfirmed or nothing ever loaded", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "unknown" }))
      .toMatchObject({ freshness: "unverified", dataLabel: "data source unverified" });
    expect(deriveWorkspaceFigureContext({ status: { phase: "idle", inFlight: false, lastSucceededAt: null, settledAt: null }, provenance: "real" }).freshness)
      .toBe("unverified");
  });

  it("claims nothing while the first load is still in flight", () => {
    expect(deriveWorkspaceFigureContext({ status: { phase: "running", inFlight: true, lastSucceededAt: null, settledAt: null }, provenance: "real" }).freshness)
      .toBeNull();
  });

  it("counts a first refresh that only partly succeeded as a load", () => {
    expect(deriveWorkspaceFigureContext({ status: { phase: "partial", inFlight: false, lastSucceededAt: null, settledAt: "2026-06-30T18:10:00Z" }, provenance: "real" }).freshness)
      .toBe("fresh");
  });

  it("labels non-real sessions so a figure's source says what kind of data it is", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "simulated" }).dataLabel).toBe("simulated data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "seeded" }).dataLabel).toBe("seeded demo data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "sample" }).dataLabel).toBe("sample data");
  });
});

describe("resolveFigureEvidence", () => {
  // The latest refresh settled at 18:10 as partial. It reloaded Strategy; Accounting's reload
  // failed, so its figures are still the 17:40 load; Data was last reloaded at 17:55 and was not
  // part of this refresh. The lifecycle's own times (18:05, 18:10) belong to none of them.
  const context = deriveWorkspaceFigureContext({
    status: { phase: "partial", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:10:00Z" },
    provenance: "real",
    staleSources: ["accounting"],
    sourceLoadedAt: { accounting: "2026-06-30T17:40:00Z", data: "2026-06-30T17:55:00Z", strategy: "2026-06-30T18:10:00Z" }
  });

  it("lists only the payloads whose reload failed", () => {
    expect(context.staleSources).toEqual(["accounting"]);
  });

  it("dates a workspace's figures from its own load, not from a refresh of another workspace", () => {
    expect(resolveFigureEvidence(context, "data")).toEqual({ asOf: "2026-06-30T17:55:00Z", freshness: "fresh" });
    expect(resolveFigureEvidence(context, "strategy")).toEqual({ asOf: "2026-06-30T18:10:00Z", freshness: "fresh" });
  });

  it("keeps the failed workspace's retained figures stale as of the load that produced them", () => {
    expect(resolveFigureEvidence(context, "accounting")).toEqual({ asOf: "2026-06-30T17:40:00Z", freshness: "stale" });
  });

  it("states no as-of time for a payload with no recorded load or when none is named", () => {
    expect(resolveFigureEvidence(context, "overview")).toEqual({ asOf: null, freshness: "fresh" });
    expect(resolveFigureEvidence(context)).toEqual({ asOf: null, freshness: "fresh" });
  });

  it("stays stale for a workspace whose reload failed even after a later refresh succeeded", () => {
    const later = deriveWorkspaceFigureContext({ status: loaded, provenance: "real", staleSources: ["trading"] });

    expect(resolveFigureEvidence(later, "trading").freshness).toBe("stale");
    expect(resolveFigureEvidence(later, "portfolio").freshness).toBe("fresh");
  });

  it("reports the account a payload was loaded for, distinguishing every account from not recorded", () => {
    const scoped = deriveWorkspaceFigureContext({
      status: loaded,
      provenance: "real",
      sourceScope: { trading: "11111111-1111-1111-1111-111111111111", crossScreenCounts: null }
    });

    expect(resolveFigureScope(scoped, "trading")).toBe("11111111-1111-1111-1111-111111111111");
    expect(resolveFigureScope(scoped, "crossScreenCounts")).toBeNull();
    expect(resolveFigureScope(scoped, "portfolio")).toBeUndefined();
    expect(resolveFigureScope(null, "trading")).toBeUndefined();
  });

  it("resolves nothing outside the shell", () => {
    expect(resolveFigureEvidence(null, "accounting")).toEqual({ asOf: null, freshness: null });
  });
});
