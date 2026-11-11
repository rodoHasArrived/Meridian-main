import { describe, expect, it } from "vitest";
import { buildTrustStripState, resolveDataSourceTone } from "@/app-shell.trust-strip";
import type { DataWorkspaceResponse, SessionInfo } from "@/types";

function session(environment: SessionInfo["environment"]): SessionInfo {
  return { displayName: "Ops Desk", role: "Operator", environment, activeWorkspace: "trading", commandCount: 0 };
}

function build(overrides: Partial<Parameters<typeof buildTrustStripState>[0]> = {}) {
  return buildTrustStripState({
    loading: false,
    bootstrapFailed: false,
    usingDevelopmentFixtures: true,
    workspaceErrors: {},
    session: session("paper"),
    data: null,
    ...overrides
  });
}

describe("buildTrustStripState environment", () => {
  // The environment is the masthead pill's leading badge once the session card stops
  // carrying it, so every session state must map to a label and a badge variant.
  it.each([
    ["live", "LIVE", "live"],
    ["paper", "PAPER", "paper"],
    ["research", "DEMO", "research"]
  ] as const)("maps a %s session to the %s badge", (environment, label, variant) => {
    expect(build({ session: session(environment) }).environment).toEqual({ label, variant, dataLabel: "SEEDED" });
  });

  it("never claims an environment before the session resolves", () => {
    expect(build({ loading: true, session: null }).environment).toMatchObject({ label: "LOADING", variant: "default" });
  });

  it("shows a resolved session's environment while workspace data is still loading", () => {
    // The session card this badge replaced showed PAPER/LIVE as soon as the session existed.
    expect(build({ loading: true, session: session("live") }).environment).toMatchObject({ label: "LIVE", variant: "live" });
  });

  it("falls back to the safe Demo label when no session is loaded", () => {
    expect(build({ session: null }).environment).toMatchObject({ label: "DEMO", variant: "research" });
  });

  // The badge is the one place that says what the operator is looking at, so the data
  // qualifier rides beside the environment instead of in a separate sample-data chip.
  it.each([
    ["seeded", "SEEDED"],
    ["simulated", "SIMULATED"],
    ["sample", "SAMPLE"],
    ["unknown", "UNKNOWN"]
  ] as const)("qualifies the environment with %s data", (dataProvenance, dataLabel) => {
    expect(build({ usingDevelopmentFixtures: false, dataProvenance }).environment.dataLabel).toBe(dataLabel);
  });

  it("shows the environment alone for real data", () => {
    expect(build({ usingDevelopmentFixtures: false, dataProvenance: "real" }).environment)
      .toEqual({ label: "PAPER", variant: "paper", dataLabel: null });
  });
});

describe("resolveDataSourceTone", () => {
  it("does not raise declared demo data on Paper or Demo as a warning", () => {
    for (const provenance of ["seeded", "simulated", "sample"] as const) {
      expect(resolveDataSourceTone(provenance, "paper")).toBe("ready");
      expect(resolveDataSourceTone(provenance, "research")).toBe("ready");
      expect(resolveDataSourceTone(provenance, null)).toBe("ready");
    }
  });

  it("blocks non-real data inside a LIVE session", () => {
    for (const provenance of ["seeded", "simulated", "sample"] as const) {
      expect(resolveDataSourceTone(provenance, "live")).toBe("blocked");
    }
  });

  it("asks for review when provenance was never confirmed, whatever the environment", () => {
    expect(resolveDataSourceTone("unknown", "paper")).toBe("review");
    expect(resolveDataSourceTone("unknown", "live")).toBe("review");
  });

  it("is ready for real data", () => {
    expect(resolveDataSourceTone("real", "live")).toBe("ready");
    expect(resolveDataSourceTone("real", "paper")).toBe("ready");
  });
});

const liveSession: SessionInfo = {
  displayName: "Ops", role: "Operator", environment: "live", activeWorkspace: "trading", commandCount: 1
};
const options = {
  loading: false, bootstrapFailed: false, usingDevelopmentFixtures: false, workspaceErrors: {}, session: liveSession, data: null
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
      .toMatchObject({ value: "Live", tone: "ready", href: "/trading/readiness" });
  });
  it("does not report an unknown provider state as healthy", () => {
    const data = { providers: [{ status: "Unrecognized" }] } as unknown as DataWorkspaceResponse;
    const item = buildTrustStripState({ ...options, data, dataProvenance: "real" }).items.find((row) => row.id === "providers");
    expect(item).toMatchObject({ value: "1 unverified", tone: "review", href: "/data/providers" });
  });
});
