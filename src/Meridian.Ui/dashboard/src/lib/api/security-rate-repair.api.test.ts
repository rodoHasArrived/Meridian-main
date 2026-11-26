import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api/http-client";
import { assertFrozenRateRepairUnchanged, commandRateRepairBatch, createRateRepairBatch, discoverRateRepairs, getRateRepairBatch, listRateRepairBatches } from "./security-rate-repair.api";
import { createInput, discovery, repairBatchId, retainedBatch } from "@/screens/security-rate-repair.test-fixtures";

vi.mock("@/lib/api/http-client", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
describe("authoritative rate-repair API", () => {
  beforeEach(() => vi.clearAllMocks());
  it("uses real reads with development fallback disabled", async () => {
    vi.mocked(apiGetJson).mockResolvedValueOnce(discovery()).mockResolvedValueOnce([retainedBatch()]).mockResolvedValueOnce(retainedBatch());
    await discoverRateRepairs(); await listRateRepairBatches(); await getRateRepairBatch(repairBatchId);
    expect(apiGetJson).toHaveBeenNthCalledWith(1, "/api/security-master/rate-repairs/candidates?skip=0&pageSize=200", { allowDevelopmentFallback: false });
    expect(apiGetJson).toHaveBeenNthCalledWith(2, "/api/security-master/rate-repairs", { allowDevelopmentFallback: false });
    expect(apiGetJson).toHaveBeenNthCalledWith(3, `/api/security-master/rate-repairs/${repairBatchId}`, { allowDevelopmentFallback: false });
  });
  it("submits only the frozen declaration and exact record versions", async () => {
    vi.mocked(apiPostJson).mockResolvedValue(retainedBatch());
    const input = createInput(); await createRateRepairBatch(input);
    expect(apiPostJson).toHaveBeenCalledWith("/api/security-master/rate-repairs", input, { allowDevelopmentFallback: false });
    expect(input).not.toHaveProperty("rawValue"); expect(input).not.toHaveProperty("actor");
  });
  it("refuses a discovery response without declared completeness or exact scalar text", async () => {
    const page = discovery();
    vi.mocked(apiGetJson).mockResolvedValue({ ...page, scannedCount: undefined });
    await expect(discoverRateRepairs()).rejects.toThrow(/completeness or exact record authority/);
    page.candidates[0].rawValue = 0.5 as unknown as string;
    vi.mocked(apiGetJson).mockResolvedValue(page);
    await expect(discoverRateRepairs()).rejects.toThrow(/completeness or exact record authority/);
  });
  it.each(["scope", "unit", "roster", "request identity"])("refuses a mismatched retained %s", async field => {
    const batch = retainedBatch();
    if (field === "scope") batch.scope.periodId = "2026-10";
    if (field === "unit") batch.unit = "Fraction";
    if (field === "roster") batch.items[0].baseVersion = 8;
    if (field === "request identity") batch.batchId = "other-batch";
    vi.mocked(apiPostJson).mockResolvedValue(batch);
    await expect(createRateRepairBatch(createInput())).rejects.toThrow(/does not match the reviewed/);
  });
  it("refuses scalar rescaling in a later command response", () => {
    const before = retainedBatch(); const after = retainedBatch({ version: 2 });
    after.items[0].rawValue = "0.005000000000";
    expect(() => assertFrozenRateRepairUnchanged(before, after)).toThrow(/changed the frozen source scalar/);
  });
  it("refuses numeric source scalars and duplicate retained records", async () => {
    const batch = retainedBatch();
    batch.items[0].rawValue = 0.5 as unknown as string;
    vi.mocked(apiGetJson).mockResolvedValue(batch);
    await expect(getRateRepairBatch(repairBatchId)).rejects.toThrow(/inconsistent retained authority/);
    batch.items[0].rawValue = "0.5"; batch.items.push({ ...batch.items[0] });
    await expect(getRateRepairBatch(repairBatchId)).rejects.toThrow(/inconsistent retained authority/);
  });
  it("fences each explicit command by retained version and rejects another batch's response", async () => {
    vi.mocked(apiPostJson).mockResolvedValue(retainedBatch());
    await commandRateRepairBatch(repairBatchId, "publish", { expectedVersion: 4 });
    expect(apiPostJson).toHaveBeenCalledWith(`/api/security-master/rate-repairs/${repairBatchId}/publish`, { expectedVersion: 4 }, { allowDevelopmentFallback: false });
    vi.mocked(apiPostJson).mockResolvedValue(retainedBatch({ batchId: "different-batch" }));
    await expect(commandRateRepairBatch(repairBatchId, "stage", { expectedVersion: 1 })).rejects.toThrow(/inconsistent retained authority/);
  });
});
