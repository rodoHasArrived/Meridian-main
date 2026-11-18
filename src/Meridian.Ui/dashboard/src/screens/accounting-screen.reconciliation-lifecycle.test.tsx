import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { BreakLifecyclePanel, type BreakLifecycleServices } from "./accounting-screen.reconciliation-lifecycle";
import { BreakAuditTimeline, auditEventLabel, orderAuditEvents } from "./accounting-screen.reconciliation-audit-timeline";
import { buildBreakLifecycleModel } from "./reconciliation-break-lifecycle";
import { classifyCaseworkResult, describeCaseworkRefusal, runReconciliationCaseworkCommand } from "./reconciliation-casework-command";
import { ApiError } from "@/lib/api-errors";
import type {
  ReconciliationBreakQueueAuditEvent,
  ReconciliationBreakQueueItem,
  ReconciliationCaseLifecycleState,
  ReconciliationCaseworkCommand,
  ReconciliationCaseworkOperationResult
} from "@/types";

function item(lifecycleState: ReconciliationCaseLifecycleState | null, overrides: Partial<ReconciliationBreakQueueItem> = {}): ReconciliationBreakQueueItem {
  return {
    breakId: "cash-1", runId: "run-1", strategyName: "Cash review", category: "AmountMismatch", status: "Open", variance: 500,
    reason: "Cash variance over tolerance.", assignedTo: null, detectedAt: "2026-10-01T00:00:00Z", lastUpdatedAt: "2026-10-01T00:00:00Z",
    reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null,
    version: 4, lifecycleState, evidenceLinks: ["journal:je-cash-1"], ...overrides
  };
}

function succeeded(updated: ReconciliationBreakQueueItem): ReconciliationCaseworkOperationResult {
  return { transitionStatus: "Success", item: updated, outcome: { state: "Succeeded", issues: [] } as unknown as ReconciliationCaseworkOperationResult["outcome"] };
}

function refused(errorCode: string, transitionStatus = "ValidationFailed"): ReconciliationCaseworkOperationResult {
  return { transitionStatus, errorCode, error: "server says no", outcome: { state: "Rejected", issues: [] } as unknown as ReconciliationCaseworkOperationResult["outcome"] };
}

function fakeServices(overrides: Partial<BreakLifecycleServices> = {}): BreakLifecycleServices {
  return {
    read: vi.fn(async () => item("Investigating", { version: 9 })),
    taxonomy: vi.fn(async () => ({
      version: 1,
      rootCauses: [
        { code: "BrokerCashTiming", displayName: "Broker cash timing", version: 1, isActive: true },
        { code: "LedgerClassification", displayName: "Ledger classification", version: 1, isActive: true }
      ],
      resolutionCodes: [{ code: "LedgerAdjusted", displayName: "Ledger adjusted", version: 1, isActive: true, requiredEvidencePrefixes: ["journal:"] }]
    })),
    setRootCause: vi.fn(async (command: ReconciliationCaseworkCommand) => succeeded(item("Investigating", { version: command.expectedVersion + 1, rootCauseCode: command.rootCauseCode }))),
    setResolution: vi.fn(async (command: ReconciliationCaseworkCommand) => succeeded(item("Investigating", { version: command.expectedVersion + 1, resolutionCode: command.resolutionCode }))),
    waive: vi.fn(async (command: ReconciliationCaseworkCommand) => succeeded(item("Resolved", { version: command.expectedVersion + 1, status: "Resolved" }))),
    signOff: vi.fn(async (command: ReconciliationCaseworkCommand) => succeeded(item("SignedOff", { version: command.expectedVersion + 1, status: "Resolved" }))),
    reopen: vi.fn(async (command: ReconciliationCaseworkCommand) => succeeded(item("Reopened", { version: command.expectedVersion + 1 }))),
    ...overrides
  };
}

describe("buildBreakLifecycleModel", () => {
  it("offers no lifecycle decisions when the payload has no lifecycle state", () => {
    expect(buildBreakLifecycleModel(item(null))).toBeNull();
  });

  it.each<[ReconciliationCaseLifecycleState, boolean, boolean, boolean, boolean]>([
    ["Open", true, true, false, false],
    ["Investigating", true, true, false, false],
    ["Resolved", false, false, true, false],
    ["SignedOff", false, false, false, true],
    ["Superseded", false, false, false, false]
  ])("gates %s: classify=%s waive=%s signOff=%s reopen=%s", (state, classify, waive, signOff, reopen) => {
    const model = buildBreakLifecycleModel(item(state))!;
    expect(model.canClassify).toBe(classify);
    const enabled = Object.fromEntries(model.actions.map((action) => [action.id, action.enabled]));
    expect(enabled).toEqual({ waive, signOff, reopen });
    for (const action of model.actions) {
      if (action.enabled) expect(action.disabledReason).toBeNull();
      else expect(action.disabledReason).toBeTruthy();
    }
  });

  it("disables every change when the version is missing", () => {
    const model = buildBreakLifecycleModel(item("Open", { version: undefined }))!;
    expect(model.canClassify).toBe(false);
    expect(model.actions.every((action) => !action.enabled && action.disabledReason?.includes("version"))).toBe(true);
  });

  it("previews the lifecycle change", () => {
    const waive = buildBreakLifecycleModel(item("Investigating"))!.actions.find((action) => action.id === "waive")!;
    expect(waive.preview).toBe("Investigating → Resolved (waived)");
  });
});

describe("reconciliation casework command", () => {
  const command = { breakId: "cash-1", action: "Waive", actor: "", commandId: "c1", correlationId: "k1", source: "t", expectedVersion: 4 } as ReconciliationCaseworkCommand;

  it("treats HTTP 200 refusals as refusals, not successes", () => {
    const outcome = classifyCaseworkResult(refused("SelfApprovalNotAllowed"), "cash-1");
    expect(outcome).toMatchObject({ kind: "refused", errorCode: "SelfApprovalNotAllowed" });
    expect(outcome.kind === "refused" && outcome.message).toContain("someone other than you");
  });

  it("classifies version conflicts from the result and from HTTP 409", async () => {
    expect(classifyCaseworkResult(refused("ConcurrencyConflict", "Conflict"), "cash-1").kind).toBe("conflict");
    const outcome = await runReconciliationCaseworkCommand(command, () => Promise.reject(new ApiError({ path: "/api/x", status: 409 })));
    expect(outcome.kind).toBe("conflict");
  });

  it("keeps server detail and missing fields after the plain sentence", () => {
    const text = describeCaseworkRefusal({ ...refused("MissingApproval"), validationIssues: ["approvalActor"] });
    expect(text).toContain("Name the approver");
    expect(text).toContain("Server detail: server says no");
    expect(text).toContain("Missing: approvalActor.");
  });

  it("rejects a result for another break", () => {
    expect(classifyCaseworkResult(succeeded(item("Open", { breakId: "other" })), "cash-1").kind).toBe("failed");
  });
});

describe("BreakLifecyclePanel", () => {
  it("sets a root cause with the loaded version and hands the new version to the queue", async () => {
    const services = fakeServices();
    const onUpdated = vi.fn();
    render(<BreakLifecyclePanel item={item("Investigating")} services={services} onUpdated={onUpdated} />);
    const select = await screen.findByLabelText("Root cause");
    await waitFor(() => expect(select).toBeEnabled());
    await userEvent.selectOptions(select, "BrokerCashTiming");
    await userEvent.click(screen.getByRole("button", { name: "Set root cause" }));
    await screen.findByText("Root cause retained in case history.");
    expect(services.setRootCause).toHaveBeenCalledWith(expect.objectContaining({ action: "SetRootCause", expectedVersion: 4, rootCauseCode: "BrokerCashTiming", breakId: "cash-1" }));
    expect(onUpdated).toHaveBeenCalledWith(expect.objectContaining({ version: 5, rootCauseCode: "BrokerCashTiming" }));
  });

  it("previews and validates a waiver before it is sent", async () => {
    const services = fakeServices();
    render(<BreakLifecyclePanel item={item("Investigating")} services={services} />);
    await userEvent.click(screen.getByRole("button", { name: "Waive" }));
    const form = screen.getByRole("form", { name: "Waive this break" });
    expect(form).toHaveTextContent("Lifecycle Investigating → Resolved (waived)");
    const confirm = within(form).getByRole("button", { name: "Confirm waive" });
    expect(confirm).toBeDisabled();
    expect(within(form).getByLabelText("Evidence links (one per line)")).toHaveValue("journal:je-cash-1");
    await userEvent.type(within(form).getByLabelText("Reason"), "Timing difference cleared next day");
    await userEvent.type(within(form).getByLabelText("Approver operator ID"), "controller-2");
    await userEvent.type(within(form).getByLabelText("Approval reference"), "APR-17");
    expect(confirm).toBeEnabled();
    await userEvent.click(confirm);
    await screen.findByText("Waiver retained in case history. The break is resolved.");
    expect(services.waive).toHaveBeenCalledWith(expect.objectContaining({
      action: "Waive", expectedVersion: 4, reason: "Timing difference cleared next day",
      evidenceLinks: ["journal:je-cash-1"], approvalActor: "controller-2", approvalReference: "APR-17"
    }));
  });

  it("shows the plain refusal message and keeps the queue unchanged", async () => {
    const onUpdated = vi.fn();
    const services = fakeServices({ signOff: vi.fn(async () => refused("ResolverSignerConflict")) });
    render(<BreakLifecyclePanel item={item("Resolved")} services={services} onUpdated={onUpdated} />);
    await userEvent.click(screen.getByRole("button", { name: "Sign off" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirm sign off" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("someone else has to sign it off");
    expect(onUpdated).not.toHaveBeenCalled();
  });

  it("asks for a reload after a version conflict and applies the reloaded version", async () => {
    const onUpdated = vi.fn();
    const services = fakeServices({ reopen: vi.fn(async () => refused("ConcurrencyConflict", "Conflict")) });
    render(<BreakLifecyclePanel item={item("SignedOff")} services={services} onUpdated={onUpdated} />);
    await userEvent.click(screen.getByRole("button", { name: "Reopen" }));
    await userEvent.type(screen.getByLabelText("Reason"), "Broker restated the statement");
    await userEvent.click(screen.getByRole("button", { name: "Confirm reopen" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("changed since you opened it");
    await userEvent.click(screen.getByRole("button", { name: "Reload this break" }));
    await waitFor(() => expect(onUpdated).toHaveBeenCalledWith(expect.objectContaining({ version: 9 })));
  });

  it("keeps classification read-only on a signed-off break and has no axe violations", async () => {
    const { container } = render(<BreakLifecyclePanel item={item("SignedOff")} services={fakeServices()} />);
    expect(await screen.findByText(/keeps its root cause and resolution/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Waive" })).toBeDisabled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("renders nothing without a lifecycle state", () => {
    const { container } = render(<BreakLifecyclePanel item={item(null)} services={fakeServices()} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("BreakAuditTimeline", () => {
  const events: ReconciliationBreakQueueAuditEvent[] = [
    { eventId: "e1", breakId: "cash-1", eventType: "Created", newLifecycleState: "Open", occurredAt: "2026-10-01T00:00:00Z", actor: "system", sequence: 1 },
    { eventId: "e2", breakId: "cash-1", eventType: "RootCauseSet", previousLifecycleState: "Open", newLifecycleState: "Investigating", occurredAt: "2026-10-02T00:00:00Z", actor: "ops-1", reason: "Broker cash timing", sequence: 2 }
  ];

  it("orders newest first and labels event types", () => {
    expect(orderAuditEvents(events).map((event) => event.eventId)).toEqual(["e2", "e1"]);
    expect(auditEventLabel("RootCauseSet")).toBe("Root cause set");
  });

  it("loads when opened, shows transitions, and reloads when the version changes", async () => {
    const load = vi.fn(async () => events);
    const { rerender } = render(<BreakAuditTimeline breakId="cash-1" refreshKey={4} load={load} />);
    expect(load).not.toHaveBeenCalled();
    await userEvent.click(screen.getByText("Activity"));
    const list = await screen.findByRole("list", { name: "Break audit history" });
    expect(within(list).getAllByRole("listitem")[0]).toHaveTextContent("Root cause set · Open → Investigating");
    rerender(<BreakAuditTimeline breakId="cash-1" refreshKey={5} load={load} />);
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
  });

  it("says so when nothing is recorded yet", async () => {
    render(<BreakAuditTimeline breakId="cash-1" load={async () => []} />);
    await userEvent.click(screen.getByText("Activity"));
    expect(await screen.findByText("No history recorded yet.")).toBeInTheDocument();
  });
});
