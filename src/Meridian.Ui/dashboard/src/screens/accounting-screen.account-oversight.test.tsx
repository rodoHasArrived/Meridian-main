import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Link, MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AccountOversightPanel } from "./accounting-screen.account-oversight";
import type { LedgerBook } from "@/types";
import type { OversightContext, OversightScope } from "@/types/account-oversight";

const api = vi.hoisted(() => ({ listAccountOversightBooks: vi.fn(), getAccountOversightContext: vi.fn(), provisionAccountOversight: vi.fn() }));
vi.mock("@/lib/api/account-oversight.api", () => api);
vi.mock("./accounting-screen.account-cash-cases", () => ({
  AccountCashCasesPanel: ({ scope, context, onDirtyChange }: { scope: OversightScope; context: OversightContext; onDirtyChange?: (dirty: boolean) => void }) =>
    <div>Cases for {scope.accountId} {context.canWrite ? "Writable" : "Read only"}<button onClick={() => onDirtyChange?.(true)}>Mark import pending</button></div>
}));
vi.mock("./accounting-screen.account-close-prices", () => ({ AccountClosePricesPanel: () => <div>Retained price history</div> }));
vi.mock("./accounting-screen.account-cash-forecasts", () => ({ AccountCashForecastsPanel: () => <div>Retained forecast history</div> }));

const bookA: LedgerBook = {
  ledgerBookId: "11111111-1111-4111-8111-111111111111", fundProfileId: "fund-a",
  fundStructureNodeId: "22222222-2222-4222-8222-222222222222", fundStructureNodeKind: "Account",
  displayName: "Custodian account A", baseCurrency: "USD", createdAt: "2026-10-14T12:00:00Z", updatedAt: "2026-10-14T12:00:00Z",
  accountingBasis: "Primary", accountingPolicyId: "policy-a", accountingPolicyVersion: "1"
};
const bookB: LedgerBook = { ...bookA, ledgerBookId: "33333333-3333-4333-8333-333333333333",
  fundStructureNodeId: "44444444-4444-4444-8444-444444444444", displayName: "Custodian account B" };
const contextFor = (book: LedgerBook, canWrite = true): OversightContext => ({
  accountId: book.fundStructureNodeId, bookId: book.ledgerBookId, bookName: book.displayName,
  baseCurrency: book.baseCurrency, actor: "reviewer", canWrite, canReadPrices: true, canManagePrices: canWrite, canProvision: false
});
const route = (book: LedgerBook) => `/accounting/reconciliation?ledgerBookId=${book.ledgerBookId}&accountId=${book.fundStructureNodeId}`;
function show(path = "/accounting/reconciliation") {
  return render(<MemoryRouter initialEntries={[path]}><AccountOversightPanel /><Link to={route(bookB)}>Open other account</Link></MemoryRouter>);
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

beforeEach(() => {
  vi.resetAllMocks();
  api.listAccountOversightBooks.mockResolvedValue([bookA, bookB]);
  api.getAccountOversightContext.mockImplementation((scope: OversightScope) => Promise.resolve(contextFor(scope.ledgerBookId === bookA.ledgerBookId ? bookA : bookB)));
  api.provisionAccountOversight.mockResolvedValue({ accountId: bookA.fundStructureNodeId, bookId: bookA.ledgerBookId, provisioned: true });
});
afterEach(() => vi.restoreAllMocks());

describe("account oversight entry", () => {
  it("requires an explicit account book and never selects the first returned account", async () => {
    show();
    await screen.findByText("Select an account book to load retained cases, prices and forecasts.");
    expect(screen.getByLabelText("Account-owned ledger book")).toHaveValue("");
    expect(api.getAccountOversightContext).not.toHaveBeenCalled();
    expect(screen.queryByText(/Cases for/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Account-owned ledger book"), { target: { value: bookB.ledgerBookId } });
    await screen.findByText(`Cases for ${bookB.fundStructureNodeId} Writable`);
    expect(api.getAccountOversightContext).toHaveBeenCalledWith({ accountId: bookB.fundStructureNodeId, ledgerBookId: bookB.ledgerBookId, baseCurrency: "USD" }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("rejects a route whose account does not own the selected book", async () => {
    show(`/accounting/reconciliation?ledgerBookId=${bookA.ledgerBookId}&accountId=${bookB.fundStructureNodeId}`);
    await screen.findByText("Requested account scope is unavailable");
    expect(api.getAccountOversightContext).not.toHaveBeenCalled();
    expect(screen.queryByText(/Cases for/)).not.toBeInTheDocument();
  });

  it("uses server permissions for read-only state and administrative controls", async () => {
    api.getAccountOversightContext.mockResolvedValue(contextFor(bookA, false));
    show(route(bookA));
    await screen.findByText(`Cases for ${bookA.fundStructureNodeId} Read only`);
    expect(screen.queryByRole("button", { name: "Initialize oversight history" })).not.toBeInTheDocument();
    expect(screen.getByText("Signed in as reviewer · Read only")).toBeVisible();
  });

  it("discards late context responses when changing accounts", async () => {
    const first = deferred<OversightContext>();
    api.getAccountOversightContext.mockImplementation((scope: OversightScope) => scope.ledgerBookId === bookA.ledgerBookId ? first.promise : Promise.resolve(contextFor(bookB)));
    show(route(bookA));
    await waitFor(() => expect(api.getAccountOversightContext).toHaveBeenCalledTimes(1));
    const firstSignal = api.getAccountOversightContext.mock.calls[0][1].signal as AbortSignal;
    fireEvent.change(screen.getByLabelText("Account-owned ledger book"), { target: { value: bookB.ledgerBookId } });
    await screen.findByText(`Cases for ${bookB.fundStructureNodeId} Writable`);
    expect(firstSignal.aborted).toBe(true);
    await act(async () => first.resolve(contextFor(bookA)));
    expect(screen.queryByText(`Cases for ${bookA.fundStructureNodeId} Writable`)).not.toBeInTheDocument();
  });

  it("keeps a pending import when the operator cancels a scope change", async () => {
    const confirmation = vi.spyOn(window, "confirm").mockReturnValue(false);
    show(route(bookA));
    await screen.findByText(`Cases for ${bookA.fundStructureNodeId} Writable`);
    fireEvent.click(screen.getByRole("button", { name: "Mark import pending" }));
    fireEvent.change(screen.getByLabelText("Account-owned ledger book"), { target: { value: bookB.ledgerBookId } });
    expect(confirmation).toHaveBeenCalledOnce();
    expect(screen.getByLabelText("Account-owned ledger book")).toHaveValue(bookA.ledgerBookId);
    expect(screen.getByText(`Cases for ${bookA.fundStructureNodeId} Writable`)).toBeVisible();
  });

  it("retains all workflow panels while switching tabs", async () => {
    show(route(bookA));
    await screen.findByText(`Cases for ${bookA.fundStructureNodeId} Writable`);
    fireEvent.click(screen.getByRole("tab", { name: "Close prices" }));
    expect(screen.getByText("Retained price history")).toBeVisible();
    expect(screen.getByText(`Cases for ${bookA.fundStructureNodeId} Writable`)).not.toBeVisible();
    fireEvent.click(screen.getByRole("tab", { name: "Cash forecast" }));
    expect(screen.getByText("Retained forecast history")).toBeVisible();
    fireEvent.click(screen.getByRole("tab", { name: "Cash cases" }));
    expect(screen.getByText(`Cases for ${bookA.fundStructureNodeId} Writable`)).toBeVisible();
  });

  it("keeps unavailable account access explicit and permits a retry", async () => {
    api.listAccountOversightBooks.mockRejectedValueOnce(new Error("Account ownership is unavailable."));
    show(route(bookA));
    await screen.findByText("Account ownership is unavailable.");
    expect(screen.queryByText(/Cases for/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry account access" }));
    await screen.findByText(`Cases for ${bookA.fundStructureNodeId} Writable`);
  });

  it("aborts administrative initialization on navigation and does not show its result for another account", async () => {
    const provision = deferred<{ accountId: string; bookId: string; provisioned: boolean }>();
    api.getAccountOversightContext.mockImplementation((scope: OversightScope) => Promise.resolve({ ...contextFor(scope.ledgerBookId === bookA.ledgerBookId ? bookA : bookB), canProvision: true }));
    api.provisionAccountOversight.mockReturnValue(provision.promise);
    show(route(bookA));
    await screen.findByText(`Cases for ${bookA.fundStructureNodeId} Writable`);
    fireEvent.click(screen.getByRole("button", { name: "Initialize oversight history" }));
    const signal = api.provisionAccountOversight.mock.calls[0][1].signal as AbortSignal;
    fireEvent.click(screen.getByRole("link", { name: "Open other account" }));
    await screen.findByText(`Cases for ${bookB.fundStructureNodeId} Writable`);
    expect(signal.aborted).toBe(true);
    await act(async () => provision.resolve({ accountId: bookA.fundStructureNodeId, bookId: bookA.ledgerBookId, provisioned: true }));
    expect(screen.queryByText("History initialized")).not.toBeInTheDocument();
  });
});
