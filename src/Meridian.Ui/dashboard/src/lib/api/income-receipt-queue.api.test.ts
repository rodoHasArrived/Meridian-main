// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { receiptRun } from "@/test/book-receipt-fixture";
import { getBookReceiptRun, getBookReceiptRuns, runBookReceiptQueue } from "./income-receipt-queue.api";

describe("authoritative book receipt API", () => {
  const fetchMock = vi.fn();
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal("fetch", fetchMock); });
  afterEach(() => vi.unstubAllGlobals());
  const respond = (body: unknown) => fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status: 200 }));

  it("reads retained runs without a mutation and escapes selected identifiers", async () => {
    respond([receiptRun()]);
    await expect(getBookReceiptRuns("book-1")).resolves.toHaveLength(1);
    expect(fetchMock.mock.calls[0]?.[0]).toBe("/api/workstation/accounting/receipt-queue/runs?ledgerBookId=book-1");
    expect(fetchMock.mock.calls[0]?.[1].method).toBeUndefined();
    respond(receiptRun({ runId: "run/with space" }));
    await getBookReceiptRun("book-1", "run/with space");
    expect(fetchMock.mock.calls[1]?.[0]).toContain("/run%2Fwith%20space?ledgerBookId=book-1");
  });
  it("posts only the book and business date, retaining exact values", async () => {
    const run = receiptRun(); run.items[0]!.expectedAmount = "9007199254740993.123456789";
    respond(run);
    const result = await runBookReceiptQueue("book-1", "2026-11-20");
    expect(result.items[0]?.expectedAmount).toBe("9007199254740993.123456789");
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ method: "POST", body: JSON.stringify({ ledgerBookId: "book-1", asOfDate: "2026-11-20" }) });
  });
  it("rejects responses for another book, run, or business date", async () => {
    respond(receiptRun()); await expect(getBookReceiptRun("book-2", "daily-run-1")).rejects.toThrow("selected book");
    respond(receiptRun()); await expect(getBookReceiptRun("book-1", "other-run")).rejects.toThrow("selected book");
    respond(receiptRun()); await expect(runBookReceiptQueue("book-1", "2026-11-19")).rejects.toThrow("selected book");
  });
  it.each(["current", "history", "allocation", "posting"])("rejects numeric amounts in %s evidence", async location => {
    const run = receiptRun();
    const target = location === "current" ? run.items[0]! : location === "history" ? run.items[0]!.expectationHistory[0]!.expectation : location === "allocation" ? run.items[0]!.receipts[0]!.allocations[0]! : run.items[0]!.postings[0]!;
    Object.assign(target, { [location === "current" || location === "history" ? "expectedAmount" : "amount"]: 10.01 });
    respond(run); await expect(getBookReceiptRun("book-1", "daily-run-1")).rejects.toThrow("non-exact amount");
  });
  it("surfaces service failure without inventing development evidence", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: "Provider unavailable" }), { status: 503 }));
    await expect(getBookReceiptRuns("book-1")).rejects.toThrow();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
