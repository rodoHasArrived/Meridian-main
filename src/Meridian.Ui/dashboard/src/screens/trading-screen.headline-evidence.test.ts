import { describe, expect, it } from "vitest";
import { withTradingHeadlineEvidence } from "@/screens/trading-screen.headline-evidence";
import type { WorkspaceFigureContextValue } from "@/lib/workspace-figure-context";
import type { MetricSnapshot } from "@/types";

const accountA = "11111111-1111-1111-1111-111111111111";
const accountB = "22222222-2222-2222-2222-222222222222";
const metrics: MetricSnapshot[] = [{ id: "cash", label: "Cash", value: "$100", delta: "", tone: "default" }];

function shell(overrides: Partial<WorkspaceFigureContextValue> = {}): WorkspaceFigureContextValue {
  return {
    freshness: "fresh",
    dataLabel: null,
    staleSources: [],
    sourceLoadedAt: { trading: "2026-10-05T15:00:00Z" },
    sourceScope: { trading: accountA },
    ...overrides
  };
}

function evidence(figureContext: WorkspaceFigureContextValue | null, operatingFundAccountId: string | null = accountA) {
  const [metric] = withTradingHeadlineEvidence(metrics, {
    figureContext,
    operatingFundAccountId,
    fundAccountId: operatingFundAccountId,
    brokerageAccount: "DU1009034",
    lastHeartbeat: "2s ago",
    brokerFreshness: "fresh"
  });
  return { provenance: metric!.provenance, freshness: metric!.freshness };
}

describe("withTradingHeadlineEvidence", () => {
  it("states the loaded payload's account and load time", () => {
    expect(evidence(shell())).toEqual({
      provenance: { source: "Trading workspace", asOf: "2026-10-05T15:00:00Z", scope: accountA },
      freshness: "fresh"
    });
  });

  it("keeps the loaded account as scope, and marks the figures stale, after another account is selected", () => {
    expect(evidence(shell(), accountB)).toEqual({
      provenance: { source: "Trading workspace", asOf: "2026-10-05T15:00:00Z", scope: accountA },
      freshness: "stale"
    });
  });

  it("falls back to the payload's brokerage account when it was loaded for every account", () => {
    expect(evidence(shell({ sourceScope: { trading: null } }), null).provenance?.scope).toBe("DU1009034");
  });

  it("takes the weakest of the payload's verdict and the broker evidence", () => {
    expect(evidence(shell({ freshness: "unverified" })).freshness).toBe("unverified");
    expect(evidence(shell({ staleSources: ["trading"] })).freshness).toBe("stale");
  });

  it("uses the selected account and the payload's heartbeat outside the shell", () => {
    expect(evidence(null)).toEqual({
      provenance: { source: "Trading workspace", asOf: "2s ago", scope: accountA },
      freshness: "fresh"
    });
  });
});
