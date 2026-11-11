import { describe, expect, it } from "vitest";
import { buildTrustStripState } from "@/app-shell.trust-strip";
import type { DataWorkspaceResponse, SessionInfo } from "@/types";

const session: SessionInfo = {
  displayName: "Ops", role: "Operator", environment: "live", activeWorkspace: "trading", commandCount: 1
};
const options = {
  loading: false, bootstrapFailed: false, usingDevelopmentFixtures: false, workspaceErrors: {}, session, data: null
};

describe("shell trust evidence", () => {
  it.each(["seeded", "simulated", "sample", "unknown"] as const)("never labels %s evidence Live despite a live session", (dataProvenance) => {
    const state = buildTrustStripState({ ...options, dataProvenance });
    const mode = state.items.find((item) => item.id === "mode")!;
    expect(mode.value).not.toBe("Live");
    expect(mode.href).toBeNull();
    expect(mode.tone).toBe("review");
    expect(state.items.find((item) => item.id === "source")?.value).toBe(dataProvenance.toUpperCase());
  });
  it("fixture usage outranks even a real provenance claim", () => {
    const state = buildTrustStripState({ ...options, usingDevelopmentFixtures: true, dataProvenance: "real" });
    expect(state.items.find((item) => item.id === "mode")?.value).toBe("Demo");
    expect(state.items.find((item) => item.id === "source")?.value).toBe("SEEDED");
  });
  it("preserves live readiness navigation for real evidence", () => {
    expect(buildTrustStripState({ ...options, dataProvenance: "real" }).items.find((item) => item.id === "mode"))
      .toMatchObject({ value: "Live", tone: "review", href: "/trading/readiness" });
  });
  it("does not report an unknown provider state as healthy", () => {
    const data = { providers: [{ status: "Unrecognized" }] } as unknown as DataWorkspaceResponse;
    const item = buildTrustStripState({ ...options, data, dataProvenance: "real" }).items.find((row) => row.id === "providers");
    expect(item).toMatchObject({ value: "1 unverified", tone: "review", href: "/data/providers" });
  });
});
