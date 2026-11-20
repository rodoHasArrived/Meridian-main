import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import * as receipts from "@/lib/api/income-receipt-queue.api";
import * as ledger from "@/lib/ledger-reports-api";
import { renderWithRouter } from "@/test/render";
import { receiptBooks, receiptRun } from "@/test/book-receipt-fixture";
import type { BookReceiptRun } from "@/types/income-receipt-queue.types";
import { IncomeReceiptQueuePanel } from "./income-receipt-queue-panel";

vi.mock("@/lib/api/income-receipt-queue.api");
vi.mock("@/lib/ledger-reports-api", () => ({ getLedgerBooks: vi.fn() }));
vi.mock("@/lib/table-workbench", async importOriginal => ({ ...await importOriginal<typeof import("@/lib/table-workbench")>(), loadTableViews: vi.fn().mockResolvedValue([]) }));

function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(complete => { resolve = complete; }); return { promise, resolve }; }
function renderQueue(path = "/accounting/reconciliation?ledgerBookId=book-1") { return renderWithRouter(<IncomeReceiptQueuePanel operatorIdentity="Operator" />, { initialEntries: [path] }); }
async function loaded() { return screen.findByRole("treegrid", { name: "Book receipt obligations" }); }

describe("book receipt queue", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(ledger.getLedgerBooks).mockResolvedValue(receiptBooks);
    vi.mocked(receipts.getBookReceiptRuns).mockResolvedValue([receiptRun()]);
    vi.mocked(receipts.getBookReceiptRun).mockImplementation(async (bookId, runId) => receiptRun({ runId, scope: { tenantId: "tenant-1", companyId: "company-1", ledgerBookId: bookId } }));
    vi.mocked(receipts.runBookReceiptQueue).mockResolvedValue(receiptRun({ runId: "new-run", sequence: 2 }));
  });
  it("keeps four distinct amounts and only reads when opened", async () => {
    renderQueue(); const table = await loaded();
    for (const amount of ["1,250.00 USD", "500.00 USD", "250.00 USD", "750.00 USD"]) expect(within(table).getByText(amount)).toBeInTheDocument();
    expect(within(table).getByText("2 days after due")).toBeInTheDocument();
    expect(within(table).getByText("Morgan Lee")).toBeInTheDocument();
    expect(receipts.runBookReceiptQueue).not.toHaveBeenCalled();
    expect(ledger.getLedgerBooks).toHaveBeenCalledWith(expect.objectContaining({ allowDevelopmentFallback: false }));
  });
  it("retains currency precision without Number conversion", async () => {
    const run = receiptRun(); run.items[0]!.expectedAmount = "9007199254740993.123456789"; run.items[0]!.currency = "KWD";
    run.items.push({ ...run.items[0]!, obligationKey: "jpy", displayName: "Yen principal", currency: "JPY", expectedAmount: "1000" });
    vi.mocked(receipts.getBookReceiptRun).mockResolvedValue(run);
    renderQueue(); const table = await loaded();
    expect(within(table).getByText("9,007,199,254,740,993.123456789 KWD")).toBeInTheDocument();
    expect(within(table).getByText("1,000 JPY")).toBeInTheDocument();
  });
  it("filters every obligation state and incomplete evidence independently", async () => {
    const run = receiptRun(); const states = ["Scheduled", "Due", "Overdue", "PartiallyReceived", "Disputed", "ReceivedButUnposted", "Reconciled", "IncompleteEvidence"];
    run.items = states.map((status, index) => ({ ...run.items[0]!, status, obligationKey: status, displayName: `Instrument ${index}`, evidenceComplete: status !== "Due" && status !== "IncompleteEvidence" }));
    vi.mocked(receipts.getBookReceiptRun).mockResolvedValue(run);
    renderQueue(); await loaded();
    for (const status of states) { fireEvent.change(screen.getByLabelText("Receipt status"), { target: { value: status } }); expect(screen.getAllByRole("row", { name: /Inspect Instrument/ })).toHaveLength(1); }
    fireEvent.change(screen.getByLabelText("Receipt status"), { target: { value: "evidence" } });
    expect(screen.getAllByRole("row", { name: /Inspect Instrument/ })).toHaveLength(2);
  });
  it("opens history, split receipts, reversals and existing exceptions with keyboard", async () => {
    const run = receiptRun(); run.items[0]!.receipts.push({ ...run.items[0]!.receipts[0]!, receiptId: "reversal-1", amount: "-500.00", reversesReceiptId: "receipt-1" });
    run.items[0]!.evidenceLinks.push("javascript:alert(1)", "/api/workstation/instruments/security-1/evidence");
    vi.mocked(receipts.getBookReceiptRun).mockResolvedValue(run);
    const user = userEvent.setup(); renderQueue(); await loaded();
    const inspect = screen.getByRole("row", { name: "Inspect Atlas 5.25% 2028 Coupon receipt" }); inspect.focus(); await user.keyboard("{Enter}");
    await user.click(screen.getByText("Expectation history (2)"));
    expect(screen.getByText("Observed 2026-11-17 · 1,200.00 USD")).toBeVisible();
    expect(screen.getByText("Observed 2026-11-20 · 1,250.00 USD")).toBeVisible();
    await user.click(screen.getByText("Cash receipts (2)"));
    expect(screen.getByText("Reverses receipt receipt-1")).toBeVisible();
    expect(screen.getAllByText("position:principal:2026-11-18: 1,000.00 USD")).toHaveLength(2);
    expect(screen.getByRole("link", { name: "Open linked exception" })).toHaveAttribute("href", "/accounting/reconciliation?ledgerBookId=book-1&reconciliationBreak=receipt-exception-1");
    expect(screen.queryByRole("link", { name: "javascript:alert(1)" })).not.toBeInTheDocument();
    await user.click(screen.getByText("Source links and obligation identity"));
    const apiEvidence = screen.getAllByRole("link", { name: "/api/workstation/instruments/security-1/evidence" })[0]!;
    expect(apiEvidence).toHaveAttribute("href", "/api/workstation/instruments/security-1/evidence");
    expect(apiEvidence).toHaveAttribute("target", "_blank");
    expect(apiEvidence).not.toHaveAttribute("data-discover");
  });
  it("does not fabricate an exception link when none was retained", async () => {
    const run = receiptRun(); run.items[0]!.exceptionId = null; vi.mocked(receipts.getBookReceiptRun).mockResolvedValue(run);
    renderQueue(); await loaded(); fireEvent.click(screen.getByRole("row", { name: /Inspect Atlas/ }));
    expect(screen.queryByRole("link", { name: "Open linked exception" })).not.toBeInTheDocument();
  });
  it("defaults to latest business date, not the newest backdated observation", async () => {
    vi.mocked(receipts.getBookReceiptRuns).mockResolvedValue([receiptRun({ runId: "backdated", asOfDate: "2026-11-01", sequence: 50 }), receiptRun({ runId: "current", sequence: 2 })]);
    renderQueue(); await loaded();
    expect(receipts.getBookReceiptRun).toHaveBeenCalledWith("book-1", "current", expect.anything());
    fireEvent.change(screen.getByLabelText("Retained receipt run"), { target: { value: "backdated" } });
    await waitFor(() => expect(receipts.getBookReceiptRun).toHaveBeenLastCalledWith("book-1", "backdated", expect.anything()));
    expect(receipts.runBookReceiptQueue).not.toHaveBeenCalled();
  });
  it("runs the explicit chosen date once while a command is pending", async () => {
    const pending = deferred<BookReceiptRun>(); vi.mocked(receipts.runBookReceiptQueue).mockReturnValue(pending.promise);
    renderQueue(); await loaded();
    fireEvent.change(screen.getByLabelText("Receipt as of date"), { target: { value: "2026-11-19" } });
    fireEvent.click(screen.getByRole("button", { name: "Run daily check" }));
    expect(screen.getByRole("button", { name: "Running daily check…" })).toBeDisabled();
    expect(receipts.runBookReceiptQueue).toHaveBeenCalledTimes(1);
    expect(receipts.runBookReceiptQueue).toHaveBeenCalledWith("book-1", "2026-11-19", expect.anything());
    await act(async () => pending.resolve(receiptRun({ runId: "generated", asOfDate: "2026-11-19" })));
    await waitFor(() => expect(screen.getByLabelText("Retained receipt run")).toHaveValue("generated"));
  });
  it("discards a late read when the selected book changes", async () => {
    const pending = deferred<BookReceiptRun>();
    vi.mocked(receipts.getBookReceiptRun).mockImplementation((bookId) => bookId === "book-1" ? pending.promise : Promise.resolve(receiptRun({ runId: "daily-run-1", items: [], scope: { tenantId: "tenant-1", companyId: "company-1", ledgerBookId: "book-2" } })));
    renderQueue(); await waitFor(() => expect(receipts.getBookReceiptRun).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText("Receipt ledger book"), { target: { value: "book-2" } });
    await screen.findByText(/No expected obligations were established/);
    await act(async () => pending.resolve(receiptRun()));
    expect(screen.queryByText("Atlas 5.25% 2028")).not.toBeInTheDocument();
  });
  it("discards a late mutation after switching books", async () => {
    const pending = deferred<BookReceiptRun>(); vi.mocked(receipts.runBookReceiptQueue).mockReturnValue(pending.promise);
    renderQueue(); await loaded(); fireEvent.click(screen.getByRole("button", { name: "Run daily check" }));
    fireEvent.change(screen.getByLabelText("Receipt ledger book"), { target: { value: "book-2" } });
    await loaded(); await act(async () => pending.resolve(receiptRun({ runId: "stale-mutation" })));
    expect(screen.getByLabelText("Receipt ledger book")).toHaveValue("book-2");
    expect(screen.queryByRole("option", { name: /stale-mutation/ })).not.toBeInTheDocument();
  });
  it("makes empty incomplete coverage visible, without claiming reconciliation", async () => {
    vi.mocked(receipts.getBookReceiptRun).mockResolvedValue(receiptRun({ items: [], coverage: [{ area: "Custodian receipts", isComplete: false, reason: "Provider mapping missing" }] }));
    renderQueue(); await screen.findByText("Provider coverage is incomplete");
    expect(screen.getByText("Custodian receipts: Provider mapping missing")).toBeVisible();
    expect(screen.queryByText("Evidence complete")).not.toBeInTheDocument();
  });
  it("surfaces denied runs while keeping retained evidence readable", async () => {
    vi.mocked(receipts.runBookReceiptQueue).mockRejectedValue(new Error("Permission denied: manage ledger required"));
    renderQueue(); await loaded(); fireEvent.click(screen.getByRole("button", { name: "Run daily check" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Permission denied");
    expect(screen.getByText("Atlas 5.25% 2028")).toBeInTheDocument();
  });
  it("shows no-runs and unavailable-book states without a mutation", async () => {
    vi.mocked(receipts.getBookReceiptRuns).mockResolvedValue([]);
    renderQueue(); expect(await screen.findByText(/No retained receipt run for this book/)).toBeVisible();
    expect(receipts.runBookReceiptQueue).not.toHaveBeenCalled();
  });
  it("has accessible controls and selectable evidence", async () => {
    const { container } = renderQueue(); await loaded();
    expect((await axe(container)).violations).toEqual([]);
  });
});
