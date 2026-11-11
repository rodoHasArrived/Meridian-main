import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { OperatorTriageQueue, triageAgeLabel } from "@/components/meridian/operator-triage-queue";
import { renderWithRouter } from "@/test/render";
import { bulkAssignOperatorTriage, commandOperatorTriage, getOperatorTriageInbox, type OperatorTriageInbox, type OperatorTriageItem } from "@/lib/operator-inbox-triage";

vi.mock("@/lib/operator-inbox-triage", async (load) => ({ ...await load<typeof import("@/lib/operator-inbox-triage")>(), getOperatorTriageInbox: vi.fn(), commandOperatorTriage: vi.fn(), bulkAssignOperatorTriage: vi.fn() }));
vi.mock("@/lib/table-workbench", async (load) => ({ ...await load<typeof import("@/lib/table-workbench")>(), loadTableViews: vi.fn(async () => []), saveTableView: vi.fn() }));

const blocked: OperatorTriageItem = { workItemId: "critical-1", label: "Cash variance", detail: "Cash requires governed reconciliation.", workspace: "accounting", targetRoute: "/accounting/reconciliation", kind: "ReconciliationBreak",
  tone: "Critical", createdAt: "2026-01-01T00:00:00Z", ownerId: null, dueAt: null, version: 2, sourceVersion: 4, acknowledgedAt: null, snoozedUntil: "2030-01-01T00:00:00Z",
  capabilities: { canAssign: true, canAcknowledge: true, canSnooze: true, canUnsnooze: false, disabledReason: null }, activity: [] };
const review: OperatorTriageItem = { ...blocked, workItemId: "review-2", label: "Report review", detail: "Pack needs review.", workspace: "reporting", targetRoute: "/reporting/report-packs", tone: "Warning", ownerId: "operator-1", snoozedUntil: null };
const response: OperatorTriageInbox = { asOf: "2026-01-02T00:00:00Z", actorId: "operator-1", items: [blocked, review], persistenceAvailable: true, unavailableReason: null };

async function mount() {
  const result = renderWithRouter(<OperatorTriageQueue fallback={<p>Loaded workspace evidence</p>} />);
  await screen.findByRole("treegrid", { name: "Authorized ranked operator queue" });
  return result;
}

describe("authorized operator triage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getOperatorTriageInbox).mockResolvedValue(response);
  });

  it("shows the authorized queue immediately with separate workspace and owner, and never snoozes a blocker", async () => {
    await mount();
    expect(getOperatorTriageInbox).toHaveBeenCalledWith(undefined, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByRole("columnheader", { name: "Workspace" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Owner" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Cash variance" })).toHaveTextContent("Unassigned");
    expect(screen.getByRole("row", { name: "Cash variance" })).toHaveTextContent("No due time");
    expect(screen.getByRole("button", { name: "Snooze" })).toBeDisabled();
    expect(screen.getByRole("row", { name: "Cash variance" })).toHaveTextContent("Open");
    expect(screen.queryByRole("button", { name: "Resolve" })).not.toBeInTheDocument();
  });

  it("My items uses the authenticated actor rather than the workspace label", async () => {
    const user = userEvent.setup(); await mount();
    await user.click(screen.getByRole("checkbox", { name: "My items" }));
    expect(screen.getByRole("row", { name: "Report review" })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: "Cash variance" })).not.toBeInTheDocument();
  });

  it("requires a reason and sends authoritative versions for assignment, then refreshes", async () => {
    const user = userEvent.setup(); await mount();
    vi.mocked(commandOperatorTriage).mockImplementation(async () => {
      vi.mocked(getOperatorTriageInbox).mockResolvedValue({ ...response, items: [{ ...blocked, ownerId: "cash-reviewer", version: 3 }, review] });
      return { status: "Applied", workItemId: blocked.workItemId, replayed: false };
    });
    await user.click(screen.getByRole("button", { name: "Assign" }));
    expect(screen.getByRole("button", { name: "Apply triage action" })).toBeDisabled();
    await user.type(screen.getByLabelText("Assignee operator ID"), "cash-reviewer");
    await user.type(screen.getByLabelText("Reason"), "Cash desk investigation");
    await user.click(screen.getByRole("button", { name: "Apply triage action" }));
    await waitFor(() => expect(commandOperatorTriage).toHaveBeenCalledWith("critical-1", expect.objectContaining({ commandId: expect.any(String), action: "Assign", assigneeId: "cash-reviewer", reason: "Cash desk investigation", expectedVersion: 2, expectedSourceVersion: 4 }), expect.any(Object)));
    await waitFor(() => expect(screen.getByRole("row", { name: "Cash variance" })).toHaveTextContent("cash-reviewer"));
    expect(screen.getByRole("row", { name: "Cash variance" })).toHaveTextContent("Blocked");
  });

  it("retains the command ID after an interrupted write and disables unrelated actions", async () => {
    const user = userEvent.setup(); await mount();
    vi.mocked(commandOperatorTriage).mockRejectedValueOnce(new Error("connection interrupted")).mockResolvedValueOnce({ status: "Applied", workItemId: blocked.workItemId, replayed: true });
    await user.click(screen.getByRole("button", { name: "Acknowledge" }));
    await user.type(screen.getByLabelText("Reason"), "Investigation started");
    await user.click(screen.getByRole("button", { name: "Apply triage action" }));
    const retry = await screen.findByRole("button", { name: "Retry same command" });
    expect(screen.getByRole("button", { name: "Assign" })).toBeDisabled();
    await user.click(retry);
    await waitFor(() => expect(commandOperatorTriage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(commandOperatorTriage).mock.calls[0][1]).toEqual(vi.mocked(commandOperatorTriage).mock.calls[1][1]);
  });

  it("reports bulk per-item refusals instead of optimistic all-success", async () => {
    const user = userEvent.setup(); await mount();
    vi.mocked(bulkAssignOperatorTriage).mockResolvedValue({ results: [{ status: "Applied", workItemId: blocked.workItemId, replayed: false }, { status: "Conflict", workItemId: review.workItemId, error: "Source version changed", replayed: false }] });
    await user.click(screen.getByRole("checkbox", { name: "Select Cash variance for assignment" }));
    await user.click(screen.getByRole("checkbox", { name: "Select Report review for assignment" }));
    await user.click(screen.getByRole("button", { name: "Assign 2 selected" }));
    await user.type(screen.getByLabelText("Assignee operator ID"), "reviewer");
    await user.type(screen.getByLabelText("Reason"), "Desk allocation");
    await user.click(screen.getByRole("button", { name: "Apply triage action" }));
    expect(await screen.findByText(/1 applied; 1 not applied: review-2: Source version changed/)).toBeInTheDocument();
    expect(bulkAssignOperatorTriage).toHaveBeenCalledWith(expect.objectContaining({ items: [{ workItemId: "critical-1", expectedVersion: 2, expectedSourceVersion: 4 }, { workItemId: "review-2", expectedVersion: 2, expectedSourceVersion: 4 }] }), expect.any(Object));
  });

  it("keeps unavailable storage read-only and distinguishes a failed inbox from empty work", async () => {
    vi.mocked(getOperatorTriageInbox).mockResolvedValue({ ...response, persistenceAvailable: false, unavailableReason: "Production triage storage is unavailable." });
    const { unmount } = await mount();
    expect(screen.getByRole("alert")).toHaveTextContent("Production triage storage is unavailable");
    expect(screen.getByRole("button", { name: "Acknowledge" })).toBeDisabled();
    unmount();
    vi.mocked(getOperatorTriageInbox).mockRejectedValue(new Error("Forbidden"));
    renderWithRouter(<OperatorTriageQueue fallback={<p>Loaded workspace evidence</p>} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Inbox triage is unavailable");
    expect(screen.getByText("Loaded workspace evidence")).toBeInTheDocument();
    expect(screen.queryByText("No authorized items match these filters.")).not.toBeInTheDocument();
  });

  it("supports queue-local j/k selection and does not turn d/r into source mutations", async () => {
    const user = userEvent.setup(); await mount();
    screen.getByRole("row", { name: "Cash variance" }).focus();
    await user.keyboard("j");
    await waitFor(() => expect(screen.getByRole("row", { name: "Report review" })).toHaveFocus());
    await user.keyboard("dr");
    expect(commandOperatorTriage).not.toHaveBeenCalled();
    await user.keyboard("a");
    expect(screen.getByRole("dialog")).toHaveAccessibleName("Assign item");
    await user.type(screen.getByLabelText("Reason"), "jkdra");
    expect(screen.getByLabelText("Reason")).toHaveValue("jkdra");
    expect(commandOperatorTriage).not.toHaveBeenCalled();
  });

  it("has accessible table/filter/action semantics and truthful missing ages", async () => {
    const { container } = await mount();
    const result = await axe(container);
    expect(result.violations).toEqual([]);
    expect(triageAgeLabel(null)).toBe("Age unavailable");
    expect(triageAgeLabel("2030-01-01", Date.parse("2026-01-01"))).toBe("Age unavailable");
  });
});
