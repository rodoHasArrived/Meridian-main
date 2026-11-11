import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ReconciliationCaseDiscussion, type CaseDiscussionServices } from "./accounting-screen.case-discussion";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkOperationResult } from "@/types";

function item(id = "case-a", version = 3): ReconciliationBreakQueueItem {
  return { breakId: id, version, comments: [], assignedTo: null } as unknown as ReconciliationBreakQueueItem;
}
function success(updated: ReconciliationBreakQueueItem): ReconciliationCaseworkOperationResult {
  return { item: updated, outcome: { state: "Succeeded", issues: [] } } as unknown as ReconciliationCaseworkOperationResult;
}
function services(): CaseDiscussionServices {
  return { read: vi.fn().mockResolvedValue(item()), comment: vi.fn().mockResolvedValue(success(item("case-a", 4))), assign: vi.fn().mockResolvedValue(success(item("case-a", 4))) };
}
async function open() {
  fireEvent.click(screen.getByText("Discussion and assignment"));
  await screen.findByText("No retained comments.");
}

it("submits retained case version and explicit mention IDs without resolving the case", async () => {
  const api = services(); const updated = vi.fn();
  render(<ReconciliationCaseDiscussion breakId="case-a" services={api} onUpdated={updated} />);
  await open();
  fireEvent.change(screen.getByLabelText("Comment"), { target: { value: "Reviewed retained variance evidence." } });
  fireEvent.change(screen.getByLabelText("Mention operator IDs (comma separated)"), { target: { value: "op-one, op-two, op-one" } });
  fireEvent.click(screen.getByRole("button", { name: "Add comment" }));
  await screen.findByText("Comment retained in case history.");
  expect(api.comment).toHaveBeenCalledWith(expect.objectContaining({ breakId: "case-a", action: "AddComment", expectedVersion: 3, note: "Reviewed retained variance evidence.", mentions: ["op-one", "op-two"] }));
  expect(updated).toHaveBeenCalledWith(expect.objectContaining({ version: 4 }));
  expect(api.assign).not.toHaveBeenCalled();
});

it("retries an uncertain comment with its original command identity", async () => {
  const api = services();
  vi.mocked(api.comment).mockRejectedValueOnce(new Error("Connection interrupted"));
  render(<ReconciliationCaseDiscussion breakId="case-a" services={api} />);
  await open();
  fireEvent.change(screen.getByLabelText("Comment"), { target: { value: "Retain this note once." } });
  fireEvent.click(screen.getByRole("button", { name: "Add comment" }));
  fireEvent.click(await screen.findByRole("button", { name: "Retry same casework command" }));
  await screen.findByText("Comment retained in case history.");
  const calls = vi.mocked(api.comment).mock.calls;
  expect(calls).toHaveLength(2);
  expect(calls[1][0]).toEqual(calls[0][0]);
});

it("ignores a late previous-case read and refuses unversioned changes", async () => {
  const api = services();
  let finish!: (row: ReconciliationBreakQueueItem) => void;
  vi.mocked(api.read).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; })).mockResolvedValue({ ...item("case-b"), version: undefined });
  const view = render(<ReconciliationCaseDiscussion breakId="case-a" services={api} />);
  fireEvent.click(screen.getByText("Discussion and assignment"));
  await waitFor(() => expect(api.read).toHaveBeenCalledWith("case-a"));
  view.rerender(<ReconciliationCaseDiscussion breakId="case-b" services={api} />);
  await screen.findByText("Case version unavailable; changes are disabled.");
  await act(async () => finish({ ...item(), comments: [{ commentId: "old", body: "Foreign note", createdAt: "2026-01-01T00:00:00Z", authorId: "old", authorDisplayName: "Old operator", evidenceLinks: [], visibility: "Internal" }] }));
  expect(screen.queryByText("Foreign note")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Add comment" })).toBeDisabled();
  expect(api.comment).not.toHaveBeenCalled();
});
