import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import * as native from "@/lib/api/private-capital.api";
import { CAPITAL_BOOK_ID, CAPITAL_BANK_LINE_ID, CAPITAL_EVIDENCE, CAPITAL_PERIOD_ID, capitalBankObservationImport, capitalCall, capitalCommitment, capitalScope, capitalWorkbench } from "@/screens/private-capital.test-fixtures";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const get = vi.mocked(apiGetJson), post = vi.mocked(apiPostJson);
describe("Authoritative native private capital API", () => {
  beforeEach(() => vi.resetAllMocks());
  it("disables development fallback and carries the selected book on native reads", async () => {
    get.mockResolvedValue(capitalWorkbench()); const signal = new AbortController().signal;
    await native.getPrivateCapitalWorkbench(CAPITAL_BOOK_ID, { signal, allowDevelopmentFallback: true });
    expect(get).toHaveBeenCalledWith(`/api/ledger/private-capital/workbench?ledgerBookId=${CAPITAL_BOOK_ID}`, { signal, allowDevelopmentFallback: false });
  });
  it("rejects foreign nested records even if the top-level book matches", async () => {
    get.mockResolvedValue({ ...capitalWorkbench(), commitments: [{ ...capitalCommitment, scope: { ...capitalScope, tenantId: "foreign" } }] });
    await expect(native.getPrivateCapitalWorkbench(CAPITAL_BOOK_ID)).rejects.toThrow("incomplete or inconsistent scope");
  });
  it("binds call retention to the reviewed plan hash and exact decimal text", async () => {
    post.mockResolvedValue(capitalCall);
    const request = { callId: capitalCall.callId, amountToCall: "250000.01", noticeDate: capitalCall.noticeDate, dueDate: capitalCall.dueDate, periodId: CAPITAL_PERIOD_ID, commitmentIds: ["commitment-a"], purpose: null };
    await native.retainPrivateCapitalCall(CAPITAL_BOOK_ID, request, "retained-hash-a");
    expect(post).toHaveBeenCalledWith(`/api/ledger/private-capital/calls?ledgerBookId=${CAPITAL_BOOK_ID}`, { preview: request, expectedPlanHash: "retained-hash-a" }, { allowDevelopmentFallback: false });
  });
  it("uses native funding authority and preserves the receipt and bank-line identities", async () => {
    post.mockResolvedValue({}); const request = { fundProfileId: "fund-a", currency: "USD", callId: "call/a", receivedDate: "2026-11-10", ledgerBookId: CAPITAL_BOOK_ID,
      entityId: capitalScope.entityId, periodId: CAPITAL_PERIOD_ID, fundings: [{ commitmentId: "commitment-a", capitalAccountId: "capital-a", investorId: "investor-a", fundedAmount: "100.01", fundingReceiptId: "receipt-2", bankLineId: CAPITAL_BANK_LINE_ID, evidenceLinks: [CAPITAL_EVIDENCE] }] };
    await native.intakePrivateCapitalFunding(request);
    expect(post).toHaveBeenCalledWith(`/api/ledger/private-capital/calls/call%2Fa/funding-intake?ledgerBookId=${CAPITAL_BOOK_ID}`, request, { allowDevelopmentFallback: false });
    expect(request).not.toHaveProperty("actor"); expect(request).not.toHaveProperty("tenantId");
  });
  it("requires a complete ready call preview before exposing retention", async () => {
    post.mockResolvedValue({ call: null, isReady: true, blockers: [] });
    await expect(native.previewPrivateCapitalCall(CAPITAL_BOOK_ID, { callId: "call-a", amountToCall: "1.00", noticeDate: "2026-11-10", dueDate: "2026-11-20", periodId: CAPITAL_PERIOD_ID, commitmentIds: ["commitment-a"], purpose: null })).rejects.toThrow("complete allocation preview");
  });
  it("accepts the server canonical call identity and still rejects another call", async () => {
    const request = { callId: " CALL-A ", amountToCall: "250000.01", noticeDate: capitalCall.noticeDate,
      dueDate: capitalCall.dueDate, periodId: CAPITAL_PERIOD_ID, commitmentIds: ["commitment-a"], purpose: null };
    post.mockResolvedValue({ call: capitalCall, isReady: true, blockers: [] });
    await expect(native.previewPrivateCapitalCall(CAPITAL_BOOK_ID, request)).resolves.toMatchObject({ call: { callId: "call-a" } });
    post.mockResolvedValue({ call: { ...capitalCall, callId: "another-call" }, isReady: true, blockers: [] });
    await expect(native.previewPrivateCapitalCall(CAPITAL_BOOK_ID, request)).rejects.toThrow("complete allocation preview");
  });
  it("imports only the accepted source identities without caller monetary or actor fields", async () => {
    post.mockResolvedValue(capitalBankObservationImport);
    const request = { evidenceLink: CAPITAL_EVIDENCE, documentId: capitalBankObservationImport.documentId };
    await native.importPrivateCapitalBankObservation(CAPITAL_BOOK_ID, request);
    expect(post).toHaveBeenCalledWith(`/api/ledger/private-capital/bank-observations?ledgerBookId=${CAPITAL_BOOK_ID}`, request, { allowDevelopmentFallback: false });
    expect(Object.keys(post.mock.calls[0][1] as object).sort()).toEqual(["documentId", "evidenceLink"]);
  });
  it("withholds bank observations whose returned source identity differs", async () => {
    post.mockResolvedValue({ ...capitalBankObservationImport, documentId: "foreign-document" });
    await expect(native.importPrivateCapitalBankObservation(CAPITAL_BOOK_ID, { evidenceLink: CAPITAL_EVIDENCE, documentId: capitalBankObservationImport.documentId })).rejects.toThrow("does not match the reviewed source identity");
  });
});
