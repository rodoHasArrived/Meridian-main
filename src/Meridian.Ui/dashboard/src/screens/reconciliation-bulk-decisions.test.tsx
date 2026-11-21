import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ReconciliationBulkDecisions } from "./reconciliation-bulk-decisions";
import { buildSuccessfulVerifiedOperationOutcome } from "@/test/verified-operation-outcome";
import type { ReconciliationBreakQueueItem, ReconciliationBulkCaseworkResult } from "@/types";

const cases = [{ breakId: "cash-1", version: 7, permittedCommands: [{ action: "Assign", enabled: true }] },
  { breakId: "cash-2", version: 9, permittedCommands: [{ action: "Assign", enabled: true }] }] as ReconciliationBreakQueueItem[];
function outcome(): ReconciliationBulkCaseworkResult {
  return { bulkActionId: "bulk-1", idempotencyKey: "idempotency-1", dryRun: true, requestedCount: 2, succeededCount: 2, failedCount: 0,
    results: cases.map((item) => ({ breakId: item.breakId, item, succeeded: false, wouldSucceed: true })), inputHashSha256: "hash", outcome: buildSuccessfulVerifiedOperationOutcome() };
}
describe("bulk decision review binding", () => {
  it("sends identical versions and reviewed inputs to execute and displays failures outside disclosure", async () => {
    const user = userEvent.setup();
    const services = { preview: vi.fn().mockResolvedValue(outcome()), execute: vi.fn().mockResolvedValue({ ...outcome(), dryRun: false, succeededCount: 1, failedCount: 1,
      results: [{ breakId: "cash-1", succeeded: true, wouldSucceed: true, item: { ...cases[0], version: 8 } },
        { breakId: "cash-2", succeeded: false, wouldSucceed: false, error: "Case version conflict.", item: { ...cases[1], version: 10 } }] }) };
    render(<ReconciliationBulkDecisions cases={cases} services={services} />);
    await user.type(screen.getByRole("textbox", { name: "Assignee" }), "controller");
    await user.type(screen.getByRole("textbox", { name: "Decision rationale" }), "Review together");
    await user.click(screen.getByRole("button", { name: "Preview selected decisions" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Confirm 2 decisions" })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: "Confirm 2 decisions" }));
    const receipt = await screen.findByRole("region", { name: "Bulk decision receipt" });
    expect(within(receipt).getAllByRole("row")[1]).toHaveTextContent("cash-2");
    expect(receipt).toHaveTextContent("Case version conflict.");
    expect(services.execute).toHaveBeenCalledWith(expect.objectContaining({ expectedVersions: { "cash-1": 7, "cash-2": 9 }, assignee: "controller", reason: "Review together", dryRun: false }));
    expect(services.preview.mock.calls[0][0].expectedVersions).toEqual(services.execute.mock.calls[0][0].expectedVersions);
    expect(services.preview.mock.calls[0][0].idempotencyKey).not.toEqual(services.execute.mock.calls[0][0].idempotencyKey);
    expect(screen.getByRole("button", { name: "Confirm 2 decisions" })).toBeDisabled();
  });

  it("invalidates a preview when selection versions or form inputs change", async () => {
    const user = userEvent.setup();
    const services = { preview: vi.fn().mockResolvedValue(outcome()), execute: vi.fn() };
    const { rerender } = render(<ReconciliationBulkDecisions cases={cases} services={services} />);
    await user.type(screen.getByRole("textbox", { name: "Assignee" }), "ops");
    await user.type(screen.getByRole("textbox", { name: "Decision rationale" }), "Review");
    await user.click(screen.getByRole("button", { name: "Preview selected decisions" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Confirm 2 decisions" })).toBeEnabled());
    rerender(<ReconciliationBulkDecisions cases={[{ ...cases[0], version: 8 }, cases[1]]} services={services} />);
    expect(screen.getByRole("button", { name: "Confirm 2 decisions" })).toBeDisabled();
    expect(services.execute).not.toHaveBeenCalled();
  });
});
