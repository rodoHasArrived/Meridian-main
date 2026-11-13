import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
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
