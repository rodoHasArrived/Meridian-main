import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as rebuildApi from "@/lib/api/break-audit-rebuild.api";
import * as api from "@/lib/api";
import { BreakAuditRebuildCheck } from "@/screens/accounting-screen.break-audit-rebuild";
import type { ReconciliationBreakQueueItem } from "@/types";

vi.mock("@/lib/api/break-audit-rebuild.api", () => ({
  getReconciliationBreakRebuiltSnapshot: vi.fn()
}));

vi.mock("@/lib/api", () => ({
  getReconciliationBreakDetail: vi.fn()
}));

const rebuilt = vi.mocked(rebuildApi);
const detail = vi.mocked(api);

afterEach(() => {
  vi.resetAllMocks();
});

function item(overrides: Partial<ReconciliationBreakQueueItem> = {}): ReconciliationBreakQueueItem {
  return {
    breakId: "break-1",
    runId: "run-42",
    strategyName: "Cash sweep",
    category: "Cash",
    status: "Open",
    variance: -1250.5,
    reason: "Statement cash row has no book counterpart.",
    assignedTo: "j.rowe",
    detectedAt: "2026-08-24T09:00:00Z",
    lastUpdatedAt: "2026-08-25T11:00:00Z",
    reviewedBy: null,
    reviewedAt: null,
    resolvedBy: null,
    resolvedAt: null,
    resolutionNote: null,
    ...overrides
  } as ReconciliationBreakQueueItem;
}

describe("BreakAuditRebuildCheck", () => {
  it("does nothing until asked, because replaying a trail is not free", () => {
    render(<BreakAuditRebuildCheck breakId="break-1" />);

    expect(rebuilt.getReconciliationBreakRebuiltSnapshot).not.toHaveBeenCalled();
    expect(detail.getReconciliationBreakDetail).not.toHaveBeenCalled();
  });

  it("disables the control when no break is selected", () => {
    render(<BreakAuditRebuildCheck breakId={null} />);

    expect(screen.getByRole("button", { name: /No break is selected/ })).toBeDisabled();
  });

  it("confirms agreement between the stored break and its audit trail", async () => {
    detail.getReconciliationBreakDetail.mockResolvedValue(item());
    rebuilt.getReconciliationBreakRebuiltSnapshot.mockResolvedValue(item());
    render(<BreakAuditRebuildCheck breakId="break-1" />);

    await userEvent.click(screen.getByRole("button", { name: /Rebuild break break-1/ }));

    expect(await screen.findByText("Audit trail agrees")).toBeInTheDocument();
  });

  it("names each field where the stored break and its trail disagree", async () => {
    detail.getReconciliationBreakDetail.mockResolvedValue(item({ status: "Resolved" }));
    rebuilt.getReconciliationBreakRebuiltSnapshot.mockResolvedValue(item());
    render(<BreakAuditRebuildCheck breakId="break-1" />);

    await userEvent.click(screen.getByRole("button", { name: /Rebuild break break-1/ }));

    expect(await screen.findByText("Audit trail disagrees")).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: "status" })).toBeInTheDocument();
    expect(screen.getByText("Resolved")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
  });

  it("fetches both halves together so the comparison is of two current reads", async () => {
    detail.getReconciliationBreakDetail.mockResolvedValue(item());
    rebuilt.getReconciliationBreakRebuiltSnapshot.mockResolvedValue(item());
    render(<BreakAuditRebuildCheck breakId="break-1" />);

    await userEvent.click(screen.getByRole("button", { name: /Rebuild break break-1/ }));

    await waitFor(() => expect(detail.getReconciliationBreakDetail).toHaveBeenCalledWith("break-1"));
    expect(rebuilt.getReconciliationBreakRebuiltSnapshot).toHaveBeenCalledWith("break-1");
  });

  it("reports a failed rebuild instead of leaving a stale verdict on screen", async () => {
    detail.getReconciliationBreakDetail.mockResolvedValue(item());
    rebuilt.getReconciliationBreakRebuiltSnapshot.mockRejectedValue(new Error("rebuild route unavailable"));
    render(<BreakAuditRebuildCheck breakId="break-1" />);

    await userEvent.click(screen.getByRole("button", { name: /Rebuild break break-1/ }));

    expect(await screen.findByText("rebuild route unavailable")).toBeInTheDocument();
    expect(screen.queryByText("Audit trail agrees")).not.toBeInTheDocument();
  });
});
