import { describe, expect, it } from "vitest";
import { deriveWorkspaceFigureContext, resolveWorkspaceFigures } from "@/lib/workspace-figure-context";

const loaded = { phase: "succeeded", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:05:00Z" } as const;

describe("deriveWorkspaceFigureContext", () => {
  it("reports a loaded real-data session as fresh with its load time", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "real" }))
      .toEqual({ asOf: "2026-06-30T18:05:00Z", freshness: "fresh", dataLabel: null, staleWorkspaces: [], staleAsOf: "2026-06-30T18:05:00Z" });
  });

  it("marks figures stale when the latest refresh failed and the last good load is still shown", () => {
    expect(deriveWorkspaceFigureContext({ status: { ...loaded, phase: "failed", settledAt: "2026-06-30T18:10:00Z" }, provenance: "real" }))
      .toMatchObject({ asOf: "2026-06-30T18:05:00Z", freshness: "stale" });
  });

  it("marks figures unverified when the data source is unconfirmed or nothing ever loaded", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "unknown" }))
      .toMatchObject({ freshness: "unverified", dataLabel: "data source unverified" });
    expect(deriveWorkspaceFigureContext({ status: { phase: "idle", inFlight: false, lastSucceededAt: null, settledAt: null }, provenance: "real" }))
      .toMatchObject({ asOf: null, freshness: "unverified" });
  });

  it("claims nothing while the first load is still in flight", () => {
    expect(deriveWorkspaceFigureContext({ status: { phase: "running", inFlight: true, lastSucceededAt: null, settledAt: null }, provenance: "real" }).freshness)
      .toBeNull();
  });

  it("labels non-real sessions so a figure's source says what kind of data it is", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "simulated" }).dataLabel).toBe("simulated data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "seeded" }).dataLabel).toBe("seeded demo data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "sample" }).dataLabel).toBe("sample data");
  });
});

describe("a partially failed refresh", () => {
  // The accounting request failed at 18:10; everything else refreshed then. The lifecycle keeps
  // the 18:05 full success as `lastSucceededAt`.
  const partial = deriveWorkspaceFigureContext({
    status: { phase: "partial", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z", settledAt: "2026-06-30T18:10:00Z" },
    provenance: "real",
    workspaceErrors: { accounting: "Accounting workspace request failed.", data: undefined }
  });

  it("lists only the workspaces whose refresh failed", () => {
    expect(partial.staleWorkspaces).toEqual(["accounting"]);
    expect(partial.staleAsOf).toBe("2026-06-30T18:05:00Z");
  });

  it("keeps the failed workspace's retained figures stale as of the last full load", () => {
    expect(resolveWorkspaceFigures(partial, "accounting")).toEqual({ asOf: "2026-06-30T18:05:00Z", freshness: "stale" });
  });

  it("keeps workspaces that did refresh fresh as of that settle", () => {
    expect(resolveWorkspaceFigures(partial, "strategy")).toEqual({ asOf: "2026-06-30T18:10:00Z", freshness: "fresh" });
    expect(resolveWorkspaceFigures(partial)).toEqual({ asOf: "2026-06-30T18:10:00Z", freshness: "fresh" });
  });

  it("stays stale for a workspace whose own later refresh failed after a full success", () => {
    const context = deriveWorkspaceFigureContext({ status: loaded, provenance: "real", workspaceErrors: { trading: "Trading refresh failed." } });

    expect(resolveWorkspaceFigures(context, "trading").freshness).toBe("stale");
    expect(resolveWorkspaceFigures(context, "portfolio").freshness).toBe("fresh");
  });

  it("resolves nothing outside the shell", () => {
    expect(resolveWorkspaceFigures(null, "accounting")).toEqual({ asOf: null, freshness: null });
  });
});
