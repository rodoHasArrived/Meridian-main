import { describe, expect, it } from "vitest";
import { buildTrustStripState } from "@/app-shell.trust-strip";
import type { SessionInfo } from "@/types";

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
    expect(build({ session: session(environment) }).environment).toEqual({ label, variant });
  });

  it("never claims an environment before the session resolves", () => {
    expect(build({ loading: true, session: null }).environment).toEqual({ label: "LOADING", variant: "default" });
  });

  it("shows a resolved session's environment while workspace data is still loading", () => {
    // The session card this badge replaced showed PAPER/LIVE as soon as the session existed.
    expect(build({ loading: true, session: session("live") }).environment).toEqual({ label: "LIVE", variant: "live" });
  });

  it("falls back to the safe Demo label when no session is loaded", () => {
    expect(build({ session: null }).environment).toEqual({ label: "DEMO", variant: "research" });
  });
});
