// @vitest-environment node
import { describe, expect, it } from "vitest";
import {
  describeBrokerageSessionSource,
  describeLiveHoldingsSource,
  describeRetainedRunSource,
  maskAccountRef,
  positionSourceAttributes
} from "@/lib/position-source";

describe("position source labels", () => {
  it("names the brokerage session and masks its account", () => {
    expect(describeBrokerageSessionSource({ provider: "Alpaca", account: "DU1234567", environment: "paper" }))
      .toEqual({ kind: "paper-session", accountRef: "…4567", label: "Paper session (Alpaca …4567)" });
    expect(describeBrokerageSessionSource({ provider: "Alpaca", account: "LIVE-9", environment: "live" }).label).toBe("Live session (Alpaca …VE-9)");
    expect(describeBrokerageSessionSource(null)).toEqual({ kind: "paper-session", accountRef: null, label: "Paper session" });
  });

  it("separates live holdings across accounts from retained run records", () => {
    expect(describeLiveHoldingsSource().label).toBe("Live holdings · all accounts");
    expect(describeRetainedRunSource("run-42", "Jun 30, 2026").label).toBe("Retained records · run run-42 · as of Jun 30, 2026");
    expect(describeRetainedRunSource(null, null).label).toBe("Retained records");
  });

  it("masks short and missing accounts safely", () => {
    expect(maskAccountRef("  ")).toBeNull();
    expect(maskAccountRef("A12")).toBe("A12");
  });

  it("publishes the source for the screenshot harness", () => {
    expect(positionSourceAttributes(describeLiveHoldingsSource())).toEqual({
      "data-position-source": "live-holdings",
      "data-position-source-label": "Live holdings · all accounts"
    });
  });
});
