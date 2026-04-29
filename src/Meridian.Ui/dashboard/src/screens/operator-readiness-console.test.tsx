import { screen, waitFor } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import * as api from "@/lib/api";
import { OperatorReadinessConsole } from "@/screens/operator-readiness-console";
import { renderWithRouter } from "@/test/render";
import type { OperatorInbox, OperatorWorkItem } from "@/types";

const inbox: OperatorInbox = {
  asOf: "2026-04-29T12:01:00Z",
  items: [
    {
      workItemId: "promotion-review-run-1",
      kind: "PromotionReview",
      label: "Promotion checklist incomplete",
      detail: "Finish continuity review.",
      tone: "Warning",
      createdAt: "2026-04-29T12:00:00Z",
      runId: "run-1",
      fundAccountId: "fund-1",
      auditReference: "audit-promotion-1",
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "TradingReadinessConsole"
    }
  ],
  criticalCount: 0,
  warningCount: 1,
  reviewCount: 1,
  summary: "1 review item needs attention."
};

const warningItems: OperatorWorkItem[] = Array.from({ length: 6 }, (_, index) => ({
  ...inbox.items[0],
  workItemId: `warning-${index}`,
  kind: "ReportPackApproval",
  label: `Warning item ${index}`,
  detail: `Warning detail ${index}`,
  tone: "Warning",
  createdAt: `2026-04-29T12:0${index}:00Z`,
  workspace: "Reporting",
  targetRoute: "/reporting",
  targetPageTag: "ReportPackApproval"
}));

const criticalItem: OperatorWorkItem = {
  ...inbox.items[0],
  workItemId: "critical-security-gap",
  kind: "SecurityMasterCoverage",
  label: "Critical security coverage gap",
  detail: "Resolve missing identifier coverage before accepting readiness.",
  tone: "Critical",
  createdAt: "2026-04-29T11:00:00Z",
  workspace: "Accounting",
  targetRoute: "/accounting/security-master",
  targetPageTag: "SecurityMaster"
};

describe("OperatorReadinessConsole", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders operator inbox work-item routes as accessible next actions", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce(inbox);

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const action = await screen.findByRole("link", {
      name: "Open promotion review: Promotion checklist incomplete"
    });

    expect(action).toHaveAttribute("href", "/trading/readiness");
    expect(screen.getAllByText("Promotion checklist incomplete").length).toBeGreaterThan(0);
    await waitFor(() => expect(api.getOperatorInbox).toHaveBeenCalledTimes(1));
  });

  it("shows critical operator inbox items before lower-priority overflow", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValueOnce({
      ...inbox,
      items: [...warningItems, criticalItem],
      criticalCount: 1,
      warningCount: 6,
      reviewCount: 7,
      summary: "7 review items need attention."
    });

    renderWithRouter(
      <OperatorReadinessConsole
        research={null}
        trading={null}
        dataOperations={null}
        governance={null}
      />,
      { initialEntries: ["/trading/readiness"] }
    );

    const criticalAction = await screen.findByRole("link", {
      name: "Open Security Master: Critical security coverage gap"
    });

    expect(criticalAction).toHaveAttribute("href", "/accounting/security-master");
    expect(screen.getByText("Showing 6 of 7 operator work items; 1 critical item, 6 warnings. Critical items sort first.")).toBeInTheDocument();
    expect(screen.getByText("1 additional work item hidden from this view after priority sorting.")).toBeInTheDocument();
  });
});
