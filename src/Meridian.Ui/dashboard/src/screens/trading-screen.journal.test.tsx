import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getJournal, getJournalOrder, getJournalSummary, type JournalOrder } from "@/lib/api/execution-workflows.api";
import { ExecutionJournalPanel } from "./trading-screen.journal";

vi.mock("@/lib/api/execution-workflows.api", () => ({
  getJournal: vi.fn(),
  getJournalOrder: vi.fn(),
  getJournalSummary: vi.fn(),
}));

const accountId = "8c6b0029-9422-4961-ad61-a0ab6cbb242d";
const recordedAt = "2026-11-10T15:00:00Z";
const fill: JournalOrder = {
  orderId: "reused", attemptId: "routed", accountId, scope: "paper", symbol: "SPY", side: "Buy", status: "Filled",
  quantity: 1, filledQuantity: 1, createdAt: recordedAt, updatedAt: recordedAt,
  events: [{ eventId: "fill", kind: "Fill", recordedAt, detail: "Filled SPY attempt" }],
};
const refusal: JournalOrder = {
  ...fill, attemptId: "refused", symbol: "AAPL", status: "Rejected", filledQuantity: 0,
  events: [{ eventId: "refusal", kind: "Rejected", recordedAt, detail: "Refused AAPL attempt" }],
};

describe("ExecutionJournalPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState(null, "", "/");
    vi.mocked(getJournal).mockResolvedValue([fill, refusal]);
    vi.mocked(getJournalSummary).mockResolvedValue({ orders: 2, fills: 1, coverage: "incomplete", method: "FIFO" });
  });

  afterEach(() => vi.useRealTimers());

  it("collapses an unscoped journal to one row without issuing requests or exposing inactive controls", async () => {
    vi.useFakeTimers();
    render(<ExecutionJournalPanel />);
    await act(() => vi.advanceTimersByTimeAsync(250));
    const journal = screen.getByRole("region", { name: "Operational trade journal" });
    expect(journal).toHaveTextContent(/^Select an account to see its journal$/);
    expect(journal).toHaveAttribute("aria-busy", "false");
    expect(within(journal).queryByRole("heading")).not.toBeInTheDocument();
    expect(within(journal).queryByRole("button")).not.toBeInTheDocument();
    expect(within(journal).queryByRole("textbox")).not.toBeInTheDocument();
    expect(getJournal).not.toHaveBeenCalled();
    expect(getJournalSummary).not.toHaveBeenCalled();
    expect(getJournalOrder).not.toHaveBeenCalled();
  });

  it("expands the journal and loads the selected account's evidence", async () => {
    const { rerender } = render(<ExecutionJournalPanel />);
    rerender(<ExecutionJournalPanel accountId={accountId} />);
    expect(screen.getByRole("region", { name: "Operational trade journal" })).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("heading", { name: "Trade journal" })).toBeVisible();
    expect(screen.getByRole("textbox", { name: "Symbol" })).toBeVisible();
    expect(await screen.findByRole("row", { name: /reused SPY/i })).toBeVisible();
    expect(getJournal).toHaveBeenCalledWith(accountId, expect.any(Object), expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByRole("button", { name: "Refresh journal" })).toBeEnabled();
    expect(screen.getByRole("region", { name: "Operational trade journal" })).toHaveAttribute("aria-busy", "false");
  });

  it("aborts an in-flight journal when account scope clears and ignores its late evidence", async () => {
    let resolveOrders!: (orders: JournalOrder[]) => void;
    vi.mocked(getJournal).mockImplementationOnce(() => new Promise((resolve) => { resolveOrders = resolve; }));
    const { rerender } = render(<ExecutionJournalPanel accountId={accountId} />);
    await waitFor(() => expect(getJournal).toHaveBeenCalledTimes(1));
    const signal = vi.mocked(getJournal).mock.calls[0]![2]!.signal!;
    rerender(<ExecutionJournalPanel accountId={null} />);
    expect(signal.aborted).toBe(true);
    expect(screen.getByRole("region", { name: "Operational trade journal" })).toHaveAttribute("aria-busy", "false");
    await act(async () => { resolveOrders([fill]); });
    expect(screen.getByRole("region", { name: "Operational trade journal" })).toHaveTextContent(/^Select an account to see its journal$/);
    expect(screen.queryByRole("row", { name: /reused SPY/i })).not.toBeInTheDocument();
    expect(screen.queryByText("incomplete")).not.toBeInTheDocument();
  });

  it("keeps account-scoped request failures visible and allows another refresh", async () => {
    vi.mocked(getJournal).mockRejectedValueOnce(new Error("Retained journal unavailable"));
    render(<ExecutionJournalPanel accountId={accountId} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Retained journal unavailable");
    expect(screen.getByRole("button", { name: "Refresh journal" })).toBeEnabled();
    expect(screen.queryByText("Select an account to see its journal")).not.toBeInTheDocument();
  });

  it("inspects each attempt independently when two rows share a client order ID", async () => {
    render(<ExecutionJournalPanel accountId={accountId} />);

    const refused = within(await screen.findByRole("row", { name: /reused AAPL/i })).getByRole("button", { name: "reused" });
    const routed = within(screen.getByRole("row", { name: /reused SPY/i })).getByRole("button", { name: "reused" });
    fireEvent.click(refused);
    expect(screen.getByText("Refused AAPL attempt")).toBeVisible();
    expect(routed).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(routed);
    expect(screen.getByText("Filled SPY attempt")).toBeVisible();
    expect(screen.queryByText("Refused AAPL attempt")).not.toBeInTheDocument();
    expect(getJournalOrder).not.toHaveBeenCalled();
  });
});
