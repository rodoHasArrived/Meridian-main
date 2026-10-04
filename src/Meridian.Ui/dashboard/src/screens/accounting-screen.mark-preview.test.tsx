import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { vi, describe, expect, it } from "vitest";
import { useValuationMarkPreview, ValuationMarkPreviewPanel } from "./accounting-screen.mark-preview";
import { previewValuationMarks } from "@/lib/api/mark-freshness.api";
import type { DailyValuationScheduleWorkItem, ValuationFreshnessPreviewDto } from "@/types";

vi.mock("@/lib/api/mark-freshness.api", () => ({ previewValuationMarks: vi.fn() }));
const schedule = { scheduleId: "daily-1", policyId: "policy-1", nextRunAtUtc: "2026-09-04T00:00:00Z" } as DailyValuationScheduleWorkItem;
const blocked: ValuationFreshnessPreviewDto = {
  policyVersion: "policy-1", assessedPositionCount: 2, blockedPositionCount: 1, affectedValuationCount: 1,
  evaluatedAtUtc: "2026-09-04T00:00:00Z",
  positions: [{ symbol: "AAPL", financialAccountId: "account-1", valuationDate: "2026-09-04", observedOn: "2026-08-01", ageDays: 34,
    policyVersion: "policy-1", status: "ReviewRequired", blockReason: "AAPL observation is stale." }]
};
function Harness({ value = schedule }: { value?: DailyValuationScheduleWorkItem }) {
  const preview = useValuationMarkPreview(value);
  return <><ValuationMarkPreviewPanel preview={preview} /><button disabled={!preview.isCurrent}>Configure valuation</button></>;
}

describe("Valuation mark impact preview", () => {
  it("shows affected counts and position reasons before configuration, and invalidates changed scope", async () => {
    vi.mocked(previewValuationMarks).mockResolvedValue(blocked);
    const view = render(<Harness />);
    expect(screen.getByRole("button", { name: "Configure valuation" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Preview mark impact" }));
    await screen.findByText("1 of 2 positions require review; 1 valuation(s) affected. Policy policy-1.");
    expect(screen.getByText("AAPL observation is stale.")).toBeInTheDocument();
    expect(screen.getByText(/Observed 2026-08-01 · age 34 day/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Configure valuation" })).toBeEnabled();

    view.rerender(<Harness value={{ ...schedule, policyId: "policy-2" }} />);
    expect(screen.getByRole("button", { name: "Configure valuation" })).toBeDisabled();
    expect(screen.queryByText("AAPL observation is stale.")).not.toBeInTheDocument();
  });

  it("fails closed on preview failure and recovers through a new assessment", async () => {
    vi.mocked(previewValuationMarks).mockRejectedValueOnce(new Error("Historical mark source unavailable."))
      .mockResolvedValueOnce({ ...blocked, blockedPositionCount: 0, affectedValuationCount: 0, positions: [] });
    render(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "Preview mark impact" }));
    await screen.findByText("Historical mark source unavailable.");
    expect(screen.getByRole("button", { name: "Configure valuation" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Preview mark impact" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Configure valuation" })).toBeEnabled());
    expect(screen.queryByText("Historical mark source unavailable.")).not.toBeInTheDocument();
  });
});
