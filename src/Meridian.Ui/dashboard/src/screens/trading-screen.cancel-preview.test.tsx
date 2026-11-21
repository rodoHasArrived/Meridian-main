import { act, renderHook, waitFor } from "@testing-library/react";
import { buildTradingConfirmDialogState, createTradingConfirmState, useTradingConfirmViewModel, type TradingConfirmServices } from "./trading-screen.view-model";
import type { CancelAllPreview } from "./trading-screen.cancel-preview";

function services(): TradingConfirmServices {
  return {
    getCancelAllPreview: vi.fn().mockResolvedValue({ orderIds: ["order-a", "order-b"], observedAt: "2026-11-11T12:00:00Z" }),
    cancelAllOrders: vi.fn().mockResolvedValue({ actionId: "cancel-1", status: "CompletedWithWarnings", message: "One order still working.", occurredAt: "2026-11-11T12:00:01Z", stillWorking: [{ orderId: "order-b", reason: "Broker rejected cancellation" }] }),
    cancelOrder: vi.fn(), closePosition: vi.fn(), pauseStrategy: vi.fn(), stopStrategy: vi.fn(), updateExecutionCircuitBreaker: vi.fn()
  };
}

it("requires an authoritative OMS preview even after acknowledgement", () => {
  const state = buildTradingConfirmDialogState(createTradingConfirmState({ kind: "cancel-all" }, true));
  expect(state.canConfirm).toBe(false);
  expect(state.confirmDisabledReason).toContain("OMS open-order population");
  expect(state.description).toContain("including other accounts and strategies");
  expect(state.description).toContain("filters do not limit");
});

it("shows full-book count, keeps affected identities and reports incomplete cancellation", async () => {
  const api = services();
  const { result } = renderHook(() => useTradingConfirmViewModel({ services: api, fundAccountId: "selected-account" }));
  act(() => result.current.openConfirm({ kind: "cancel-all" }));
  await waitFor(() => expect(result.current.description).toContain("2 open order(s)"));
  expect(result.current.state.cancelAllPreview?.orderIds).toEqual(["order-a", "order-b"]);
  act(() => result.current.setReviewAcknowledged(true));
  await act(async () => result.current.executeConfirm());
  expect(api.cancelAllOrders).toHaveBeenCalledWith();
  expect(result.current.resultPanel?.tone).toBe("warning");
  expect(result.current.state.result?.stillWorking).toHaveLength(1);
});

it("does not restore a preview after its confirmation is closed", async () => {
  const api = services();
  let resolve!: (value: CancelAllPreview) => void;
  vi.mocked(api.getCancelAllPreview!).mockImplementation(() => new Promise((done) => { resolve = done; }));
  const { result } = renderHook(() => useTradingConfirmViewModel({ services: api }));
  act(() => result.current.openConfirm({ kind: "cancel-all" }));
  act(() => result.current.closeConfirm());
  await act(async () => resolve({ orderIds: ["old"], observedAt: "2026-11-11T12:00:00Z" }));
  expect(result.current.open).toBe(false);
  expect(result.current.state.cancelAllPreview).toBeUndefined();
  expect(api.cancelAllOrders).not.toHaveBeenCalled();
});
