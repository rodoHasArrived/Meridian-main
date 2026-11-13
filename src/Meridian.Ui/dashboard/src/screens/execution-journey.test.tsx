import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ExecutionJourneyPanel } from "@/screens/execution-journey";
import { buildExecutionJourney } from "@/screens/execution-journey.view-model";
import * as api from "@/lib/api/execution-audit.api";
import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";
import type { TradingFill } from "@/types";

vi.mock("@/lib/api/execution-audit.api", () => ({ searchExecutionAuditTrail: vi.fn() }));
const search = vi.mocked(api.searchExecutionAuditTrail);
const audit: AuditTrailTimelineEntry = { auditId: "a-1", occurredAt: "2026-06-01T12:01:00Z", objectKind: "Order", objectId: "order-1", category: "Order", action: "OrderSubmitted", outcome: "PendingApproval", actor: "Operator", actionLedgerSequence: 91, currentActionHash: "hash", actionLedgerStatus: "WalRetained", metadata: { retained: "original evidence" } };
const result = (entries: AuditTrailTimelineEntry[]): AuditTrailExplorerResult => ({ asOf: "2026-06-01T12:05:00Z", totalMatched: entries.length, returned: entries.length, entries, sourceAvailability: { executionAudit: true, operationsContinuity: false } });
const fill: TradingFill = { fillId: "fill-1", orderId: "order-1", symbol: "ABC", side: "Buy", quantity: "2", price: "100.00", venue: "TEST", timestamp: "2026-06-01T12:00:00Z" };
afterEach(() => vi.resetAllMocks());

describe("execution journey", () => {
  it("joins only explicit identities, deduplicates audit and fill IDs, and orders actual and recorded times", () => {
    const related = { ...audit, auditId: "a-2", objectKind: "ExecutionControl", objectId: "control-1", category: "Control", relatedObjectIds: ["order-1"], actionLedgerSequence: 109 };
    const unrelated = { ...audit, auditId: "not-this-order", objectId: "order-2", correlationId: "shared" };
    const vm = buildExecutionJourney("order-1", [result([audit, related, unrelated]), result([audit])], [fill, fill, { ...fill, fillId: "other", orderId: "order-2" }]);
    expect(vm.events.map((event) => event.id)).toEqual(["fill:fill-1", "audit:a-1", "audit:a-2"]);
    expect(vm.events.map((event) => event.timestampKind)).toEqual(["Occurred", "Recorded", "Recorded"]);
    expect(vm.hasControlEvidence).toBe(true);
    expect(vm.truncated).toBe(false);
  });

  it("shows retained evidence and unknown controls without treating a sequence jump as a missing event", async () => {
    search.mockResolvedValue(result([audit]));
    const user = userEvent.setup();
    render(<ExecutionJourneyPanel orderId="order-1" fills={[fill]} />);
    await screen.findByText(/Submission: OrderSubmitted/);
    expect(search).toHaveBeenCalledWith({ orderId: "order-1", limit: 1000 }, expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByText(/Control outcomes are unknown/)).toBeInTheDocument();
    await user.click(screen.getByText(/Submission: OrderSubmitted/));
    expect(screen.getByText("original evidence")).toBeVisible();
    expect(screen.getByText(/WalRetained #91/)).toBeVisible();
    expect(screen.getByText(/Sequence jumps can reflect other orders/)).toBeInTheDocument();
    expect(within(screen.getByRole("list")).getAllByRole("listitem")).toHaveLength(2);
  });

  it("reports unavailable and truncated sources explicitly", async () => {
    search.mockResolvedValue({ ...result([]), totalMatched: 1200, returned: 0, sourceAvailability: { executionAudit: false, operationsContinuity: false } });
    render(<ExecutionJourneyPanel orderId="order-1" />);
    expect(await screen.findByText(/Execution audit source is not configured/)).toBeInTheDocument();
    expect(screen.getByText(/Results truncated/)).toBeInTheDocument();
  });
  it("does not mix ambiguous order evidence with previous links or loaded fills", async () => {
    const ambiguous = { ...result([audit]), orderIdentityStatus: "Ambiguous" };
    expect(buildExecutionJourney("order-1", [ambiguous, result([audit])], [fill]).events).toEqual([]);
    search.mockResolvedValue(ambiguous);
    render(<ExecutionJourneyPanel orderId="order-1" fills={[fill]} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Order identity is ambiguous");
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
  });

  it("discards an old order response after selection changes", async () => {
    let resolveOld!: (value: AuditTrailExplorerResult) => void;
    const old = new Promise<AuditTrailExplorerResult>((resolve) => { resolveOld = resolve; });
    search.mockImplementation((query) => query?.orderId === "order-1" ? old : Promise.resolve(result([{ ...audit, auditId: "new", objectId: "order-2", action: "SecondOrder", }])));
    const { rerender } = render(<ExecutionJourneyPanel orderId="order-1" />);
    rerender(<ExecutionJourneyPanel orderId="order-2" />);
    await screen.findByText(/SecondOrder/);
    resolveOld(result([audit]));
    await waitFor(() => expect(screen.queryByText(/OrderSubmitted/)).not.toBeInTheDocument());
    expect(screen.getByText(/SecondOrder/)).toBeInTheDocument();
  });
});
