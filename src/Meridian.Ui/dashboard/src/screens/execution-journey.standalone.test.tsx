import { act, screen, waitFor, within } from "@testing-library/react";
import { Link } from "react-router-dom";
import { renderWithRouter as render } from "@/test/render";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { chronologicalJourney, ExecutionJourney } from "./execution-journey";
import { searchExecutionAuditTrail } from "@/lib/api/execution-audit.api";
import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";
vi.mock("@/lib/api/execution-audit.api", () => ({ searchExecutionAuditTrail: vi.fn() }));
afterEach(() => vi.resetAllMocks());
const entry: AuditTrailTimelineEntry = { auditId: "submit", occurredAt: "2026-11-12T12:00:00Z", objectKind: "Order", objectId: "order-1", orderId: "order-1", category: "Order", action: "OrderSubmitted", outcome: "Accepted", actor: "operator" };
const result: AuditTrailExplorerResult = { asOf: "2026-11-12", returned: 2, totalMatched: 3, sources: [{ source: "PaperSessionFillHistory", state: "Unavailable" }], coverageNote: "Retained evidence only.", entries: [{ ...entry, auditId: "uncertain", action: "OrderSubmissionUncertain", outcome: "RecoveryRequired", occurredAt: "2026-11-12T12:02:00Z" }, entry] };
describe("execution journey", () => {
  it("orders retained events by timestamps, deduplicates identities and preserves invalid timestamp evidence", () => {
    expect(chronologicalJourney([result.entries[0], entry, entry, { ...entry, auditId: "invalid", occurredAt: "unknown" }]).map((row) => row.auditId)).toEqual(["submit", "uncertain", "invalid"]);
  });
  it("queries exact order identity, discloses coverage and expands event evidence", async () => {
    vi.mocked(searchExecutionAuditTrail).mockResolvedValue(result);
    render(<ExecutionJourney recent={result} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Journey order ID"), "order-1");
    await user.click(screen.getByRole("button", { name: "Trace order" }));
    await screen.findByText(/Order order-1 · 2 of 3/);
    expect(searchExecutionAuditTrail).toHaveBeenCalledWith(expect.objectContaining({ orderId: "order-1", limit: 1000 }), expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByRole("alert")).toHaveTextContent("Journey truncated");
    expect(screen.getByText(/Sources:/)).toHaveTextContent("Unavailable");
    expect(screen.getByLabelText("Journey stage coverage")).toHaveTextContent("Fill report: not recorded / unknown");
    const timeline = within(screen.getByLabelText("Chronological order events"));
    await user.click(timeline.getByText("OrderSubmitted"));
    expect(timeline.getByText("OrderSubmitted").closest("details")).toHaveAttribute("open");
    expect(screen.getAllByText("not separately retained")).toHaveLength(2);
  });
  it("invalidates a previous request when the order changes", async () => {
    let complete!: (value: AuditTrailExplorerResult) => void;
    vi.mocked(searchExecutionAuditTrail).mockReturnValue(new Promise((resolve) => { complete = resolve; }));
    const user = userEvent.setup(); render(<ExecutionJourney recent={null} />);
    await user.type(screen.getByLabelText("Journey order ID"), "A"); await user.click(screen.getByRole("button", { name: "Trace order" }));
    await user.clear(screen.getByLabelText("Journey order ID")); await user.type(screen.getByLabelText("Journey order ID"), "B");
    await act(async () => complete(result));
    await waitFor(() => expect(screen.queryByText(/recorded events · as of/)).not.toBeInTheDocument());
  });
  it("discards delayed evidence after the operating scope changes", async () => {
    let complete!: (value: AuditTrailExplorerResult) => void;
    vi.mocked(searchExecutionAuditTrail).mockReturnValue(new Promise((resolve) => { complete = resolve; }));
    const user = userEvent.setup();
    render(<><Link to="/trading/risk?fundAccountId=B">Switch operating scope</Link><ExecutionJourney recent={null} /></>, { initialEntries: ["/trading/risk?fundAccountId=A"] });
    await user.type(screen.getByLabelText("Journey order ID"), "A"); await user.click(screen.getByRole("button", { name: "Trace order" }));
    const signal = vi.mocked(searchExecutionAuditTrail).mock.calls[0][1]!.signal!;
    await user.click(screen.getByRole("link", { name: "Switch operating scope" }));
    expect(signal.aborted).toBe(true);
    await act(async () => complete(result));
    expect(screen.getByLabelText("Journey order ID")).toHaveValue("");
    expect(screen.queryByText(/recorded events · as of/)).not.toBeInTheDocument();
  });
  it("surfaces ambiguous identity without implying an absent order", async () => {
    vi.mocked(searchExecutionAuditTrail).mockResolvedValue({ ...result, orderIdentityStatus: "Ambiguous" });
    const user = userEvent.setup(); render(<ExecutionJourney recent={null} />);
    await user.type(screen.getByLabelText("Journey order ID"), "17"); await user.click(screen.getByRole("button", { name: "Trace order" }));
    expect(await screen.findByText(/Order identity is ambiguous/)).toBeInTheDocument();
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
  });
});
