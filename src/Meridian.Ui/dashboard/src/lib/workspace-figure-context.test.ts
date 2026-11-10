import { describe, expect, it } from "vitest";
import { deriveWorkspaceFigureContext } from "@/lib/workspace-figure-context";

const loaded = { phase: "succeeded", inFlight: false, lastSucceededAt: "2026-06-30T18:05:00Z" } as const;

describe("deriveWorkspaceFigureContext", () => {
  it("reports a loaded real-data session as fresh with its load time", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "real" }))
      .toEqual({ asOf: "2026-06-30T18:05:00Z", freshness: "fresh", dataLabel: null });
  });

  it("marks figures stale when the latest refresh failed and the last good load is still shown", () => {
    expect(deriveWorkspaceFigureContext({ status: { ...loaded, phase: "failed" }, provenance: "real" }))
      .toMatchObject({ asOf: "2026-06-30T18:05:00Z", freshness: "stale" });
  });

  it("marks figures unverified when the data source is unconfirmed or nothing ever loaded", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "unknown" }))
      .toMatchObject({ freshness: "unverified", dataLabel: "data source unverified" });
    expect(deriveWorkspaceFigureContext({ status: { phase: "idle", inFlight: false, lastSucceededAt: null }, provenance: "real" }))
      .toMatchObject({ asOf: null, freshness: "unverified" });
  });

  it("claims nothing while the first load is still in flight", () => {
    expect(deriveWorkspaceFigureContext({ status: { phase: "running", inFlight: true, lastSucceededAt: null }, provenance: "real" }).freshness)
      .toBeNull();
  });

  it("labels non-real sessions so a figure's source says what kind of data it is", () => {
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "simulated" }).dataLabel).toBe("simulated data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "seeded" }).dataLabel).toBe("seeded demo data");
    expect(deriveWorkspaceFigureContext({ status: loaded, provenance: "sample" }).dataLabel).toBe("sample data");
  });
});
