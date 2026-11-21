import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api/private-capital.api";
import { ApiError } from "@/lib/api-errors";
import { noticeOperationRunId, noticeOperationStorageKey, noticeOperationTarget } from "@/lib/private-capital-notice-intent";
import { CapitalCallIssuanceScreen } from "@/screens/capital-call-issuance-screen";
import { CAPITAL_BOOK_ID, capitalBook, capitalCall, capitalCommitment, capitalScope, capitalWorkbench } from "@/screens/private-capital.test-fixtures";
import type { PrivateCapitalNotice, PrivateCapitalWorkbench } from "@/types/private-capital";

vi.mock("@/lib/api/private-capital.api", async () => ({ ...await vi.importActual<typeof api>("@/lib/api/private-capital.api"),
  getPrivateCapitalBooks: vi.fn(), getPrivateCapitalWorkbench: vi.fn(), generatePrivateCapitalNotice: vi.fn() }));
const mocks = vi.mocked(api);
const firstId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66", secondId = "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77";
const secondLine = { ...capitalCall.lines[0], installmentId: "call-a:commitment-b", commitmentId: "commitment-b", capitalAccountId: "capital-b",
  amount: "125000.01", uncalledBefore: "2000000.00", uncalledAfter: "1874999.99", issuedAmount: "125000.01", fundedAmount: "0.00", outstandingAmount: "125000.01",
  issuanceJournalEntryId: "af7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e88" };
function twoInstallments(): PrivateCapitalWorkbench { return { ...capitalWorkbench(),
  commitments: [capitalCommitment, { ...capitalCommitment, commitmentId: "commitment-b", capitalAccountId: "capital-b", totalCommitment: "2000000.00", uncalledAmount: "2000000.00" }],
  calls: [{ ...capitalCall, amountToCall: "375000.02", lines: [capitalCall.lines[0], secondLine] }] }; }
function notice(commitmentId: string, runId: string, delivered = false): PrivateCapitalNotice { return {
  runId, callId: "call-a", investorId: "investor-a", commitmentId, governanceState: delivered ? "Released" : "Draft",
  packageId: delivered ? "released-package-a" : null, deliveryState: delivered ? "Delivered" : "NotQueued", route: `/reporting/runs/detail?runId=${runId}`
}; }
function mount() { return render(<MemoryRouter initialEntries={[`/accounting/capital-calls?ledgerBookId=${CAPITAL_BOOK_ID}`]}><CapitalCallIssuanceScreen /></MemoryRouter>); }
async function openCall(amount = "375000.02") {
  await screen.findByRole("heading", { name: "Retained commitment register" });
  fireEvent.click(screen.getByRole("button", { name: `call-a · ${amount} USD` }));
}
const installment = (commitmentId: string) => within(screen.getByRole("region", { name: `Investor notice installment call-a:${commitmentId}` }));
const target = (commitmentId: string) => noticeOperationTarget(capitalScope, "call-a", "investor-a", commitmentId);
function seedLegacy() {
  // Original v1 request had no commitment selector; preserve these exact scope fields and UUID.
  const legacyTarget = { tenantId: capitalScope.tenantId, companyId: capitalScope.companyId, entityId: capitalScope.entityId,
    fundProfileId: capitalScope.fundProfileId, ledgerBookId: capitalScope.ledgerBookId, currency: capitalScope.currency, callId: "call-a", investorId: "investor-a" };
  const key = `meridian.private-capital.notice-operation.v1:${JSON.stringify(Object.values(legacyTarget))}`;
  const raw = JSON.stringify({ version: 1, target: legacyTarget, operationId: firstId });
  sessionStorage.setItem(key, raw); return { key, raw };
}

describe("Installment-specific capital call notices", () => {
  beforeEach(() => { vi.resetAllMocks(); sessionStorage.clear(); mocks.getPrivateCapitalBooks.mockResolvedValue([capitalBook]); });
  afterEach(() => vi.restoreAllMocks());

  it("keeps two commitments for one investor separately actionable and reports only each installment's notice state", async () => {
    let workbench = twoInstallments(); workbench.notices = [notice("commitment-a", "released-notice-a", true)];
    mocks.getPrivateCapitalWorkbench.mockImplementation(async () => workbench);
    vi.spyOn(crypto, "randomUUID").mockReturnValue(secondId);
    mocks.generatePrivateCapitalNotice.mockImplementation(async (_book, _call, _investor, request) => {
      workbench = { ...workbench, notices: [...workbench.notices, notice("commitment-b", noticeOperationRunId(request.operationId))] };
      return { run: { runId: noticeOperationRunId(request.operationId) } };
    });
    mount(); await openCall();
    expect(installment("commitment-a").getByRole("button", { name: "Regenerate notice for investor-a · commitment-a" })).toBeEnabled();
    expect(installment("commitment-a").getByText("investor-a · Governance: Released · Delivery: Delivered")).toBeInTheDocument();
    expect(installment("commitment-b").queryByText(/Delivery: Delivered/)).not.toBeInTheDocument();
    expect(installment("commitment-b").getByText(/No retained notice run for this installment/)).toBeInTheDocument();
    fireEvent.click(installment("commitment-b").getByRole("button", { name: "Generate notice for investor-a · commitment-b" }));
    await screen.findByRole("button", { name: "Regenerate notice for investor-a · commitment-b" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[0].slice(0, 4)).toEqual([CAPITAL_BOOK_ID, "call-a", "investor-a", { operationId: secondId, commitmentId: "commitment-b" }]);
    expect(installment("commitment-b").getByText("investor-a · Governance: Draft · Delivery: NotQueued")).toBeInTheDocument();
    expect(installment("commitment-b").queryByRole("link", { name: "Inspect notice run released-notice-a" })).not.toBeInTheDocument();
    expect(installment("commitment-a").getByRole("link", { name: "Inspect notice run released-notice-a" })).toBeInTheDocument();
    expect(installment("commitment-a").queryByText(/Delivery: NotQueued/)).not.toBeInTheDocument();
  });

  it("recovers one installment after reload while another commitment for the same investor gets its own deliberate operation", async () => {
    let workbench = twoInstallments();
    const keyA = noticeOperationStorageKey(target("commitment-a")), keyB = noticeOperationStorageKey(target("commitment-b"));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValueOnce(firstId).mockReturnValueOnce(secondId);
    mocks.getPrivateCapitalWorkbench.mockImplementation(async () => workbench);
    mocks.generatePrivateCapitalNotice.mockImplementation(async (_book, _call, _investor, request) => {
      const runId = noticeOperationRunId(request.operationId);
      if (!workbench.notices.some(row => row.runId === runId)) workbench = { ...workbench, notices: [...workbench.notices, notice(request.commitmentId!, runId)] };
      if (mocks.generatePrivateCapitalNotice.mock.calls.length === 1) throw new Error("Successful notice acknowledgement was lost");
      return { run: { runId } };
    });
    const first = mount(); await openCall();
    fireEvent.click(installment("commitment-a").getByRole("button", { name: "Generate notice for investor-a · commitment-a" }));
    await screen.findByText("Write outcome is unconfirmed");
    const originalBody = mocks.generatePrivateCapitalNotice.mock.calls[0][3];
    expect(JSON.parse(sessionStorage.getItem(keyA)!).operationId).toBe(firstId); expect(sessionStorage.getItem(keyB)).toBeNull();
    first.unmount(); mount(); await openCall();
    expect(installment("commitment-a").getByRole("button", { name: "Recover original notice for investor-a · commitment-a" })).toBeEnabled();
    expect(installment("commitment-b").getByRole("button", { name: "Generate notice for investor-a · commitment-b" })).toBeEnabled();
    expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce(); expect(uuid).toHaveBeenCalledOnce();
    fireEvent.click(installment("commitment-b").getByRole("button", { name: "Generate notice for investor-a · commitment-b" }));
    await screen.findByRole("button", { name: "Regenerate notice for investor-a · commitment-b" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual({ operationId: secondId, commitmentId: "commitment-b" });
    expect(JSON.parse(sessionStorage.getItem(keyA)!).operationId).toBe(firstId); expect(sessionStorage.getItem(keyB)).toBeNull();
    fireEvent.click(installment("commitment-a").getByRole("button", { name: "Recover original notice for investor-a · commitment-a" }));
    await screen.findByRole("button", { name: "Regenerate notice for investor-a · commitment-a" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[2][3]).toEqual(originalBody);
    expect(originalBody).toEqual({ operationId: firstId, commitmentId: "commitment-a" });
    expect(uuid).toHaveBeenCalledTimes(2); expect(sessionStorage.getItem(keyA)).toBeNull();
  });

  it("migrates a sole-installment legacy intent and recovers the exact original omitted-selector body after reload", async () => {
    const previous = seedLegacy(), key = noticeOperationStorageKey(target("commitment-a"));
    const uuid = vi.spyOn(crypto, "randomUUID");
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockResolvedValue({ run: { runId: noticeOperationRunId(firstId) } });
    const first = mount(); await openCall("250000.01");
    await screen.findByRole("button", { name: "Recover original notice for investor-a" });
    expect(JSON.parse(sessionStorage.getItem(key)!)).toEqual({ version: 2, target: target("commitment-a"), operationId: firstId, legacyOmittedSelector: true });
    expect(sessionStorage.getItem(previous.key)).toBeNull();
    first.unmount(); mount(); await openCall("250000.01");
    expect(mocks.generatePrivateCapitalNotice).not.toHaveBeenCalled(); expect(uuid).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole("button", { name: "Recover original notice for investor-a" }));
    await waitFor(() => expect(sessionStorage.getItem(key)).toBeNull());
    expect(mocks.generatePrivateCapitalNotice.mock.calls[0][3]).toEqual({ operationId: firstId });
    expect(uuid).not.toHaveBeenCalled();
  });

  it("uses an explicit commitment selector for a new operation after a migrated legacy run is confirmed failed", async () => {
    seedLegacy(); const key = noticeOperationStorageKey(target("commitment-a"));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(secondId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new ApiError({ path: "/notice", status: 409,
      responseBody: JSON.stringify({ code: "notice_operation_failed", runId: noticeOperationRunId(firstId) }) }))
      .mockImplementationOnce(async (_book, _call, _investor, request) => {
        expect(JSON.parse(sessionStorage.getItem(key)!)).toEqual({ version: 2, target: target("commitment-a"), operationId: secondId });
        return { run: { runId: noticeOperationRunId(request.operationId) } };
      });
    mount(); await openCall("250000.01");
    fireEvent.click(await screen.findByRole("button", { name: "Recover original notice for investor-a" }));
    const generate = await screen.findByRole("button", { name: "Generate new notice after confirmed failure for investor-a" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[0][3]).toEqual({ operationId: firstId });
    expect(uuid).not.toHaveBeenCalled();
    fireEvent.click(generate);
    await waitFor(() => expect(sessionStorage.getItem(key)).toBeNull());
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual({ operationId: secondId, commitmentId: "commitment-a" });
    expect(uuid).toHaveBeenCalledOnce();
  });

  it("preserves an ambiguous legacy investor intent without guessing an installment or generating another operation", async () => {
    const previous = seedLegacy(), uuid = vi.spyOn(crypto, "randomUUID");
    mocks.getPrivateCapitalWorkbench.mockResolvedValue(twoInstallments());
    mount(); await openCall();
    for (const commitmentId of ["commitment-a", "commitment-b"]) {
      const section = installment(commitmentId);
      expect(section.getByRole("button", { name: `Generate notice for investor-a · ${commitmentId}` })).toBeDisabled();
      expect(section.getByRole("alert")).toHaveTextContent("A prior investor-level notice intent cannot be assigned to multiple installments");
      expect(sessionStorage.getItem(noticeOperationStorageKey(target(commitmentId)))).toBeNull();
    }
    expect(sessionStorage.getItem(previous.key)).toBe(previous.raw);
    expect(mocks.generatePrivateCapitalNotice).not.toHaveBeenCalled(); expect(uuid).not.toHaveBeenCalled();
  });

  it("never replays another installment's pending UUID from a record under the wrong selection", async () => {
    const operationA = { version: 2, target: target("commitment-a"), operationId: firstId };
    const keyA = noticeOperationStorageKey(target("commitment-a")), keyB = noticeOperationStorageKey(target("commitment-b"));
    sessionStorage.setItem(keyA, JSON.stringify(operationA)); sessionStorage.setItem(keyB, JSON.stringify(operationA));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(secondId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue(twoInstallments());
    mocks.generatePrivateCapitalNotice.mockResolvedValue({ run: { runId: noticeOperationRunId(secondId) } });
    mount(); await openCall();
    expect(installment("commitment-a").getByRole("button", { name: "Recover original notice for investor-a · commitment-a" })).toBeEnabled();
    expect(installment("commitment-b").getByText(/An invalid notice recovery record was cleared/)).toBeInTheDocument();
    expect(sessionStorage.getItem(keyB)).toBeNull(); expect(mocks.generatePrivateCapitalNotice).not.toHaveBeenCalled();
    fireEvent.click(installment("commitment-b").getByRole("button", { name: "Generate notice for investor-a · commitment-b" }));
    await waitFor(() => expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce());
    expect(mocks.generatePrivateCapitalNotice.mock.calls[0][3]).toEqual({ operationId: secondId, commitmentId: "commitment-b" });
    expect(sessionStorage.getItem(keyA)).toBe(JSON.stringify(operationA)); expect(uuid).toHaveBeenCalledOnce();
  });
});
