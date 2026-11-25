import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import type { SecurityMasterExceptionCaseQueueItem } from "@/lib/api/security-master-review-queues.api";
import { SecurityMasterExceptionQueuePanel } from "./security-master-exception-queue";

function item(caseId: string, title: string, slaBreached: boolean, securityId: string | null): SecurityMasterExceptionCaseQueueItem {
  return {
    row: {
      caseId,
      caseKey: `k-${caseId}`,
      category: "Conflict",
      ruleCode: "SM-CONFLICT",
      securityId,
      severity: "High",
      title,
      detail: "vendor-a and vendor-b disagree on the coupon.",
      status: "Open",
      assignee: null,
      openedAt: "2026-11-23T06:00:00Z",
      lastSeenAt: "2026-11-24T06:00:00Z",
      slaDueAt: "2026-11-24T12:00:00Z",
      closedAt: null,
      closedReason: null
    },
    slaBreached,
    ageHours: 30
  };
}

const breached = item("c1111111-1111-1111-1111-111111111111", "Conflicting coupon", true, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
const onTime = item("c2222222-2222-2222-2222-222222222222", "Ambiguous ticker", false, null);

describe("SecurityMasterExceptionQueuePanel", () => {
  it("shows the server's SLA verdict, the breach count and an open-security action", async () => {
    const onOpenSecurity = vi.fn();
    render(<SecurityMasterExceptionQueuePanel load={() => Promise.resolve([breached, onTime])} onOpenSecurity={onOpenSecurity} />);

    expect(await screen.findByText("2 open case(s); 1 past SLA.")).toBeInTheDocument();
    expect(screen.getByText("Breached (due 2026-11-24 12:00 UTC)")).toBeInTheDocument();
    expect(screen.getByText("Due 2026-11-24 12:00 UTC")).toBeInTheDocument();
    expect(screen.getAllByText("Unassigned")).toHaveLength(2);
    expect(screen.getAllByText("30h")).toHaveLength(2);

    const open = screen.getAllByRole("button", { name: "Open security" });
    expect(open).toHaveLength(1);
    await userEvent.click(open[0]);
    expect(onOpenSecurity).toHaveBeenCalledWith(breached.row.securityId);
  });

  it("syncs, reports the counts and reloads", async () => {
    const load = vi.fn().mockResolvedValue([]);
    const sync = vi.fn().mockResolvedValue({ opened: 3, refreshed: 4, closed: 1 });
    render(<SecurityMasterExceptionQueuePanel load={load} sync={sync} />);
    expect(await screen.findByText("No open cases.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Sync cases" }));

    expect(await screen.findByText("Sync opened 3, refreshed 4 and closed 1 case(s).")).toBeInTheDocument();
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
  });

  it("assigns the selected open case to the named operator", async () => {
    const assign = vi.fn().mockResolvedValue(undefined);
    render(<SecurityMasterExceptionQueuePanel load={() => Promise.resolve([breached])} assign={assign} defaultAssignee=" carol.ops " />);

    const assignButton = await screen.findByRole("button", { name: "Assign" });
    expect(assignButton).toBeDisabled();
    await userEvent.click(screen.getByRole("radio", { name: "Select Conflicting coupon" }));
    expect(assignButton).toBeEnabled();

    await userEvent.click(assignButton);

    expect(assign).toHaveBeenCalledWith(breached.row.caseId, "carol.ops");
    expect(await screen.findByText('Assigned "Conflicting coupon" to carol.ops.')).toBeInTheDocument();
  });

  it("names the server's reason when the case closed meanwhile", async () => {
    const assign = vi.fn().mockRejectedValue(new ApiError({ path: "/x", status: 409, responseBody: `{"error":"Case '${breached.row.caseId}' is not open."}` }));
    render(<SecurityMasterExceptionQueuePanel load={() => Promise.resolve([breached])} assign={assign} defaultAssignee="carol.ops" />);

    await userEvent.click(await screen.findByRole("radio", { name: "Select Conflicting coupon" }));
    await userEvent.click(screen.getByRole("button", { name: "Assign" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(`Case '${breached.row.caseId}' is not open.`);
  });

  it("explains a forbidden queue", async () => {
    render(<SecurityMasterExceptionQueuePanel load={() => Promise.reject(new ApiError({ path: "/x", status: 403 }))} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("You don't have permission to view the Security Master exception queue.");
  });
});
