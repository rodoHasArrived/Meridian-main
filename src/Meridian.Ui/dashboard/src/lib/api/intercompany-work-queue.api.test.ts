import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { getAgreementWorkQueue, observeAgreementWorkItem, updateAgreementWorkItem } from "./intercompany-work-queue.api";
import type { AgreementWorkItem, AgreementWorkQueue, AgreementWorkQueueScope } from "@/types/intercompany-work-queue";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const scope: AgreementWorkQueueScope = { ledgerBookId: "book-a", fundProfileId: "fund/a", asOf: "2026-11-23", expiryHorizonDays: 45 };
const result: AgreementWorkQueue = { ledgerBookId: "book-a", fundProfileId: "fund/a", asOf: "2026-11-23", expiryHorizonDays: 45, evaluatedAtUtc: "2026-11-23T12:00:00Z", status: "Current", isComplete: true, sourceFingerprint: "source", canManage: true, cases: [], guarantees: [], diagnostics: [] };
beforeEach(() => vi.clearAllMocks());
describe("Lifecycle queue transport", () => {
  it("pins explicit scope and disables development fallbacks", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(result);
    expect(await getAgreementWorkQueue(scope)).toEqual(result);
    expect(apiGetJson).toHaveBeenCalledWith("/api/ledger/intercompany/work-queue?ledgerBookId=book-a&asOf=2026-11-23&expiryHorizonDays=45&fundProfileId=fund%2Fa", { allowDevelopmentFallback: false });
  });
  it.each([{ ledgerBookId: "foreign" }, { asOf: "2026-11-22" }, { expiryHorizonDays: 30 }, { fundProfileId: "foreign" }])("rejects a mismatched scope even for empty lists: %j", async change => {
    vi.mocked(apiGetJson).mockResolvedValue({ ...result, ...change });
    await expect(getAgreementWorkQueue(scope)).rejects.toThrow("different book");
  });
  it("sends the reviewed case tokens and rejects an unrelated mutation receipt", async () => {
    const request = { commandId: "command-a", caseId: "case-a", expectedSourceFingerprint: "source", reason: "Follow up", expectedVersion: 7, expectedRecordHash: "record-7" };
    vi.mocked(apiPostJson).mockResolvedValue({ caseId: "case-a", ledgerBookId: "book-a", isTracked: true, version: 8, history: [{ commandId: "someone-else" }] } as AgreementWorkItem);
    await expect(observeAgreementWorkItem(scope, request)).rejects.toThrow("retained case receipt");
    expect(apiPostJson).toHaveBeenCalledWith(expect.stringContaining("/work-queue/observe?"), request, { allowDevelopmentFallback: false });
    await expect(updateAgreementWorkItem(scope, { ...request, action: "AssignOwner", owner: "operator" })).rejects.toThrow("retained case receipt");
    expect(apiPostJson).toHaveBeenLastCalledWith(expect.stringContaining("/work-queue/update?"), expect.objectContaining({ expectedVersion: 7, expectedRecordHash: "record-7", expectedSourceFingerprint: "source", owner: "operator" }), { allowDevelopmentFallback: false });
  });
});
