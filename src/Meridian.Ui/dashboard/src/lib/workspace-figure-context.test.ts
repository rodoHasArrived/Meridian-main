import { describe, expect, it } from "vitest";
import { deriveWorkspaceFigureContext, resolveWorkspaceFigures } from "@/lib/workspace-figure-context";

const loaded = { phase: "succeeded", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:05:00Z" } as const;

describe("deriveWorkspaceFigureContext", () => {
  it("reports a loaded real-data session as fresh", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "real" }))
      .toEqual({ freshness: "fresh", dataLabel: null, staleWorkspaces: [], workspaceLoadedAt: {} });
  });

  it("marks figures stale when the latest refresh failed and the last good load is still shown", () => {
    expect(deriveWorkspaceFigureContext({ status: { ...loaded, phase: "failed", settledAt: "2026-06-30T18:10:00Z" }, provenance: "real" }).freshness)
      .toBe("stale");
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

describe("resolveWorkspaceFigures", () => {
  // The latest refresh settled at 18:10 as partial. It reloaded Strategy; Accounting's reload
  // failed, so its figures are still the 17:40 load; Data was last reloaded at 17:55 and was not
  // part of this refresh. The lifecycle's own times (18:05, 18:10) belong to none of them.
  const context = deriveWorkspaceFigureContext({
    status: { phase: "partial", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:10:00Z" },
    provenance: "real",
    workspaceErrors: { accounting: "Accounting workspace request failed.", data: undefined },
    workspaceLoadedAt: { accounting: "2026-06-30T17:40:00Z", data: "2026-06-30T17:55:00Z", strategy: "2026-06-30T18:10:00Z" }
  });

  it("lists only the workspaces whose refresh failed", () => {
    expect(context.staleWorkspaces).toEqual(["accounting"]);
  });

  it("dates a workspace's figures from its own load, not from a refresh of another workspace", () => {
    expect(resolveWorkspaceFigures(context, "data")).toEqual({ asOf: "2026-06-30T17:55:00Z", freshness: "fresh" });
    expect(resolveWorkspaceFigures(context, "strategy")).toEqual({ asOf: "2026-06-30T18:10:00Z", freshness: "fresh" });
  });

  it("keeps the failed workspace's retained figures stale as of the load that produced them", () => {
    expect(resolveWorkspaceFigures(context, "accounting")).toEqual({ asOf: "2026-06-30T17:40:00Z", freshness: "stale" });
  });

  it("states no as-of time for a workspace with no recorded load or a strip that names none", () => {
    expect(resolveWorkspaceFigures(context, "settings")).toEqual({ asOf: null, freshness: "fresh" });
    expect(resolveWorkspaceFigures(context)).toEqual({ asOf: null, freshness: "fresh" });
  });

  it("stays stale for a workspace whose own later refresh failed after a full success", () => {
    const later = deriveWorkspaceFigureContext({ status: loaded, provenance: "real", workspaceErrors: { trading: "Trading refresh failed." } });

    expect(resolveWorkspaceFigures(later, "trading").freshness).toBe("stale");
    expect(resolveWorkspaceFigures(later, "portfolio").freshness).toBe("fresh");
  });

  it("resolves nothing outside the shell", () => {
    expect(resolveWorkspaceFigures(null, "accounting")).toEqual({ asOf: null, freshness: null });
  });
});
