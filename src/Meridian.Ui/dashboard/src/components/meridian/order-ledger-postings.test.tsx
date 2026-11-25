import { screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { bookForAccount, executionLookups, OrderLedgerPostings } from "@/components/meridian/order-ledger-postings";
import { getExecutionJournalEntries, getLedgerBooks } from "@/lib/ledger-reports-api";
import { renderWithRouter } from "@/test/render";
import type { JournalOrder } from "@/lib/api/execution-workflows.api";
import type { LedgerBook } from "@/types";

vi.mock("@/lib/ledger-reports-api", async (load) => ({ ...await load<typeof import("@/lib/ledger-reports-api")>(), getLedgerBooks: vi.fn(), getExecutionJournalEntries: vi.fn() }));

const fillGuid = "6f8b2c1e-0000-4000-8000-000000000001";
const order = {
  orderId: "PO-1", scope: "paper", accountId: "11111111-2222-3333-4444-555555555555", symbol: "AAPL", side: "Buy", status: "Filled",
  quantity: 10, filledQuantity: 10, createdAt: "2026-06-30T00:00:00Z", updatedAt: "2026-06-30T00:00:00Z",
  events: [
    { eventId: "e1", kind: "Fill", recordedAt: "2026-06-30T00:00:00Z", activityFill: { fillId: fillGuid.toUpperCase(), quantity: 10, price: 50, filledAt: "2026-06-30T00:00:00Z" } },
    { eventId: "e2", kind: "Fill", recordedAt: "2026-06-30T00:00:00Z", activityFill: { fillId: "broker-fill-77", quantity: 0, price: 50, filledAt: "2026-06-30T00:00:00Z" } }
  ]
} as unknown as JournalOrder;
const book = (id: string, node: string, kind = "Account") => ({ ledgerBookId: id, fundProfileId: "f", fundStructureNodeId: node, fundStructureNodeKind: kind, displayName: `Book ${id}`, baseCurrency: "USD", createdAt: "", updatedAt: "", accountingBasis: "Primary", accountingPolicyId: "p", accountingPolicyVersion: "1" }) as LedgerBook;
const entry = (id: string, timestamp: string) => ({ journalEntryId: id, periodId: "period-6", ledgerBookId: "b1", timestamp, description: `Posting ${id}`, totalDebits: 500, totalCredits: 500, isBalanced: true, lines: [] });

describe("order ledger postings", () => {
  beforeEach(() => vi.clearAllMocks());

  it("matches a book only by the account's exact fund-structure node", () => {
    expect(bookForAccount([book("b1", order.accountId.toUpperCase()), book("b2", "other")], order.accountId)?.ledgerBookId).toBe("b1");
    expect(bookForAccount([book("b1", order.accountId, "Fund")], order.accountId)).toBeNull();
    expect(bookForAccount([book("b1", order.accountId), book("b2", order.accountId)], order.accountId)).toBeNull();
  });

  it("looks up the order and only fill ids that can be ledger fill identifiers", () => {
    expect(executionLookups(order)).toEqual([{ orderId: "PO-1" }, { fillId: fillGuid }]);
  });

  it("lists postings found by order and fill once each, linking to the journal entry", async () => {
    vi.mocked(getLedgerBooks).mockResolvedValue([book("b1", order.accountId)]);
    vi.mocked(getExecutionJournalEntries).mockImplementation(async (_book, source) =>
      "orderId" in source ? [entry("je-1", "2026-06-30T10:00:00Z")] as never : [entry("je-1", "2026-06-30T10:00:00Z"), entry("je-2", "2026-06-30T09:00:00Z")] as never);
    renderWithRouter(<OrderLedgerPostings order={order} />);

    const list = await screen.findByRole("list", { name: "Ledger postings for order PO-1" });
    const links = within(list).getAllByRole("link");
    expect(links.map((link) => link.textContent)).toEqual(["je-2", "je-1"]);
    expect(links[1]).toHaveAttribute("href", "/accounting/journal-entries/detail?journalEntryId=je-1&periodId=period-6");
    expect(getExecutionJournalEntries).toHaveBeenCalledWith("b1", { orderId: "PO-1" }, expect.anything());
    expect(getExecutionJournalEntries).toHaveBeenCalledWith("b1", { fillId: fillGuid }, expect.anything());
  });

  it("asks for the book when no book belongs to the account, and searches nothing until chosen", async () => {
    const user = userEvent.setup();
    vi.mocked(getLedgerBooks).mockResolvedValue([book("b9", "fund-node", "Fund")]);
    vi.mocked(getExecutionJournalEntries).mockResolvedValue([]);
    renderWithRouter(<OrderLedgerPostings order={order} />);

    const select = await screen.findByRole("combobox");
    expect(getExecutionJournalEntries).not.toHaveBeenCalled();
    await user.selectOptions(select, "b9");
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("No postings in Book b9 recorded this order or its fill."));
  });
});
