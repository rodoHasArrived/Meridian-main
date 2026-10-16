import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { approvePendingPayment, getBankTransactions, getPendingPayments, rejectPendingPayment } from "@/lib/api/banking-payments.api";

describe("banking payment API requests", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => vi.unstubAllGlobals());

  it("sends the selected entity on queue, evidence, approval, and rejection requests", async () => {
    const entityId = "entity / treasury & operations";
    const signal = new AbortController().signal;
    await getPendingPayments(entityId, { signal });
    await getBankTransactions(entityId, { signal });
    await approvePendingPayment("pay / 1", entityId, { reviewNotes: "Checked", actionOrigin: "HumanOperator" });
    await rejectPendingPayment("pay / 2", entityId, { reason: "Wrong beneficiary", actionOrigin: "HumanOperator" });

    const urls = fetchMock.mock.calls.map(([url]) => new URL(url, "https://meridian.test"));
    expect(urls.map(url => url.pathname)).toEqual([
      "/api/banking/payments/pending", "/api/banking/transactions",
      "/api/banking/payments/pay%20%2F%201/approve", "/api/banking/payments/pay%20%2F%202/reject"
    ]);
    for (const url of urls) expect(url.searchParams.get("entityId")).toBe(entityId);
    expect(fetchMock.mock.calls[0][1].signal).toBe(signal);
    expect(fetchMock.mock.calls[1][1].signal).toBe(signal);
    expect(fetchMock.mock.calls[2][1].method).toBe("POST");
    expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toEqual({ reviewNotes: "Checked", actionOrigin: "HumanOperator" });
    expect(fetchMock.mock.calls[3][1].method).toBe("POST");
    expect(JSON.parse(fetchMock.mock.calls[3][1].body)).toEqual({ reason: "Wrong beneficiary", actionOrigin: "HumanOperator" });
  });

  it("keeps entity authorization failures visible", async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 403, text: async () => "Entity access denied" });
    await expect(getPendingPayments("entity-2")).rejects.toThrow("403");
    await expect(approvePendingPayment("pay-1", "entity-2", {})).rejects.toThrow("403");
    await expect(rejectPendingPayment("pay-1", "entity-2", { reason: "Review" })).rejects.toThrow("403");
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
});
