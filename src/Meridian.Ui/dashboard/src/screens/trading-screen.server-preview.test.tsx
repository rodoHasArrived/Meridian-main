import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { submitOrder } from "@/lib/api";
import { ServerOrderPreviewPanel } from "./trading-screen.server-preview";
import { useOrderTicketViewModel } from "./trading-screen.view-model";
import type { ServerOrderPreview } from "@/lib/api/execution-workflows.api";
import type { OrderResult } from "@/types";

const account = "8c6b0029-9422-4961-ad61-a0ab6cbb242d";
function preview(canSubmit = true): ServerOrderPreview {
  return { fingerprint: "request-1", evaluatedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 30_000).toISOString(),
    canSubmit, requiresApproval: false, estimatedNotional: 100, estimatedFees: null,
    checks: [{ code: "TEST", status: canSubmit ? "passed" : "blocked", message: "Current account evidence checked." }] };
}
describe("authoritative ticket preview", () => {
  afterEach(() => vi.unstubAllGlobals());

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
  it.each([
    { status: 429, body: null, message: "Too many requests were sent at once." },
    { status: 400, body: { success: false, errorMessage: "Order quantity exceeds the limit." }, message: "Order quantity exceeds the limit." },
    { status: 503, body: { type: "https://meridian.io/errors/execution-pre-dispatch-unavailable", detail: "Order management system is not active." }, message: "Order management system is not active." }
  ])("allows a newly previewed order after a definitive $status refusal", async ({ status, body, message }) => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(body && JSON.stringify(body), { status }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ success: true, orderId: "accepted" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);
    const previewOrder = vi.fn().mockResolvedValue(preview());
    const services = { submitOrder, previewOrder };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(result.current.errorText).toContain(message);
    expect(result.current.serverPreviewError).toBeNull();

    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    expect(previewOrder.mock.calls[1][0].clientOrderId).not.toBe(previewOrder.mock.calls[0][0].clientOrderId);
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.current.orderId).toBe("accepted");
  });

  it.each([
    { status: 502, body: null },
    { status: 503, body: null },
    { status: 504, body: null },
    { status: 201, body: "{}" }
  ])("holds a submission with an uncertain HTTP $status outcome", async ({ status, body }) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { status })));
    const services = { submitOrder, previewOrder: vi.fn().mockResolvedValue(preview()) };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(result.current.serverPreviewError).toContain("unconfirmed");
    expect(result.current.canSubmit).toBe(false);
  });

  it("looks up the submitted account and order after a pending acknowledgement is lost during draft edits", async () => {
    let rejectSubmission!: (error: Error) => void;
    const submission = new Promise<OrderResult>((_, reject) => { rejectSubmission = reject; });
    const services = { submitOrder: vi.fn().mockReturnValue(submission), previewOrder: vi.fn().mockResolvedValue(preview()) };
    const { result, rerender } = renderHook(({ fundAccountId }) => useOrderTicketViewModel({ services, fundAccountId }),
      { initialProps: { fundAccountId: account.toUpperCase() } });
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    let pending!: Promise<void>;
    act(() => { pending = result.current.submitOrder(); });
    const submitted = services.submitOrder.mock.calls[0][0];

    const newAccount = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";
    rerender({ fundAccountId: newAccount });
    act(() => result.current.updateField("quantity", "2"));
    await waitFor(() => expect(services.previewOrder).toHaveBeenLastCalledWith(expect.objectContaining({ fundAccountId: newAccount, quantity: 2 }), expect.anything()));
    expect(services.previewOrder.mock.lastCall?.[0].clientOrderId).not.toBe(submitted.clientOrderId);
    await act(async () => { rejectSubmission(new Error("Acknowledgement lost")); await pending; });
    expect(result.current.serverPreviewError).toContain(submitted.clientOrderId);

    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });
    expect(services.submitOrder).toHaveBeenCalledTimes(1);
    expect(result.current.canSubmit).toBe(false);

    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 404 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ orderId: submitted.clientOrderId, status: "Filled" })));
    vi.stubGlobal("fetch", fetchMock);
    await act(async () => result.current.refreshServerPreview());
    expect(fetchMock).toHaveBeenCalledWith(`/api/execution/journal/orders/${submitted.clientOrderId}?accountId=${submitted.fundAccountId}`, expect.anything());
    expect(result.current.serverPreviewError).toContain(submitted.clientOrderId);
    await act(async () => result.current.refreshServerPreview());
    await waitFor(() => expect(result.current.orderId).toBe(submitted.clientOrderId));
  });
  it("distinguishes unknown fees and expired checks", () => {
    render(<ServerOrderPreviewPanel preview={preview()} error={null} loading={false} expired sessionIntent="RegularOnly" onSessionIntent={vi.fn()} onRefresh={vi.fn()} />);
    expect(screen.getByText("Unknown")).toBeVisible();
    expect(screen.getByRole("status")).toHaveTextContent("Preview expired");
  });
});
