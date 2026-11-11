import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api/private-capital.api";
import { ApiError } from "@/lib/api-errors";
import { CapitalCallIssuanceScreen } from "@/screens/capital-call-issuance-screen";
import { CAPITAL_BOOK_ID, CAPITAL_ENTITY_ID, CAPITAL_PERIOD_ID, CAPITAL_BANK_LINE_ID, CAPITAL_EVIDENCE, capitalBankObservationImport, capitalBook, capitalCall, capitalCommitment, capitalWorkbench } from "@/screens/private-capital.test-fixtures";
import type { PrivateCapitalCallPreview, PrivateCapitalNoticeGenerationResult } from "@/types/private-capital";
import { noticeOperationStorageKey, noticeOperationTarget } from "@/lib/private-capital-notice-intent";

vi.mock("@/lib/api/private-capital.api", async () => ({ ...await vi.importActual<typeof api>("@/lib/api/private-capital.api"), getPrivateCapitalBooks: vi.fn(), getPrivateCapitalWorkbench: vi.fn(),
  createPrivateCapitalCommitment: vi.fn(), approvePrivateCapitalCommitment: vi.fn(), previewPrivateCapitalCall: vi.fn(),
  retainPrivateCapitalCall: vi.fn(), intakePrivateCapitalCall: vi.fn(), intakePrivateCapitalFunding: vi.fn(), generatePrivateCapitalNotice: vi.fn(), importPrivateCapitalBankObservation: vi.fn() }));
const mocks = vi.mocked(api);
function mount() { return render(<MemoryRouter initialEntries={[`/accounting/capital-calls?ledgerBookId=${CAPITAL_BOOK_ID}`]}><CapitalCallIssuanceScreen /></MemoryRouter>); }
const enter = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
async function ready() { await screen.findByRole("heading", { name: "Retained commitment register" }); }
function fillCommitment() {
  enter("Commitment ID", "commitment-b"); enter("Investor ID", "investor-b"); enter("Capital account ID", "capital-b");
  enter("Total commitment", "12345678901234567890.12"); enter("Commitment date", "2026-01-01"); enter("Effective from", "2026-01-01");
  enter("Retained commitment evidence (one reference per line)", CAPITAL_EVIDENCE);
  fireEvent.click(screen.getByLabelText("I checked the source terms and retained evidence."));
}
function fillCall() {
  enter("Capital call ID", "call-a"); enter("Amount to call", "250000.01"); enter("Accounting period ID", CAPITAL_PERIOD_ID);
  enter("Notice date", "2026-11-03"); enter("Due date", "2026-11-20");
  fireEvent.click(screen.getByLabelText(/commitment-a · investor-a · v1 · Uncalled/));
}
describe("Native commitment-to-cash workbench", () => {
  beforeEach(() => { vi.resetAllMocks(); sessionStorage.clear(); mocks.getPrivateCapitalBooks.mockResolvedValue([capitalBook]); mocks.getPrivateCapitalWorkbench.mockResolvedValue(capitalWorkbench()); });
  afterEach(() => vi.restoreAllMocks());
  it("requires retained authority and never exposes fake commitments when it is unavailable", async () => {
    mocks.getPrivateCapitalWorkbench.mockRejectedValue(new Error("Authoritative persistence is not configured")); mount();
    await screen.findByText("Authoritative persistence is not configured");
    expect(screen.queryByRole("button", { name: "Retain commitment draft" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Capital accounts" })).toHaveAttribute("href", "/accounting/capital-accounts");
  });
  it("retains exact decimal source terms and leaves approval separate", async () => {
    mocks.createPrivateCapitalCommitment.mockResolvedValue({ ...capitalCommitment, commitmentId: "commitment-b", approvalStatus: 0 });
    mount(); await ready(); expect(screen.getByRole("button", { name: "Retain commitment draft" })).toBeDisabled(); fillCommitment();
    fireEvent.click(screen.getByRole("button", { name: "Retain commitment draft" }));
    await waitFor(() => expect(mocks.createPrivateCapitalCommitment).toHaveBeenCalledOnce());
    expect(mocks.createPrivateCapitalCommitment.mock.calls[0][0]).toBe(CAPITAL_BOOK_ID);
    expect(mocks.createPrivateCapitalCommitment.mock.calls[0][1]).toMatchObject({ totalCommitment: "12345678901234567890.12", expectedRevision: 0, evidenceLinks: [CAPITAL_EVIDENCE] });
    expect(mocks.approvePrivateCapitalCommitment).not.toHaveBeenCalled(); expect(mocks.intakePrivateCapitalCall).not.toHaveBeenCalled();
  });
  it("approves only the explicitly reviewed retained revision", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), commitments: [{ ...capitalCommitment, approvalStatus: 0, revision: 3 }] });
    mocks.approvePrivateCapitalCommitment.mockResolvedValue(capitalCommitment); mount(); await ready();
    fireEvent.click(screen.getByRole("button", { name: "Review commitment-a" }));
    expect(screen.getByRole("button", { name: "Approve reviewed revision" })).toBeDisabled();
    fireEvent.click(screen.getByLabelText("I reviewed the retained terms, investor, scope and evidence for this revision."));
    fireEvent.click(screen.getByRole("button", { name: "Approve reviewed revision" }));
    await waitFor(() => expect(mocks.approvePrivateCapitalCommitment).toHaveBeenCalledWith(CAPITAL_BOOK_ID, "commitment-a", 3, expect.objectContaining({ signal: expect.any(AbortSignal) })));
  });
  it("retains a reviewed server allocation using its plan hash without posting", async () => {
    mocks.previewPrivateCapitalCall.mockResolvedValue({ call: capitalCall, isReady: true, blockers: [] });
    mocks.retainPrivateCapitalCall.mockResolvedValue(capitalCall); mount(); await ready(); fillCall();
    fireEvent.click(screen.getByRole("button", { name: "Preview call allocation" }));
    await screen.findByRole("region", { name: "Authoritative call preview" });
    expect(screen.getByRole("button", { name: "Retain reviewed call" })).toBeDisabled();
    fireEvent.click(screen.getByLabelText("I reviewed each allocation and the uncalled balances.")); fireEvent.click(screen.getByRole("button", { name: "Retain reviewed call" }));
    await waitFor(() => expect(mocks.retainPrivateCapitalCall).toHaveBeenCalledOnce());
    expect(mocks.retainPrivateCapitalCall.mock.calls[0].slice(0, 3)).toEqual([CAPITAL_BOOK_ID, expect.objectContaining({ commitmentIds: ["commitment-a"], amountToCall: "250000.01" }), "retained-hash-a"]);
    expect(mocks.intakePrivateCapitalCall).not.toHaveBeenCalled();
  });
  it("clears a delayed preview when call terms change", async () => {
    let complete!: (value: PrivateCapitalCallPreview) => void;
    mocks.previewPrivateCapitalCall.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    mount(); await ready(); fillCall(); fireEvent.click(screen.getByRole("button", { name: "Preview call allocation" }));
    await waitFor(() => expect(mocks.previewPrivateCapitalCall).toHaveBeenCalledOnce());
    enter("Amount to call", "100000.00"); complete({ call: capitalCall, isReady: true, blockers: [] });
    await waitFor(() => expect(screen.queryByText("Plan retained-hash-a")).not.toBeInTheDocument());
    expect(screen.queryByRole("button", { name: "Retain reviewed call" })).not.toBeInTheDocument();
    expect(mocks.previewPrivateCapitalCall.mock.calls[0][2]?.signal?.aborted).toBe(true);
  });
  it("keeps blocked previews in review and renders the server reason", async () => {
    mocks.previewPrivateCapitalCall.mockResolvedValue({ call: null, isReady: false, blockers: ["Call exceeds approved uncalled commitment"] });
    mount(); await ready(); fillCall(); fireEvent.click(screen.getByRole("button", { name: "Preview call allocation" }));
    await screen.findByText("Call exceeds approved uncalled commitment");
    expect(screen.queryByRole("button", { name: "Retain reviewed call" })).not.toBeInTheDocument();
  });
  it("preserves the exact request for retry after an unconfirmed write", async () => {
    mocks.createPrivateCapitalCommitment.mockRejectedValueOnce(new Error("Network interruption")).mockResolvedValueOnce(capitalCommitment);
    mount(); await ready(); fillCommitment(); fireEvent.click(screen.getByRole("button", { name: "Retain commitment draft" }));
    await screen.findByText("Write outcome is unconfirmed"); expect(screen.getByLabelText("Ledger book")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await waitFor(() => expect(mocks.createPrivateCapitalCommitment).toHaveBeenCalledTimes(2));
    expect(mocks.createPrivateCapitalCommitment.mock.calls[1][1]).toEqual(mocks.createPrivateCapitalCommitment.mock.calls[0][1]);
  });
  it("queues a separately identified partial receipt using retained installment scope", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall], notices: [{ runId: "notice-a", callId: "call-a", investorId: "investor-a", governanceState: "Released", packageId: "package-a", deliveryState: "Pending", route: "/reporting/runs/detail?runId=notice-a" }] });
    mocks.intakePrivateCapitalFunding.mockResolvedValue({ producerSkips: [], intake: { created: [], skipped: [], needsFixCount: 0 }, evidenceAssessments: {}, readiness: 0, readinessBlockers: [] });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    expect(screen.getByText("investor-a · Governance: Released · Delivery: Pending")).toBeInTheDocument();
    enter("Posted installment", "call-a:commitment-a"); enter("Funding receipt ID", "receipt-2"); enter("Bank transaction line ID", CAPITAL_BANK_LINE_ID);
    enter("Cash received", "50000.01"); enter("Cash received date", "2026-11-10"); enter("Retained bank or remittance evidence (one reference per line)", CAPITAL_EVIDENCE);
    fireEvent.click(screen.getByLabelText("I matched this cash receipt to the installment and retained bank evidence."));
    fireEvent.click(screen.getByRole("button", { name: "Queue funding draft for review" }));
    await waitFor(() => expect(mocks.intakePrivateCapitalFunding).toHaveBeenCalledOnce());
    expect(mocks.intakePrivateCapitalFunding.mock.calls[0][0]).toMatchObject({ ledgerBookId: CAPITAL_BOOK_ID, entityId: CAPITAL_ENTITY_ID, periodId: CAPITAL_PERIOD_ID, fundings: [{ commitmentId: "commitment-a", investorId: "investor-a", capitalAccountId: "capital-a", fundingReceiptId: "receipt-2", bankLineId: CAPITAL_BANK_LINE_ID, fundedAmount: "50000.01", evidenceLinks: [CAPITAL_EVIDENCE] }] });
    expect(mocks.generatePrivateCapitalNotice).not.toHaveBeenCalled();
  });
  it("blocks earlier scope while a different book selection is unapplied", async () => {
    mocks.getPrivateCapitalBooks.mockResolvedValue([capitalBook, { ...capitalBook, ledgerBookId: "book-b", displayName: "Book B" }]); mount(); await ready();
    enter("Ledger book", "book-b");
    expect(screen.getByRole("button", { name: "Retain commitment draft" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview call allocation" })).toBeDisabled();
    expect(within(screen.getByRole("group", { name: "Capital activity for selected book" })).getByLabelText("Commitment ID")).toBeDisabled();
  });
  it("offers one effective approved revision and never revives an inactive successor", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), commitments: [
      capitalCommitment,
      { ...capitalCommitment, revision: 2, effectiveFrom: "2026-06-01" },
      { ...capitalCommitment, revision: 3, effectiveFrom: "2026-12-01" },
      { ...capitalCommitment, revision: 4, effectiveFrom: "2027-02-01", isActive: false },
      { ...capitalCommitment, revision: 5, effectiveFrom: "2027-03-01", approvalStatus: 0 }
    ] });
    mount(); await ready();
    const eligible = within(screen.getByRole("group", { name: "Approved commitments" }));
    expect(eligible.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(eligible.getByText("Enter a notice date to select the effective approved commitment revisions.")).toBeInTheDocument();
    enter("Notice date", "2026-11-03");
    expect(eligible.getAllByRole("checkbox")).toHaveLength(1);
    expect(eligible.getByLabelText(/commitment-a · investor-a · v2 · Uncalled/)).toBeInTheDocument();
    enter("Notice date", "2026-12-01");
    expect(eligible.getAllByRole("checkbox")).toHaveLength(1);
    expect(eligible.getByLabelText(/commitment-a · investor-a · v3 · Uncalled/)).toBeInTheDocument();
    enter("Notice date", "2027-03-02");
    expect(eligible.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(eligible.getByText("No active approved commitment revision is effective for this notice date.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preview call allocation" })).toBeDisabled();
  });
  it("clears selected commitments and reviewed preview when the notice date changes", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), commitments: [capitalCommitment,
      { ...capitalCommitment, revision: 2, effectiveFrom: "2026-06-01" },
      { ...capitalCommitment, revision: 3, effectiveFrom: "2026-06-01" }
    ] });
    mocks.previewPrivateCapitalCall.mockResolvedValue({ call: capitalCall, isReady: true, blockers: [] });
    mount(); await ready();
    enter("Capital call ID", "call-a"); enter("Amount to call", "250000.01"); enter("Accounting period ID", CAPITAL_PERIOD_ID);
    enter("Notice date", "2026-11-03"); enter("Due date", "2026-11-20");
    const eligible = within(screen.getByRole("group", { name: "Approved commitments" }));
    expect(eligible.getAllByRole("checkbox")).toHaveLength(1);
    fireEvent.click(eligible.getByLabelText(/commitment-a · investor-a · v3 · Uncalled/));
    fireEvent.click(screen.getByRole("button", { name: "Preview call allocation" }));
    await screen.findByRole("region", { name: "Authoritative call preview" });
    expect(mocks.previewPrivateCapitalCall.mock.calls[0][1].commitmentIds).toEqual(["commitment-a"]);
    fireEvent.click(screen.getByLabelText("I reviewed each allocation and the uncalled balances."));
    enter("Notice date", "2026-03-01");
    expect(eligible.getByLabelText(/commitment-a · investor-a · v1 · Uncalled/)).not.toBeChecked();
    expect(screen.queryByRole("region", { name: "Authoritative call preview" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retain reviewed call" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Preview call allocation" }));
    await screen.findByText("Select at least one approved commitment.");
    expect(mocks.previewPrivateCapitalCall).toHaveBeenCalledOnce();
  });
  it("shows an accessible overdue receivable indication from the server observation", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall], generatedAtUtc: "2026-11-21T00:00:00Z" });
    mount(); await ready();
    expect(screen.getByText("Overdue receivable")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /^call-a · 250000.01 USD/ }));
    const detail = within(screen.getByRole("region", { name: "Call call-a" }));
    expect(detail.getByText("Overdue receivable")).toBeInTheDocument();
    expect(detail.getByRole("status")).toHaveTextContent("Evaluated at server observation 2026-11-21T00:00:00Z");
  });
  it("imports reviewed bank evidence without posting and requires a fresh funding match", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.importPrivateCapitalBankObservation.mockResolvedValue(capitalBankObservationImport);
    mocks.intakePrivateCapitalFunding.mockResolvedValue({ producerSkips: [], intake: { created: [], skipped: [], needsFixCount: 0 }, evidenceAssessments: {}, readiness: 0, readinessBlockers: [] });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    enter("Posted installment", "call-a:commitment-a"); enter("Funding receipt ID", "receipt-import-2");
    fireEvent.click(screen.getByLabelText("I matched this cash receipt to the installment and retained bank evidence."));
    enter("Bank observation Evidence Vault reference", CAPITAL_EVIDENCE);
    enter("Bank observation document ID", capitalBankObservationImport.documentId);
    fireEvent.click(screen.getByRole("button", { name: "Import reviewed bank observation" }));
    await screen.findByText(/Bank observation retained/);
    expect(mocks.importPrivateCapitalBankObservation).toHaveBeenCalledWith(CAPITAL_BOOK_ID, { evidenceLink: CAPITAL_EVIDENCE, documentId: capitalBankObservationImport.documentId }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByLabelText("Bank transaction line ID")).toHaveValue(CAPITAL_BANK_LINE_ID);
    expect(screen.getByLabelText("Cash received")).toHaveValue("50000.01");
    expect(screen.getByLabelText("Cash received date")).toHaveValue("2026-11-10");
    expect(screen.getByLabelText("Retained bank or remittance evidence (one reference per line)")).toHaveValue(CAPITAL_EVIDENCE);
    expect(screen.getByLabelText("I matched this cash receipt to the installment and retained bank evidence.")).not.toBeChecked();
    expect(mocks.intakePrivateCapitalFunding).not.toHaveBeenCalled(); expect(mocks.intakePrivateCapitalCall).not.toHaveBeenCalled();
    expect(mocks.getPrivateCapitalWorkbench).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByLabelText("I matched this cash receipt to the installment and retained bank evidence."));
    fireEvent.click(screen.getByRole("button", { name: "Queue funding draft for review" }));
    await waitFor(() => expect(mocks.intakePrivateCapitalFunding).toHaveBeenCalledOnce());
    expect(mocks.intakePrivateCapitalFunding.mock.calls[0][0]).toMatchObject({ receivedDate: "2026-11-10", fundings: [{ fundingReceiptId: "receipt-import-2", bankLineId: CAPITAL_BANK_LINE_ID, fundedAmount: "50000.01", evidenceLinks: [CAPITAL_EVIDENCE] }] });
  });
  it("leaves cash fields unchanged when reviewed source import conflicts", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.importPrivateCapitalBankObservation.mockRejectedValue(new ApiError({ path: "/api/ledger/private-capital/bank-observations", status: 409, detail: "The bank observation identity already contains different source content." }));
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    enter("Bank observation Evidence Vault reference", CAPITAL_EVIDENCE); enter("Bank observation document ID", capitalBankObservationImport.documentId);
    fireEvent.click(screen.getByRole("button", { name: "Import reviewed bank observation" }));
    await screen.findByText(/The bank observation identity already contains different source content/);
    expect(screen.getByLabelText("Bank transaction line ID")).toHaveValue("");
    expect(screen.getByLabelText("Cash received")).toHaveValue("");
    expect(screen.getByLabelText("Cash received date")).toHaveValue("");
    expect(screen.queryByText(/Bank observation retained/)).not.toBeInTheDocument();
    expect(mocks.intakePrivateCapitalFunding).not.toHaveBeenCalled(); expect(mocks.intakePrivateCapitalCall).not.toHaveBeenCalled();
  });
  it("recovers an unconfirmed bank import with the same source and preserves the returned match", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.importPrivateCapitalBankObservation.mockRejectedValueOnce(new Error("Lost bank import acknowledgement"))
      .mockResolvedValueOnce({ ...capitalBankObservationImport, isReplay: true });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    enter("Bank observation Evidence Vault reference", CAPITAL_EVIDENCE); enter("Bank observation document ID", capitalBankObservationImport.documentId);
    fireEvent.click(screen.getByRole("button", { name: "Import reviewed bank observation" }));
    await screen.findByText("Write outcome is unconfirmed");
    fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await screen.findByText(/Existing bank observation recovered/);
    expect(mocks.importPrivateCapitalBankObservation.mock.calls[1][1]).toEqual(mocks.importPrivateCapitalBankObservation.mock.calls[0][1]);
    expect(mocks.getPrivateCapitalWorkbench).toHaveBeenCalledOnce();
    expect(screen.getByLabelText("Bank transaction line ID")).toHaveValue(CAPITAL_BANK_LINE_ID);
    expect(screen.getByLabelText("Cash received")).toHaveValue("50000.01");
    expect(screen.getByLabelText("I matched this cash receipt to the installment and retained bank evidence.")).not.toBeChecked();
    expect(mocks.intakePrivateCapitalFunding).not.toHaveBeenCalled();
  });
  it("keeps exact retained decimal text and revision identity in the shared tables", async () => {
    const exact = "12345678901234567890.1200";
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(),
      commitments: [{ ...capitalCommitment, totalCommitment: exact }],
      calls: [{ ...capitalCall, lines: [{ ...capitalCall.lines[0], amount: exact }] }] });
    mount(); await ready();
    const register = within(screen.getByRole("table", { name: "Commitment register and posted capital balances" }));
    expect(register.getByText(`${exact} USD`)).toBeInTheDocument();
    expect(register.getByRole("row", { name: "Commitment commitment-a, revision 1, investor investor-a" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    const allocation = within(screen.getByRole("table", { name: "Call allocation and posted progress" }));
    expect(allocation.getByText(`${exact} USD`)).toBeInTheDocument();
    expect(allocation.getByRole("row", { name: "Installment call-a:commitment-a, investor investor-a, commitment revision 1" })).toBeInTheDocument();
  });
  it("retries the original notice operation after a lost response and mints a new operation only for deliberate regeneration", async () => {
    const firstOperation = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    const secondOperation = "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77";
    const runId = `capital-call-notice-op-${firstOperation.replaceAll("-", "")}`;
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValueOnce(firstOperation).mockReturnValueOnce(secondOperation);
    const initial = { ...capitalWorkbench(), calls: [capitalCall] };
    const recovered = { ...initial, notices: [{ runId, callId: "call-a", investorId: "investor-a", governanceState: "Draft", packageId: null, deliveryState: "NotQueued", route: `/reporting/runs/detail?runId=${runId}` }] };
    mocks.getPrivateCapitalWorkbench.mockResolvedValueOnce(initial).mockResolvedValue(recovered);
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new Error("The generated notice response was lost"))
      .mockResolvedValueOnce({ run: { runId } }).mockResolvedValueOnce({ run: { runId: `capital-call-notice-op-${secondOperation.replaceAll("-", "")}` } });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Generate notice for investor-a" }));
    await screen.findByText("Write outcome is unconfirmed");
    expect(screen.getByRole("button", { name: "Recover original notice for investor-a" })).toBeDisabled();
    const originalBody = mocks.generatePrivateCapitalNotice.mock.calls[0][3];
    expect(originalBody).toEqual({ operationId: firstOperation });
    fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await screen.findByRole("button", { name: "Regenerate notice for investor-a" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toBe(originalBody);
    expect(uuid).toHaveBeenCalledOnce();
    expect(screen.getByRole("link", { name: `Inspect notice run ${runId}` })).toHaveAttribute("href", `/reporting/runs/detail?runId=${runId}`);
    expect(screen.getByText(/Regenerate creates a new notice run; retrying an unconfirmed request recovers its original run/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Regenerate notice for investor-a" }));
    await waitFor(() => expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledTimes(3));
    expect(mocks.generatePrivateCapitalNotice.mock.calls[2][3]).toEqual({ operationId: secondOperation });
    expect(uuid).toHaveBeenCalledTimes(2);
  });
  it("admits one notice operation for a double click while generation is pending", async () => {
    const operationId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(operationId);
    let complete!: (value: PrivateCapitalNoticeGenerationResult) => void;
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    const generate = screen.getByRole("button", { name: "Generate notice for investor-a" });
    act(() => { generate.click(); generate.click(); });
    expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce();
    expect(mocks.generatePrivateCapitalNotice.mock.calls[0][3]).toEqual({ operationId });
    expect(uuid).toHaveBeenCalledOnce();
    expect(generate).toBeDisabled();
    await act(async () => { complete({ run: { runId: `capital-call-notice-op-${operationId.replaceAll("-", "")}` } }); });
  });
  it("recovers a lost notice response after unmount and remount without minting or automatically sending another operation", async () => {
    const operationId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(operationId);
    const target = noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a");
    const key = noticeOperationStorageKey(target);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall], notices: [{ runId: "older-notice", callId: "call-a", investorId: "investor-a", governanceState: "Released", packageId: "old-package", deliveryState: "Delivered", route: "/reporting/runs/detail?runId=older-notice" }] });
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new Error("Successful notice acknowledgement was lost"))
      .mockResolvedValueOnce({ run: { runId: `capital-call-notice-op-${operationId.replaceAll("-", "")}` } });
    const first = mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Regenerate notice for investor-a" }));
    await screen.findByText("Write outcome is unconfirmed");
    const originalBody = mocks.generatePrivateCapitalNotice.mock.calls[0][3];
    expect(JSON.parse(sessionStorage.getItem(key)!)).toEqual({ version: 1, target, operationId });
    first.unmount();
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    const recover = await screen.findByRole("button", { name: "Recover original notice for investor-a" });
    expect(screen.getByRole("link", { name: "Inspect notice run older-notice" })).toBeInTheDocument();
    expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce(); expect(uuid).toHaveBeenCalledOnce();
    expect(sessionStorage.getItem(key)).not.toBeNull();
    fireEvent.click(recover);
    await screen.findByRole("button", { name: "Regenerate notice for investor-a" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual(originalBody);
    expect(uuid).toHaveBeenCalledOnce(); expect(sessionStorage.getItem(key)).toBeNull();
  });
  it("does not send notice generation if its original operation cannot be retained", async () => {
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    const generate = await screen.findByRole("button", { name: "Generate notice for investor-a" });
    await waitFor(() => expect(generate).not.toBeDisabled());
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Recovery storage unavailable"); });
    fireEvent.click(generate);
    await screen.findByText(/Notice generation has not been sent because its original operation could not be retained/);
    expect(mocks.generatePrivateCapitalNotice).not.toHaveBeenCalled();
  });
  it("retains the original notice operation when a success response identifies another run", async () => {
    const operationId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    vi.spyOn(crypto, "randomUUID").mockReturnValue(operationId);
    const key = noticeOperationStorageKey(noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a"));
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockResolvedValue({ run: { runId: "a-different-notice-run" } });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(await screen.findByRole("button", { name: "Generate notice for investor-a" }));
    await screen.findByText(/The notice result does not match the original operation/);
    expect(JSON.parse(sessionStorage.getItem(key)!).operationId).toBe(operationId);
    expect(mocks.getPrivateCapitalWorkbench).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Recover original notice for investor-a" })).toBeDisabled();
  });
  it("starts a fresh notice only after exact terminal failure and an explicit new action", async () => {
    const firstId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66", nextId = "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77";
    const key = noticeOperationStorageKey(noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a"));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValueOnce(firstId).mockReturnValueOnce(nextId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new ApiError({ path: "/notice", status: 409,
      responseBody: JSON.stringify({ code: "notice_operation_failed", runId: `capital-call-notice-op-${firstId.replaceAll("-", "")}` }) }))
      .mockImplementationOnce(async () => {
        expect(JSON.parse(sessionStorage.getItem(key)!).operationId).toBe(nextId);
        return { run: { runId: `capital-call-notice-op-${nextId.replaceAll("-", "")}` } };
      });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Generate notice for investor-a" }));
    const startNew = await screen.findByRole("button", { name: "Generate new notice after confirmed failure for investor-a" });
    expect(JSON.parse(sessionStorage.getItem(key)!).operationId).toBe(firstId);
    expect(uuid).toHaveBeenCalledOnce(); expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce();
    expect(screen.getByText(/The original notice run failed; its audit remains retained/)).toBeInTheDocument();
    fireEvent.click(startNew);
    await waitFor(() => expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledTimes(2));
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual({ operationId: nextId });
    expect(uuid).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(sessionStorage.getItem(key)).toBeNull());
  });
  it.each<[string, number, Record<string, string>]>([
    ["generic conflict", 409, { error: "The operation needs review" }],
    ["in-flight operation", 409, { code: "notice_operation_in_progress", runId: "capital-call-notice-op-8f7c8a521a444f4e9a2b6f0d5a7c9e66" }],
    ["read-only refusal", 403, { code: "notice_operation_failed", runId: "capital-call-notice-op-8f7c8a521a444f4e9a2b6f0d5a7c9e66" }],
    ["server unavailable", 503, { code: "notice_operation_failed", runId: "capital-call-notice-op-8f7c8a521a444f4e9a2b6f0d5a7c9e66" }],
    ["foreign run", 409, { code: "notice_operation_failed", runId: "capital-call-notice-op-9f7c8a521a444f4e9a2b6f0d5a7c9e77" }]
  ])("keeps the original notice identity for %s", async (_scenario, status, body) => {
    const operationId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    const key = noticeOperationStorageKey(noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a"));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(operationId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new ApiError({ path: "/notice", status, responseBody: JSON.stringify(body) }));
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Generate notice for investor-a" }));
    await screen.findByRole("button", { name: "Recover original notice for investor-a" });
    await screen.findByText("Capital activity unavailable");
    expect(screen.queryByRole("button", { name: /Generate new notice after confirmed failure/ })).not.toBeInTheDocument();
    expect(JSON.parse(sessionStorage.getItem(key)!).operationId).toBe(operationId);
    expect(uuid).toHaveBeenCalledOnce(); expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce();
  });
  it("requires a fresh terminal confirmation after reload before offering a new notice", async () => {
    const operationId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66";
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(operationId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockRejectedValue(new ApiError({ path: "/notice", status: 409,
      responseBody: JSON.stringify({ code: "notice_operation_failed", runId: `capital-call-notice-op-${operationId.replaceAll("-", "")}` }) }));
    const first = mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Generate notice for investor-a" }));
    await screen.findByRole("button", { name: "Generate new notice after confirmed failure for investor-a" });
    first.unmount();
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    const recover = await screen.findByRole("button", { name: "Recover original notice for investor-a" });
    expect(screen.queryByRole("button", { name: /Generate new notice after confirmed failure/ })).not.toBeInTheDocument();
    expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce();
    fireEvent.click(recover);
    await screen.findByRole("button", { name: "Generate new notice after confirmed failure for investor-a" });
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual({ operationId });
    expect(uuid).toHaveBeenCalledOnce();
  });
  it("preserves a confirmed failed intent when retaining its explicit replacement fails", async () => {
    const firstId = "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66", nextId = "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77";
    const key = noticeOperationStorageKey(noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a"));
    const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValueOnce(firstId).mockReturnValueOnce(nextId);
    mocks.getPrivateCapitalWorkbench.mockResolvedValue({ ...capitalWorkbench(), calls: [capitalCall] });
    mocks.generatePrivateCapitalNotice.mockRejectedValueOnce(new ApiError({ path: "/notice", status: 409,
      responseBody: JSON.stringify({ code: "notice_operation_failed", runId: `capital-call-notice-op-${firstId.replaceAll("-", "")}` }) }))
      .mockResolvedValueOnce({ run: { runId: `capital-call-notice-op-${nextId.replaceAll("-", "")}` } });
    mount(); await ready(); fireEvent.click(screen.getByRole("button", { name: "call-a · 250000.01 USD" }));
    fireEvent.click(screen.getByRole("button", { name: "Generate notice for investor-a" }));
    const startNew = await screen.findByRole("button", { name: "Generate new notice after confirmed failure for investor-a" });
    const writes = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage unavailable"); });
    fireEvent.click(startNew);
    await screen.findByText("Write outcome is unconfirmed");
    expect(JSON.parse(sessionStorage.getItem(key)!).operationId).toBe(firstId);
    expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledOnce();
    writes.mockRestore();
    fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await waitFor(() => expect(mocks.generatePrivateCapitalNotice).toHaveBeenCalledTimes(2));
    expect(mocks.generatePrivateCapitalNotice.mock.calls[1][3]).toEqual({ operationId: nextId });
    expect(uuid).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(sessionStorage.getItem(key)).toBeNull());
  });
});
