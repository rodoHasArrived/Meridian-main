import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ServerOrderPreviewPanel } from "./trading-screen.server-preview";
import { useOrderTicketViewModel } from "./trading-screen.view-model";
import type { ServerOrderPreview } from "@/lib/api/execution-workflows.api";

const account = "8c6b0029-9422-4961-ad61-a0ab6cbb242d";
function preview(canSubmit = true): ServerOrderPreview {
  return { fingerprint: "request-1", evaluatedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 30_000).toISOString(),
    canSubmit, requiresApproval: false, estimatedNotional: 100, estimatedFees: null,
    checks: [{ code: "TEST", status: canSubmit ? "passed" : "blocked", message: "Current account evidence checked." }] };
}
describe("authoritative ticket preview", () => {
  it("prevents submission before a successful preview and keeps the preview client ID for submit", async () => {
    const submitOrder = vi.fn().mockResolvedValue({ success: true, orderId: "accepted", reason: null });
    const previewOrder = vi.fn().mockResolvedValue(preview());
    const services = { submitOrder, previewOrder };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(submitOrder).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(submitOrder).toHaveBeenCalledTimes(1);
    expect(submitOrder.mock.calls[0][0].clientOrderId).toBe(previewOrder.mock.calls[0][0].clientOrderId);
    expect(submitOrder.mock.calls[0][0].sessionIntent).toBe("RegularOnly");
  });
  it("does not promote failed checks into a submittable order", async () => {
    const services = { submitOrder: vi.fn(), previewOrder: vi.fn().mockResolvedValue(preview(false)) };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview).not.toBeNull());
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(services.submitOrder).not.toHaveBeenCalled();
    expect(result.current.canSubmit).toBe(false);
  });
  it("holds an uncertain submission across draft edits", async () => {
    const services = { submitOrder: vi.fn().mockRejectedValue(new Error("Acknowledgement lost")), previewOrder: vi.fn().mockResolvedValue(preview()) };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(result.current.serverPreviewError).toContain("unconfirmed");
    act(() => { result.current.updateField("quantity", "2"); result.current.setAcknowledged(true); });
    await waitFor(() => expect(services.previewOrder).toHaveBeenCalledTimes(2));
    await act(async () => { await result.current.submitOrder(); });
    expect(services.submitOrder).toHaveBeenCalledTimes(1);
    expect(result.current.canSubmit).toBe(false);
  });
  it("distinguishes unknown fees and expired checks", () => {
    render(<ServerOrderPreviewPanel preview={preview()} error={null} loading={false} expired sessionIntent="RegularOnly" onSessionIntent={vi.fn()} onRefresh={vi.fn()} />);
    expect(screen.getByText("Unknown")).toBeVisible();
    expect(screen.getByRole("status")).toHaveTextContent("Preview expired");
  });
});
