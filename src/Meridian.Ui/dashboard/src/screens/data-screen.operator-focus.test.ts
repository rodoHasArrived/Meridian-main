import { describe, expect, it } from "vitest";
import { buildDataOperatorFocusItems } from "@/screens/data-screen.operator-focus";
import type { DataWorkspaceResponse } from "@/types";

function workspace(providers: Record<string, unknown>[]): DataWorkspaceResponse {
  return { providers, backfills: [], exports: [] } as unknown as DataWorkspaceResponse;
}

describe("buildDataOperatorFocusItems", () => {
  it("sends a provider with an id to its retained verification history", () => {
    const [item] = buildDataOperatorFocusItems(workspace([
      { providerId: "alpaca", provider: "Alpaca", status: "Degraded", capability: "Streaming", latency: "—", note: "Probe failing" }
    ]));
    expect(item).toMatchObject({
      route: "/data?providerFamily=alpaca#provider-verification-history",
      actionLabel: "Open provider history",
      tone: "blocked"
    });
  });

  it("keeps the provider trust list for a provider without an id, and lists no healthy provider", () => {
    const items = buildDataOperatorFocusItems(workspace([
      { provider: "Legacy feed", status: "Warning", capability: "Historical", latency: "—", note: "Stale" },
      { providerId: "polygon", provider: "Polygon", status: "Healthy", capability: "Streaming", latency: "—", note: "" }
    ]));
    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({ route: "/data/providers", actionLabel: "Open provider trust", tone: "review" });
  });
});
