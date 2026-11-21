import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { ApprovalCollaborationPanel } from "@/components/meridian/approval-collaboration-panel";
import { applyApprovalCollaboration, getApprovalCollaboration, type ApprovalCollaboration } from "@/lib/approval-collaboration";

vi.mock("@/lib/approval-collaboration", () => ({ applyApprovalCollaboration: vi.fn(), getApprovalCollaboration: vi.fn() }));
const response: ApprovalCollaboration = { workflowId: "workflow-1", version: 7, approvalState: "Submitted", currentApprovalId: "approval-1", submitter: "preparer", reviewer: "reviewer-1",
  comments: [], approvalHistory: [{ approvalId: "approval-1", status: "Submitted", operator: "preparer", reviewer: "reviewer-1", rationale: "Evidence ready", submittedAtUtc: "2026-01-01T00:00:00Z", decidedAtUtc: null }], canComment: true, canReassign: true, disabledReason: null };

describe("approval collaboration", () => {
  beforeEach(() => { vi.clearAllMocks(); vi.mocked(getApprovalCollaboration).mockResolvedValue(response); });
  it("retains a comment with exact approval/version and never calls a decision command", async () => {
    const user = userEvent.setup();
    vi.mocked(applyApprovalCollaboration).mockResolvedValue({ success: true });
    const onChanged = vi.fn();
    renderWithRouter(<ApprovalCollaborationPanel workflowId="workflow-1" onChanged={onChanged} />);
    await screen.findByText("No retained comments.");
    await user.type(screen.getByLabelText("Comment"), "Inspect the retained cash statement.");
    await user.type(screen.getByLabelText("Mention operator IDs · comma separated"), "cash-reviewer, controller");
    await user.click(screen.getByRole("button", { name: "Retain comment" }));
    await waitFor(() => expect(applyApprovalCollaboration).toHaveBeenCalledWith("workflow-1", "comment", expect.objectContaining({ expectedVersion: 7, approvalId: "approval-1", commandId: expect.any(String), body: "Inspect the retained cash statement.", mentions: ["cash-reviewer", "controller"], actor: "" }), expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(await screen.findByText("Comment retained. Approval state is unchanged.")).toBeInTheDocument();
  });
  it("requires an explicit independent reviewer and reason for reassignment", async () => {
    const user = userEvent.setup(); vi.mocked(applyApprovalCollaboration).mockResolvedValue({ success: false, errorMessage: "Reviewer must differ from submitter." });
    renderWithRouter(<ApprovalCollaborationPanel workflowId="workflow-1" />);
    await screen.findByText("No retained comments.");
    await user.click(screen.getByText("Reassign reviewer"));
    expect(screen.getByRole("button", { name: "Retain reassignment" })).toBeDisabled();
    await user.type(screen.getByLabelText("Independent reviewer operator ID"), "preparer");
    await user.type(screen.getByLabelText("Reassignment reason"), "Cover rotation");
    await user.click(screen.getByRole("button", { name: "Retain reassignment" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Reviewer must differ from submitter");
    expect(screen.queryByRole("button", { name: "Retry same approval command" })).not.toBeInTheDocument();
  });
  it("retries the same command after interruption while withholding other writes", async () => {
    const user = userEvent.setup();
    vi.mocked(applyApprovalCollaboration).mockRejectedValueOnce(new Error("connection interrupted")).mockResolvedValueOnce({ success: true });
    renderWithRouter(<ApprovalCollaborationPanel workflowId="workflow-1" />);
    await screen.findByText("No retained comments.");
    await user.type(screen.getByLabelText("Comment"), "Review evidence.");
    await user.click(screen.getByRole("button", { name: "Retain comment" }));
    await user.click(await screen.findByRole("button", { name: "Retry same approval command" }));
    await waitFor(() => expect(applyApprovalCollaboration).toHaveBeenCalledTimes(2));
    expect(vi.mocked(applyApprovalCollaboration).mock.calls[0][2]).toEqual(vi.mocked(applyApprovalCollaboration).mock.calls[1][2]);
  });
  it("withholds stale workflow discussion during a delayed scope switch", async () => {
    const user = userEvent.setup();
    const { rerender } = renderWithRouter(<ApprovalCollaborationPanel workflowId="workflow-1" />);
    await screen.findByText("No retained comments.");
    await user.type(screen.getByLabelText("Comment"), "Old workflow draft");
    vi.mocked(getApprovalCollaboration).mockReturnValue(new Promise(() => {}));
    rerender(<ApprovalCollaborationPanel workflowId="workflow-2" />);
    expect(screen.queryByDisplayValue("Old workflow draft")).not.toBeInTheDocument();
    expect(screen.queryByText("Reviewer: reviewer-1")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retain comment" })).not.toBeInTheDocument();
  });
  it("keeps finalized histories read-only and accessible", async () => {
    vi.mocked(getApprovalCollaboration).mockResolvedValue({ ...response, approvalState: "Approved", canComment: false, canReassign: false, disabledReason: "Discussion requires a pending approval." });
    const { container } = renderWithRouter(<ApprovalCollaborationPanel workflowId="workflow-1" />);
    await screen.findByText("No retained comments.");
    expect(screen.getByLabelText("Comment")).toBeDisabled();
    expect((await axe(container)).violations).toEqual([]);
  });
});
