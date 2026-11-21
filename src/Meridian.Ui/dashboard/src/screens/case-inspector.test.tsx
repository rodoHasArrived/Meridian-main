import { MemoryRouter } from "react-router-dom";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { CaseInspector, CaseVariance, caseChanges } from "./case-inspector";
import { caseResidual } from "./reconciliation-case-values";
import { buildSuccessfulVerifiedOperationOutcome } from "@/test/verified-operation-outcome";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkOperationResult, ReconciliationCaseworkCommand } from "@/types";

const record: ReconciliationBreakQueueItem = { breakId: "cash-1", runId: "run-1", strategyName: "Cash review",
  category: "AmountMismatch", status: "Open", variance: 500, reason: "Cash variance needs review", assignedTo: "ops",
  detectedAt: "2026-01-01T00:00:00Z", lastUpdatedAt: "2026-01-01T00:00:00Z", reviewedBy: null, reviewedAt: null,
  resolvedBy: null, resolvedAt: null, resolutionNote: null, version: 1, rootCauseCode: "TimingDifference", resolutionCode: "EvidenceAccepted",
  permittedCommands: [{ action: "Resolve", enabled: true, disabledReason: null }, { action: "Assign", enabled: false, disabledReason: "Requires an operations role." }],
  measures: [{ kind: "Value", expected: 100, actual: 600, variance: 500, tolerance: 1, unit: "USD" }] };
function result(item: ReconciliationBreakQueueItem, overrides: Partial<ReconciliationCaseworkOperationResult> = {}): ReconciliationCaseworkOperationResult {
  return { transitionStatus: "Success", item, outcome: buildSuccessfulVerifiedOperationOutcome(), ...overrides };
}
function setup(readRecord: ReconciliationBreakQueueItem = record, options: { operatorId?: string; commandMemory?: Map<string, ReconciliationCaseworkCommand> } = {}) {
  const services = { read: vi.fn().mockResolvedValue(readRecord), audit: vi.fn().mockResolvedValue([]),
    resolve: vi.fn().mockResolvedValue(result({ ...record, version: 2, status: "Resolved" })),
    taxonomy: vi.fn().mockResolvedValue({ version: 1,
      rootCauses: [{ code: "TimingDifference", displayName: "Timing difference", version: 1, isActive: true }],
      resolutionCodes: [{ code: "EvidenceAccepted", displayName: "Evidence accepted", version: 1, isActive: true }] }),
    commands: { TransitionStatus: vi.fn(), Assign: vi.fn(), Waive: vi.fn(), SignOff: vi.fn(), Reopen: vi.fn(), Supersede: vi.fn(), SetRootCause: vi.fn(), SetResolution: vi.fn() } };
  const onChanged = vi.fn();
  const view = render(<MemoryRouter><CaseInspector {...options} caseId="cash-1" initialCase={record} services={services} onChanged={onChanged} /></MemoryRouter>);
  return { ...view, services, onChanged, user: userEvent.setup() };
}

describe("shared case inspector", () => {
  it("keeps exact case and scope identifiers inspectable without placing them in the primary facts", async () => {
    const { user, services } = setup({ ...record, fundAccountId: "fund-account-42", accountingPeriodId: "period-2026-05" });
    const inspector = screen.getByRole("region", { name: "Case inspector" });
    await waitFor(() => expect(inspector).toHaveAttribute("aria-busy", "false"));
    expect(inspector).toHaveAttribute("data-case-id", record.breakId);
    expect(screen.getByRole("heading", { name: record.reason })).toBeVisible();
    expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled();
    const identifiers = within(inspector).getByText("Case identifiers").closest("details")!;
    for (const value of [record.breakId, "fund-account-42", "period-2026-05"]) {
      expect(within(identifiers).getByText(value)).not.toBeVisible();
    }
    await user.click(within(identifiers).getByText("Case identifiers"));
    for (const value of [record.breakId, "fund-account-42", "period-2026-05"]) {
      expect(within(identifiers).getByText(value)).toBeVisible();
    }
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("states the shared unknown data condition once and retains its context on every comparison value", async () => {
    setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    const comparison = screen.getByRole("region", { name: "Comparison" });
    const notice = within(comparison).getByText(/^Data condition unavailable\./);
    expect(notice).toBeVisible();
    expect(within(comparison).getAllByText(/Data condition unavailable/)).toHaveLength(1);
    expect(within(comparison).queryByText("Confirmed")).not.toBeInTheDocument();
    for (const value of ["100.00 USD", "600.00 USD", "+500.00 USD", "1.00 USD"]) {
      const amount = within(comparison).getByText(value).closest("[aria-describedby]")!;
      expect(amount.getAttribute("aria-describedby")?.split(/\s+/)).toContain(notice.id);
      expect(amount).toHaveAttribute("title", "A verified data condition was not supplied for this case measure.");
    }
  });

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

  it("preserves small quantity residuals and tolerance precision in the inspector and queue primitive", async () => {
    const quantity: ReconciliationBreakQueueItem = { ...record, measures: [{ kind: "Quantity", expected: 1, actual: 1.0001, actualExact: "1.00010001",
      variance: 0.0001, varianceExact: "0.00010001", tolerance: 0.00001, toleranceExact: "0.0000000000000000000000000001", unit: "Shares" }] };
    const { rerender } = setup(quantity);
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    const comparison = within(screen.getByRole("region", { name: "Comparison" }));
    expect(comparison.getByText("+0.00010001 Shares")).toBeInTheDocument();
    expect(comparison.getByText("0.0000000000000000000000000001 Shares")).toBeInTheDocument();
    expect(comparison.queryByRole("button", { name: /Amount passport/ })).not.toBeInTheDocument();
    rerender(<MemoryRouter><CaseVariance item={quantity} /></MemoryRouter>);
    expect(screen.getByText("+0.00010001 Shares")).toBeInTheDocument();
    expect(screen.queryByText(/0\.00 Shares/)).not.toBeInTheDocument();
  });

  it("keeps all retained quantity and basis dimensions visible instead of selecting one scalar residual", async () => {
    const multiple: ReconciliationBreakQueueItem = { ...record, measures: [
      { kind: "Quantity", expected: 1, actual: 2, variance: 1, tolerance: 0, unit: "Shares" },
      { kind: "CostBasis", expected: 100, actual: 110, variance: 10, tolerance: 0.01, unit: "EUR" }
    ] };
    const { rerender } = setup(multiple);
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    const comparison = within(screen.getByRole("table", { name: "Retained residual components" }));
    expect(comparison.getByRole("rowheader", { name: /Quantity\s*Shares/ })).toBeVisible();
    expect(comparison.getByRole("rowheader", { name: /Cost basis\s*EUR/ })).toBeVisible();
    expect(comparison.queryByRole("button", { name: /Inspect Quantity residual/ })).not.toBeInTheDocument();
    rerender(<MemoryRouter><CaseVariance item={multiple} /></MemoryRouter>);
    expect(screen.getByText("Multiple")).toBeInTheDocument();
    rerender(<MemoryRouter><CaseVariance item={{ ...record, measures: [{ ...record.measures![0], actualExact: null }] }} /></MemoryRouter>);
    expect(screen.getByText("Unavailable")).toBeInTheDocument();
    expect(screen.queryByText(/500/)).not.toBeInTheDocument();
  });

  it.each([false, true])("keeps incomplete quantity coverage visible beside zero basis without a scalar money header (reversed: %s)", async (reversed) => {
    const measures: NonNullable<ReconciliationBreakQueueItem["measures"]> = [
      { kind: "Quantity", expected: null, actual: null, variance: null, tolerance: 0.01,
        unit: "Shares", unavailableReason: "Native quantity coverage is unavailable." },
      { kind: "CostBasis", expected: 100, actual: 100, variance: 0, tolerance: 0.01, unit: "EUR" }
    ];
    const mixed: ReconciliationBreakQueueItem = { ...record, variance: 0, measures: reversed ? measures.slice().reverse() : measures };
    const { rerender } = setup(mixed);
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    const comparison = within(screen.getByRole("table", { name: "Retained residual components" }));
    const quantity = within(comparison.getByRole("row", { name: /Quantity\s*Shares/ }));
    expect(quantity.getAllByText("Unavailable")).toHaveLength(3);
    const basis = within(comparison.getByRole("row", { name: /Cost basis\s*EUR/ }));
    expect(basis.getAllByText("100")).toHaveLength(2);
    expect(basis.getByRole("button", { name: /Inspect Cost basis residual 0/ })).toBeVisible();
    expect(caseResidual(mixed)).toBeNull();
    rerender(<MemoryRouter><CaseVariance item={mixed} /></MemoryRouter>);
    expect(screen.getByText("Multiple")).toBeInTheDocument();
    expect(screen.queryByText(/0(?:\.00)? EUR/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Amount passport/ })).not.toBeInTheDocument();
  });

  it("opens a visible form and sends the exact loaded version and decision fields before showing a receipt", async () => {
    const { user, services, onChanged } = setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Root cause code" }), "TimingDifference");
    await user.selectOptions(screen.getByRole("combobox", { name: "Resolution code" }), "EvidenceAccepted");
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Reviewed the retained cash evidence");
    expect(services.resolve).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    await waitFor(() => expect(services.resolve).toHaveBeenCalledWith(expect.objectContaining({ breakId: "cash-1", expectedVersion: 1,
      rootCauseCode: "TimingDifference", resolutionCode: "EvidenceAccepted", operatorRationale: "Reviewed the retained cash evidence" })));
    expect(await screen.findByRole("status", { name: "Decision receipt" })).toHaveTextContent("Succeeded");
    expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ version: 2, status: "Resolved" }));
  });

  it("returns focus after a successful decision only once its pending case refresh enables the trigger", async () => {
    const assignable: ReconciliationBreakQueueItem = { ...record, permittedCommands: [{ action: "Assign", enabled: true, disabledReason: null }] };
    const updated = { ...assignable, version: 2 };
    const { user, services } = setup(assignable);
    const trigger = await screen.findByRole("button", { name: "Assign" });
    await waitFor(() => expect(trigger).toBeEnabled());
    let finishRefresh!: (item: ReconciliationBreakQueueItem) => void;
    services.read.mockReturnValueOnce(new Promise<ReconciliationBreakQueueItem>((resolve) => { finishRefresh = resolve; }));
    services.commands.Assign.mockResolvedValue(result(updated));
    await user.click(trigger);
    await user.type(screen.getByRole("textbox", { name: "Assign rationale" }), "Reviewed assignment");
    await user.click(screen.getByRole("button", { name: "Confirm assign" }));
    await screen.findByRole("status", { name: "Decision receipt" });
    expect(trigger).toBeDisabled();
    expect(trigger).not.toHaveFocus();
    await act(async () => { finishRefresh(updated); });
    await waitFor(() => { expect(trigger).toBeEnabled(); expect(trigger).toHaveFocus(); });
    expect(services.commands.Assign).toHaveBeenCalledTimes(1);
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

  it("classifies an investigating case through guarded taxonomy forms without resolving it", async () => {
    const investigating: ReconciliationBreakQueueItem = { ...record, status: "InReview", lifecycleState: "Investigating", version: 7, rootCauseCode: null, resolutionCode: null,
      permittedCommands: [{ action: "SetRootCause", enabled: true, disabledReason: null }, { action: "SetResolution", enabled: true, disabledReason: null }] };
    const { user, services } = setup(investigating);
    const classified = { ...investigating, version: 8, rootCauseCode: "TimingDifference" };
    services.commands.SetRootCause.mockImplementation(async () => {
      services.read.mockResolvedValue(classified);
      return result(classified);
    });
    services.commands.SetResolution.mockImplementation(async () => {
      const updated = { ...classified, version: 9, resolutionCode: "EvidenceAccepted" };
      services.read.mockResolvedValue(updated);
      return result(updated);
    });
    await waitFor(() => expect(screen.getByRole("region", { name: "Case inspector" })).toHaveAttribute("aria-busy", "false"));
    expect(screen.queryByRole("button", { name: "Update root cause" })).not.toBeInTheDocument();
    await user.click(screen.getByText("Other decisions"));
    await user.click(screen.getByRole("button", { name: "Update root cause" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Root cause code" }), "TimingDifference");
    expect(screen.queryByRole("combobox", { name: "Resolution code" })).not.toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Update root cause rationale" }), "Confirmed timing evidence");
    expect(services.commands.SetRootCause).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Confirm update root cause" }));
    await waitFor(() => expect(services.commands.SetRootCause).toHaveBeenCalledWith(expect.objectContaining({
      action: "SetRootCause", expectedVersion: 7, rootCauseCode: "TimingDifference", reason: "Confirmed timing evidence"
    })));
    await waitFor(() => expect(screen.getByRole("button", { name: "Update resolution" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Update resolution" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Resolution code" }), "EvidenceAccepted");
    expect(screen.queryByRole("combobox", { name: "Root cause code" })).not.toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Update resolution rationale" }), "Retained resolution plan");
    await user.click(screen.getByRole("button", { name: "Confirm update resolution" }));
    await waitFor(() => expect(services.commands.SetResolution).toHaveBeenCalledWith(expect.objectContaining({
      action: "SetResolution", expectedVersion: 8, resolutionCode: "EvidenceAccepted", reason: "Retained resolution plan"
    })));
    expect(await screen.findByRole("status", { name: "Decision receipt" })).toHaveTextContent("Succeeded");
    expect(services.resolve).not.toHaveBeenCalled();
  });

  it("honors the server refusal to change classification on a resolved case", async () => {
    const { user, services } = setup({ ...record, status: "Resolved", permittedCommands: [
      { action: "SetRootCause", enabled: false, disabledReason: "Resolved cases cannot change classification." }
    ] });
    await waitFor(() => expect(screen.getByRole("region", { name: "Case inspector" })).toHaveAttribute("aria-busy", "false"));
    await user.click(screen.getByText("Other decisions"));
    expect(screen.getByRole("button", { name: "Update root cause" })).toBeDisabled();
    expect(screen.getByText("Resolved cases cannot change classification.")).toBeInTheDocument();
    await user.click(screen.getByText("Timeline and discussion"));
    expect(screen.getByText("TimingDifference")).toBeVisible();
    expect(screen.getByText("EvidenceAccepted")).toBeVisible();
    expect(services.commands.SetRootCause).not.toHaveBeenCalled();
  });

  it("cancels with Escape and returns keyboard focus to the decision trigger", async () => {
    const { user, services } = setup();
    const trigger = screen.getByRole("button", { name: "Resolve" });
    await waitFor(() => expect(trigger).toBeEnabled());
    await user.click(trigger);
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Root cause code" })).toHaveFocus());
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

  it("checks retained audit before resuming the identical pending command and payload", async () => {
    const current = { ...record, tenantId: "tenant", companyId: "company", permittedCommands: [{ action: "Assign", enabled: true, disabledReason: null }] };
    const memory = new Map<string, ReconciliationCaseworkCommand>();
    const { user, services } = setup(current, { operatorId: "ops-user", commandMemory: memory });
    services.commands.Assign.mockRejectedValueOnce(new Error("Connection interrupted"))
      .mockResolvedValueOnce(result({ ...current, version: 2 }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Assign" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Assign" }));
    await user.type(screen.getByRole("textbox", { name: "Assign rationale" }), "Reviewed assignment");
    await user.click(screen.getByRole("button", { name: "Confirm assign" }));
    expect(await screen.findByRole("region", { name: "Pending case command" })).toHaveTextContent("Command outcome unconfirmed");
    expect(screen.queryByRole("button", { name: "Resume same command" })).not.toBeInTheDocument();
    const command = services.commands.Assign.mock.calls[0][0];
    expect([...memory.values()]).toEqual([command]);
    expect(services.commands.Assign).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Check command outcome" }));
    await user.click(await screen.findByRole("button", { name: "Resume same command" }));
    await waitFor(() => expect(services.commands.Assign).toHaveBeenCalledTimes(2));
    expect(services.commands.Assign).toHaveBeenNthCalledWith(2, command);
    await waitFor(() => expect(screen.queryByRole("region", { name: "Pending case command" })).not.toBeInTheDocument());
  });

  it("rereads an uncertain legacy resolve and requires fresh review without retrying it", async () => {
    const { user, services } = setup();
    services.resolve.mockRejectedValueOnce(new ApiError({ path: "/case/resolve", status: 503 }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Resolve" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Resolve" }));
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Reviewed evidence");
    services.read.mockResolvedValue({ ...record, version: 2, status: "Resolved", permittedCommands: [{ action: "Resolve", enabled: false, disabledReason: "Already resolved." }] });
    await user.click(screen.getByRole("button", { name: "Confirm resolve" }));
    expect(await screen.findByRole("alert", { name: "Case changed" })).toHaveTextContent("Resolved");
    expect(services.resolve).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: "Resume same command" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Review latest version" }));
    await waitFor(() => expect(screen.queryByRole("form", { name: "Resolve case" })).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Resolve" })).toBeDisabled();
    expect(services.resolve).toHaveBeenCalledTimes(1);
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
