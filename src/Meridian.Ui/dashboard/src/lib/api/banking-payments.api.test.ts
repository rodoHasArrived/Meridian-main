import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { approvePendingPayment, getBankTransactions, getPendingPayments, rejectPendingPayment } from "./banking-payments.api";

// Exercise the actual client and endpoint builders: mocking these wrappers hid
// the required entityId query parameter from the component tests.
describe("banking payment requests", () => {
  const fetchMock = vi.fn();
  const entityId = "11111111-1111-1111-1111-111111111111";
  const paymentId = "22222222-2222-2222-2222-222222222222";

  beforeEach(() => {
    fetchMock.mockReset().mockImplementation(async () => new Response("[]", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "backend-connected");
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it("lists pending payments and bank evidence in the selected entity", async () => {
    const controller = new AbortController();
    await getPendingPayments(entityId, { signal: controller.signal });
    await getBankTransactions(entityId, { signal: controller.signal });
    expect(fetchMock).toHaveBeenNthCalledWith(1, `/api/banking/payments/pending?entityId=${entityId}`,
      expect.objectContaining({ signal: controller.signal }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `/api/banking/transactions?entityId=${entityId}`,
      expect.objectContaining({ signal: controller.signal }));
  });

  it("sends the entity on both decisions while preserving the governed request body", async () => {
    const approval = { reviewNotes: "Reviewed against retained invoice", actionOrigin: "HumanOperator" as const };
    const rejection = { reason: "Invoice does not match", actionOrigin: "HumanOperator" as const };
    await approvePendingPayment(paymentId, entityId, approval);
    await rejectPendingPayment(paymentId, entityId, rejection);
    expect(fetchMock).toHaveBeenNthCalledWith(1, `/api/banking/payments/${paymentId}/approve?entityId=${entityId}`,
      expect.objectContaining({ method: "POST", body: JSON.stringify(approval) }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `/api/banking/payments/${paymentId}/reject?entityId=${entityId}`,
      expect.objectContaining({ method: "POST", body: JSON.stringify(rejection) }));
  });

  it("encodes identifiers without allowing the entity to introduce another query parameter", async () => {
    await approvePendingPayment("payment/one", "entity/one&other=two", { reviewNotes: null });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/banking/payments/payment%2Fone/approve?entityId=entity%2Fone%26other%3Dtwo");
  });

  it("does not send an unscoped queue read or decision", () => {
    expect(() => getPendingPayments(" ")).toThrow("Select an entity");
    expect(() => approvePendingPayment(paymentId, "", { reviewNotes: null })).toThrow("Select an entity");
    expect(() => rejectPendingPayment(paymentId, "", { reason: "Unscoped" })).toThrow("Select an entity");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("retains backend failures instead of reporting success", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: "Entity is unavailable" }), { status: 400 }));
    await expect(getPendingPayments(entityId)).rejects.toMatchObject({ status: 400 });
    await expect(approvePendingPayment(paymentId, entityId, {})).rejects.toMatchObject({ status: 400 });
    await expect(rejectPendingPayment(paymentId, entityId, { reason: "Rejected" })).rejects.toMatchObject({ status: 400 });
  });
});
