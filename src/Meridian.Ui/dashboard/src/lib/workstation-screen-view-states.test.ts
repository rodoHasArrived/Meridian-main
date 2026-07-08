import { describe, expect, it } from "vitest";
import {
  WORKSTATION_SCREEN_VIEW_STATE_SCREENS,
  buildWorkstationScreenViewStateEnvelope
} from "@/lib/workstation-screen-view-states";

describe("workstation screen view-state contracts", () => {
  it("names object-centric browser workstation view-state screens", () => {
    expect(WORKSTATION_SCREEN_VIEW_STATE_SCREENS).toEqual({
      reportingOperationsRecord: "reporting-operations-record",
      tradingBlotter: "trading-blotter",
      portfolioWorkstation: "portfolio-workstation",
      accountingReconciliation: "accounting-reconciliation",
      dataWorkstation: "data-workstation",
      settingsWorkstation: "settings-workstation"
    });
  });

  it("builds a versioned envelope for typed screen-local state", () => {
    expect(buildWorkstationScreenViewStateEnvelope("trading-blotter", {
      activeTable: "orders",
      selectedId: "order-1"
    })).toEqual({
      v: 1,
      screen: "trading-blotter",
      state: {
        activeTable: "orders",
        selectedId: "order-1"
      }
    });
  });
});
