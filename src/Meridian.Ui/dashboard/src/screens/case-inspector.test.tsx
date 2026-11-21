import { MemoryRouter } from "react-router-dom";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { CaseInspector, CaseVariance, caseChanges } from "./case-inspector";
import { buildSuccessfulVerifiedOperationOutcome } from "@/test/verified-operation-outcome";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkOperationResult } from "@/types";

const record: ReconciliationBreakQueueItem = { breakId: "cash-1", runId: "run-1", strategyName: "Cash review",
  category: "AmountMismatch", status: "Open", variance: 500, reason: "Cash variance needs review", assignedTo: "ops",
  detectedAt: "2026-01-01T00:00:00Z", lastUpdatedAt: "2026-01-01T00:00:00Z", reviewedBy: null, reviewedAt: null,
  resolvedBy: null, resolvedAt: null, resolutionNote: null, version: 1,
  permittedCommands: [{ action: "Resolve", enabled: true, disabledReason: null }, { action: "Assign", enabled: false, disabledReason: "Requires an operations role." }],
  measures: [{ kind: "Value", expected: 100, actual: 600, variance: 500, tolerance: 1, unit: "USD" }] };
function result(item: ReconciliationBreakQueueItem, overrides: Partial<ReconciliationCaseworkOperationResult> = {}): ReconciliationCaseworkOperationResult {
  return { transitionStatus: "Success", item, outcome: buildSuccessfulVerifiedOperationOutcome(), ...overrides };
}
function setup(readRecord: ReconciliationBreakQueueItem = record) {
  const services = { read: vi.fn().mockResolvedValue(readRecord), audit: vi.fn().mockResolvedValue([]),
    resolve: vi.fn().mockResolvedValue(result({ ...record, version: 2, status: "Resolved" })),
    commands: { TransitionStatus: vi.fn(), Assign: vi.fn(), Waive: vi.fn(), SignOff: vi.fn(), Reopen: vi.fn(), Supersede: vi.fn() } };
  const onChanged = vi.fn();
  const view = render(<MemoryRouter><CaseInspector caseId="cash-1" initialCase={record} services={services} onChanged={onChanged} /></MemoryRouter>);
  return { ...view, services, onChanged, user: userEvent.setup() };
}

describe("shared case inspector", () => {
  it("prefers exact wire text for comparison sides and the queue fallback above the safe integer limit", async () => {
    const precise = { ...record, variance: 9007199254740992, varianceExact: "9007199254740993.125",
      measures: [{ ...record.measures![0], expected: 9007199254740992, expectedExact: "9007199254740993.125",
        actual: 9007199254740996, actualExact: "9007199254740994.126", varianceExact: "1.001",
        toleranceExact: "0.0000000000000000000000000001" }] };
    const { rerender } = setup(precise);
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    const comparison = screen.getByRole("region", { name: "Comparison" });
    expect(within(comparison).getByText("9,007,199,254,740,993.13 USD")).toBeInTheDocument();
    expect(within(comparison).getByText("9,007,199,254,740,994.13 USD")).toBeInTheDocument();
    rerender(<MemoryRouter><CaseVariance item={{ ...precise, measures: undefined }} /></MemoryRouter>);
    expect(screen.getByText("+9,007,199,254,740,993.13 Unit unavailable")).toBeInTheDocument();
  });

  it("detects exact fractional changes even when numeric compatibility fields compare equal", () => {
    const before = { ...record, measures: [{ ...record.measures![0], variance: 0.001,
      varianceExact: "0.0010000000000000000000000001" }] };
    const after = { ...before, measures: [{ ...before.measures[0], varianceExact: "0.0010000000000000000000000002" }] };
    expect(caseChanges(before, after)).toEqual([{ label: "Variance", before: "0.0010000000000000000000000001", after: "0.0010000000000000000000000002" }]);
    expect(caseChanges(before, { ...before, measures: [{ ...before.measures[0], varianceExact: "0.00100000000000000000000000010" }] })).toEqual([]);
  });

  it("opens a visible form and sends the exact loaded version and decision fields before showing a receipt", async () => {
    const { user, services, onChanged } = setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    await user.type(screen.getByRole("textbox", { name: "Root cause code" }), "TimingDifference");
    await user.type(screen.getByRole("textbox", { name: "Resolution code" }), "EvidenceAccepted");
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Reviewed the retained cash evidence");
    expect(services.resolve).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    await waitFor(() => expect(services.resolve).toHaveBeenCalledWith(expect.objectContaining({ breakId: "cash-1", expectedVersion: 1,
      rootCauseCode: "TimingDifference", resolutionCode: "EvidenceAccepted", operatorRationale: "Reviewed the retained cash evidence" })));
    expect(await screen.findByRole("status", { name: "Decision receipt" })).toHaveTextContent("Succeeded");
    expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ version: 2, status: "Resolved" }));
  });

  it("shows semantic conflicts and reuses the latest version only after review and another explicit confirmation", async () => {
    const { user, services } = setup();
    const latest = { ...record, version: 2, assignedTo: "controller", variance: 600, measures: [{ ...record.measures![0], variance: 600 }] };
    services.resolve.mockResolvedValueOnce(result(latest, { transitionStatus: "Conflict", errorCode: "ConcurrencyConflict" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Reviewed");
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    const conflict = await screen.findByRole("alert", { name: "Case changed" });
    expect(conflict).toHaveTextContent("controller");
    expect(conflict).toHaveTextContent("600");
    expect(services.resolve).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Confirm resolve" })).toBeDisabled();
    services.read.mockResolvedValue(latest);
    await user.click(screen.getByRole("button", { name: "Review latest version" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Confirm resolve" })).toBeEnabled());
    expect(services.resolve).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    await waitFor(() => expect(services.resolve).toHaveBeenLastCalledWith(expect.objectContaining({ expectedVersion: 2 })));
  });

  it("normalizes HTTP409 into a current-record comparison without retrying the decision", async () => {
    const { user, services } = setup();
    services.resolve.mockRejectedValue(new ApiError({ path: "/case", status: 409 }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Reviewed");
    services.read.mockResolvedValue({ ...record, version: 3, status: "Resolved" });
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    expect(await screen.findByRole("alert", { name: "Case changed" })).toHaveTextContent("Resolved");
    expect(services.resolve).toHaveBeenCalledTimes(1);
  });

  it("starts review through the guarded lifecycle command rather than silently resolving an open case", async () => {
    const { user, services } = setup({ ...record, permittedCommands: [{ action: "TransitionStatus", enabled: true, disabledReason: null }] });
    services.commands.TransitionStatus.mockResolvedValue(result({ ...record, status: "InReview", version: 2 }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Start review" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Start review" }));
    await user.type(screen.getByRole("textbox", { name: "Start review rationale" }), "Investigate the cash disagreement");
    await user.click(screen.getByRole("button", { name: "Confirm start review" }));
    await waitFor(() => expect(services.commands.TransitionStatus).toHaveBeenCalledWith(expect.objectContaining({
      action: "TransitionStatus", status: "Investigating", expectedVersion: 1, assignee: "ops"
    })));
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("cancels with Escape and returns keyboard focus to the decision trigger", async () => {
    const { user, services } = setup();
    const trigger = screen.getByRole("button", { name: "Resolve" });
    await waitFor(() => expect(trigger).toBeEnabled());
    await user.click(trigger);
    await waitFor(() => expect(screen.getByRole("textbox", { name: "Root cause code" })).toHaveFocus());
    await user.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(screen.queryByRole("form", { name: "Resolve case" })).not.toBeInTheDocument();
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("shows role requirements and makes missing comparison evidence explicit", async () => {
    const { user } = setup({ ...record, measures: [] });
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByText("Other decisions"));
    expect(screen.getByRole("button", { name: "Assign" })).toBeDisabled();
    expect(screen.getByText("Comparison unavailable: No retained comparison pair is recorded for this case.")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Case inspector" })).getByRole("button", { name: "Assign" })).toHaveAttribute("aria-describedby");
  });

  it("has no axe violations in the populated decision form", async () => {
    const { user, container } = setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    expect((await axe(container)).violations).toEqual([]);
  });

  it("reports only actual changes, preserving zero and absent values", () => {
    expect(caseChanges(record, { ...record, variance: 0, measures: [{ ...record.measures![0], variance: 0 }], assignedTo: null })).toEqual([
      { label: "Owner", before: "ops", after: "Not recorded" }, { label: "Variance", before: "500", after: "0" }
    ]);
  });
});
