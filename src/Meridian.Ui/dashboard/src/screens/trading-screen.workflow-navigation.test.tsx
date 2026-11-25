import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useTradingScreenShellViewModel } from "@/screens/trading-screen.view-model";

describe("Trading task deep links", () => {
  it.each([
    ["#session-replay-panel", "replaySheetOpen"],
    ["#promotion-gate-panel", "promotionSheetOpen"]
  ] as const)("opens the requested sheet on a direct %s visit", (hash, flag) => {
    const { result } = renderHook(() => useTradingScreenShellViewModel({ pathname: "/trading", data: null, hash }));
    expect(result.current[flag]).toBe(true);
  });

  it("follows a changed task hash without reopening a deliberately closed sheet on ordinary rerenders", () => {
    const { result, rerender } = renderHook(({ hash }) => useTradingScreenShellViewModel({ pathname: "/trading", data: null, hash }), {
      initialProps: { hash: "#session-replay-panel" }
    });
    act(() => result.current.setWorkflowPanelOpen("replay", false));
    expect(result.current.replaySheetOpen).toBe(false);
    rerender({ hash: "#session-replay-panel" });
    expect(result.current.replaySheetOpen).toBe(false);
    rerender({ hash: "#promotion-gate-panel" });
    expect(result.current.promotionSheetOpen).toBe(true);
    expect(result.current.replaySheetOpen).toBe(false);
    rerender({ hash: "#session-replay-panel" });
    expect(result.current.replaySheetOpen).toBe(true);
    expect(result.current.promotionSheetOpen).toBe(false);
  });

  it("leaves manually opened tools alone when the hash is not a workflow panel", () => {
    const { result, rerender } = renderHook(({ hash }) => useTradingScreenShellViewModel({ pathname: "/trading", data: null, hash }), {
      initialProps: { hash: "" }
    });
    expect(result.current.promotionSheetOpen).toBe(false);
    act(() => result.current.openWorkflowPanel("promotion"));
    rerender({ hash: "#brokerage-recovery" });
    expect(result.current.promotionSheetOpen).toBe(true);
  });
});
